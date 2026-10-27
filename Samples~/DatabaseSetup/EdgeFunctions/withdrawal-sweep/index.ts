// withdrawal-sweep
// 탈퇴 유예가 지난 계정을 지운다(Sign in with Apple 연결을 먼저 끊고 auth 계정 삭제).
//
// 예전에는 그 계정이 다시 로그인할 때 withdrawal-guard 만 지웠다 — 다시 안 들어온 탈퇴자는 영영 남았다.
// 절차는 withdrawal-guard 와 같다: account_closures 기록 → 애플 철회(실패해도 삭제는 진행) → auth 계정 삭제.
//
// 부르는 곳: install.sql 9절 cron(매일 02:00 UTC, pg_net). 지우는 대상은 서버가 정하고(유예가 지난 계정만,
// 유예가 지나면 취소도 막힌다) 10분 안의 재호출은 건너뛰므로 인증 없이 받는다(verify_jwt 끔). cron 이 키 없이 부를 수 있게 하려는 것이다.
// 응답에는 성공 여부만 싣는다. 실패는 로그에 남기고, 하루 뒤 ts_withdrawal_overdue_check 가 남은 계정을 보고 실패로 끝낸다.
//
// 필요한 값: APPLE_TEAM_ID·APPLE_KEY_ID·APPLE_PRIVATE_KEY (운영 콘솔 "설정" 또는 Edge Function Secrets, apple-token 과 같은 값).
// 없으면 애플 철회만 건너뛰고 삭제는 한다.

import { createClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN server-config ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 스토어·애플 연동 값. 운영 콘솔 "설정"에서 넣은 값(DB Vault)을 먼저 쓰고, 없으면 Edge Function Secrets 를 쓴다.
// 1분 동안 기억해 둔다 — 콘솔에서 바꾼 값은 늦어도 1분 뒤부터 걸린다. 읽기에 실패하면 기억하지 않고 Secrets 로 넘어간다.
let serverConfigCache: { at: number; values: Record<string, string> } | null = null;

async function serverConfig(key: string): Promise<string> {
  if (!serverConfigCache || Date.now() - serverConfigCache.at > 60_000) {
    const db = createClient(Deno.env.get("SUPABASE_URL")!, JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default, {
      auth: { autoRefreshToken: false, persistSession: false },
    });
    const { data, error } = await db.rpc("ts_server_config_get");
    if (error) console.warn(`[server-config] 콘솔 설정값을 읽지 못해 Secrets 를 씁니다: ${error.message}`);
    else serverConfigCache = { at: Date.now(), values: (data ?? {}) as Record<string, string> };
  }
  const fromConsole = (serverConfigCache?.values[key] ?? "").trim();
  return fromConsole !== "" ? fromConsole : (Deno.env.get(key) ?? "").trim();
}

async function serverConfigList(key: string): Promise<string[]> {
  return (await serverConfig(key)).split(",").map((s) => s.trim()).filter((s) => s.length > 0);
}
// ═══ END server-config ═══

// ═══ BEGIN apple-revoke ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// Sign in with Apple 토큰 철회. apple-token(탈퇴 신청·계정 삭제 직전)과 withdrawal-sweep(유예 지난 계정 삭제)이 쓴다.
type AppleKeys = { teamId: string; keyId: string; privateKey: string };

const APPLE_REVOKE_URL = "https://appleid.apple.com/auth/revoke";

const b64url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

const b64urlText = (text: string) => b64url(new TextEncoder().encode(text));

// 애플 문서 "Creating a client secret": ES256, kid=Key ID, iss=팀 ID, sub=client_id, aud=https://appleid.apple.com.
// 요청 한 번에 쓰고 버리므로 만료는 짧게 둔다(최대 6개월까지 허용된다).
async function createClientSecret(keys: AppleKeys, clientId: string): Promise<string> {
  // 대시보드에 붙여넣다 줄바꿈이 글자 그대로의 "\n" 으로 들어가는 경우가 있어 그것도 걷어 낸다.
  const pem = keys.privateKey.replace(/-----[^-]+-----/g, "").replace(/\\n/g, "").replace(/\s+/g, "");
  const der = Uint8Array.from(atob(pem), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("pkcs8", der, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);

  const now = Math.floor(Date.now() / 1000);
  const signingInput =
    b64urlText(JSON.stringify({ alg: "ES256", kid: keys.keyId })) + "." +
    b64urlText(JSON.stringify({ iss: keys.teamId, iat: now, exp: now + 300, aud: "https://appleid.apple.com", sub: clientId }));

  // WebCrypto ECDSA 서명은 JWS 가 요구하는 r||s(64바이트) 형식 그대로다.
  const sig = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key, new TextEncoder().encode(signingInput)));
  return `${signingInput}.${b64url(sig)}`;
}

// 애플이 늦으면 SDK 쪽 호출(탈퇴 신청·로그인·withdrawal-guard)이 통째로 멈춘다. 짧게 끊는다.
async function postForm(url: string, form: Record<string, string>): Promise<Response> {
  return await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams(form),
    signal: AbortSignal.timeout(8000),
  });
}

async function revokeRefreshToken(keys: AppleKeys, clientId: string, refreshToken: string): Promise<{ ok: boolean; detail?: string }> {
  const res = await postForm(APPLE_REVOKE_URL, {
    client_id: clientId,
    client_secret: await createClientSecret(keys, clientId),
    token: refreshToken,
    token_type_hint: "refresh_token",
  });
  // 200 = 철회됨 또는 이미 무효였음(애플 문서). 둘 다 더 할 일이 없다.
  if (res.ok) return { ok: true };
  return { ok: false, detail: `${res.status} ${await res.text()}` };
}
// ═══ END apple-revoke ═══

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const secretKeys          = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

// 한 번에 지우는 최대 수. 남으면 다음 날 이어서 한다(대개 하루치는 이보다 훨씬 적다).
const BATCH = 100;
const MIN_GAP_SECONDS = 600;

Deno.serve(async () => {
  const reply = (status: number, body: Record<string, unknown>) =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  // 동시에 여러 번 불리면 같은 목록을 두 번 처리해 애플 철회가 겹치고 두 번째 삭제가 실패로 남는다.
  const { data: started, error: startError } = await db.rpc("ts_withdrawal_sweep_try_start", { p_min_gap_seconds: MIN_GAP_SECONDS });
  if (startError) {
    console.error(`[withdrawal-sweep] 시작 기록 실패: ${startError.message}`);
    return reply(500, { ok: false });
  }
  if (!started) return reply(200, { ok: true, skipped: true });

  const { data: due, error: dueError } = await db.rpc("ts_withdrawal_due_accounts", { p_limit: BATCH });
  if (dueError) {
    console.error(`[withdrawal-sweep] 대상 조회 실패: ${dueError.message}`);
    return reply(500, { ok: false });
  }
  const rows = (due ?? []) as { account_id: string; user_id: string }[];
  if (rows.length === 0) return reply(200, { ok: true });

  const keys: AppleKeys = {
    teamId: await serverConfig("APPLE_TEAM_ID"),
    keyId: await serverConfig("APPLE_KEY_ID"),
    privateKey: await serverConfig("APPLE_PRIVATE_KEY"),
  };
  const appleConfigured = !!(keys.teamId && keys.keyId && keys.privateKey);

  let deleted = 0;
  let failed = 0;
  for (const row of rows) {
    try {
      await db.from("account_closures").upsert(
        { user_id: row.user_id, account_id: row.account_id, closed_at: new Date().toISOString(), note: "withdrawal_sweep" },
        { onConflict: "user_id" },
      );

      // 계정이 지워지면 보관한 애플 토큰도 함께 지워져 다시는 끊을 수 없다 — 여기가 마지막 기회다.
      // 그래서 토큰을 읽지 못했으면 이번에는 지우지 않는다("토큰 없음"으로 읽고 지우면 영영 못 끊는다).
      const { data: token, error: tokenError } = await db
        .from("apple_auth_tokens").select("client_id, refresh_token").eq("account_id", row.account_id).maybeSingle();
      if (tokenError) {
        failed++;
        console.error(`[withdrawal-sweep] 애플 토큰 조회 실패 — 다음 회차로 미룸: ${tokenError.message}, account=${row.account_id}`);
        continue;
      }
      if (token) {
        if (!appleConfigured) {
          console.warn(`[withdrawal-sweep] APPLE_* 값이 없어 애플 연결을 끊지 못했습니다. account=${row.account_id}`);
        } else {
          // 철회는 이용자 요청인 삭제를 막지 않는다 — 키 형식 오류·시간 초과로 던져도 삭제는 진행한다(withdrawal-guard 와 같다).
          try {
            const r = await revokeRefreshToken(keys, token.client_id, token.refresh_token);
            if (!r.ok) console.warn(`[withdrawal-sweep] 애플 철회 실패(삭제는 진행): ${r.detail}, account=${row.account_id}`);
          } catch (e) {
            console.warn(`[withdrawal-sweep] 애플 철회 중 예외(삭제는 진행): ${e instanceof Error ? e.message : e}, account=${row.account_id}`);
          }
        }
      }

      const del = await db.auth.admin.deleteUser(row.account_id, false);
      if (del.error) {
        failed++;
        console.error(`[withdrawal-sweep] 삭제 실패: ${del.error.message}, account=${row.account_id}`);
      } else {
        deleted++;
      }
    } catch (e) {
      failed++;
      console.error(`[withdrawal-sweep] 처리 중 예외: ${e instanceof Error ? e.message : e}, account=${row.account_id}`);
    }
  }

  console.log(`[withdrawal-sweep] 대상 ${rows.length} · 삭제 ${deleted} · 실패 ${failed}`);
  return reply(failed > 0 ? 500 : 200, { ok: failed === 0, deleted, failed });
});
