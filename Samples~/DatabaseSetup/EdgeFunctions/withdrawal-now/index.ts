// withdrawal-now
// 로그인한 본인의 계정을 그 자리에서 지운다(즉시 탈퇴). SDK Supabase.DeleteAccountNowAsync() 가 부른다.
//
// 절차는 withdrawal-sweep·withdrawal-guard 와 같다: account_closures 기록 → Sign in with Apple 연결 끊기 → auth 계정 삭제.
// 예약 탈퇴(유예 뒤 삭제)와 달리 되돌릴 수 없다 — 취소 토큰도 없다.
//
// 요청: 사용자 JWT 필수(verify_jwt 켬). 본문 없음. 지우는 계정은 JWT 의 주인뿐이다.
// 응답: { deleted: true } 또는 { deleted: false, reason }.
//
// 필요한 값: APPLE_TEAM_ID·APPLE_KEY_ID·APPLE_PRIVATE_KEY (운영 콘솔 "설정" 또는 Edge Function Secrets, apple-token 과 같은 값).
// 없으면 애플 연결 끊기만 건너뛰고 삭제는 한다.

import { createClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN server-config ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 스토어·애플 연동 값. 운영 콘솔 "설정"에서 넣은 값(DB Vault)을 먼저 쓰고, 없으면 Edge Function Secrets 를 쓴다.
// 1분 동안 기억해 둔다 — 콘솔에서 바꾼 값은 늦어도 1분 뒤부터 걸린다. 읽기에 실패하면 이미 기억해 둔 값을 계속 쓰고,
// 기억해 둔 값이 없을 때만(함수가 막 깨어났을 때) Secrets 로 넘어간다.
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

const SUPABASE_URL             = Deno.env.get("SUPABASE_URL")!;
const publishableKeys          = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys               = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY      = secretKeys.default;

const reply = (status: number, body: Record<string, unknown>) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

function jwtSub(jwt: string): string | null {
  const part = jwt.split(".")[1];
  if (!part) return null;
  try {
    const b64 = part.replace(/-/g, "+").replace(/_/g, "/");
    const payload = JSON.parse(atob(b64 + "=".repeat((4 - (b64.length % 4)) % 4)));
    return typeof payload.sub === "string" ? payload.sub : null;
  } catch {
    return null;
  }
}

// ═══ BEGIN request-limit ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 콘솔 "설정 › 요청 제한" — 이 함수 호출도 게임의 데이터 요청과 같은 기준으로 센다. 빈 DB 함수(ts_request_limit_ping)를
// 사용자 로그인으로 불러 DB 의 사전 검사(ts_request_limit_check)를 태운다. 제한 중이면 그쪽이 429 로 거절한다.
// 검사 자체가 실패하면(네트워크 등) 막지 않는다 — 제한 기능이 고장 나도 게임이 멈추지 않게.
async function requestLimited(authHeader: string): Promise<boolean> {
  try {
    const res = await fetch(`${Deno.env.get("SUPABASE_URL")}/rest/v1/rpc/ts_request_limit_ping`, {
      method: "POST",
      headers: {
        Authorization: authHeader,
        apikey: JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!).default,
        "Content-Type": "application/json",
      },
      body: "{}",
    });
    await res.body?.cancel();
    return res.status === 429;
  } catch {
    return false;
  }
}
// ═══ END request-limit ═══

Deno.serve(async (req) => {
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ") ? authHeader.slice("Bearer ".length) : "";
  if (!jwt) return reply(401, { deleted: false, reason: "missing_jwt" });
  if (await requestLimited(authHeader)) return reply(429, { deleted: false, reason: "request_limited" });

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });
  const { data: { user } } = await userClient.auth.getUser();
  if (!user) {
    // 앞 요청에서 이미 지웠는데 응답만 끊긴 경우 — 다시 눌러도 계속 실패하지 않게 "이미 지워짐"으로 답한다.
    // 서명은 verify_jwt 가 이미 확인했으므로 토큰의 sub 를 믿고, 그 계정이 정말 없는지 관리자 조회로 확인한다.
    const sub = jwtSub(jwt);
    if (sub) {
      const { data: found, error: lookupError } = await db.auth.admin.getUserById(sub);
      if (!lookupError && !found?.user) return reply(200, { deleted: true, already: true });
      if (lookupError && /not.?found/i.test(lookupError.message)) return reply(200, { deleted: true, already: true });
    }
    return reply(401, { deleted: false, reason: "user_not_found" });
  }

  // 계정이 지워지면 보관한 애플 토큰도 함께 지워져 다시는 끊을 수 없다. 읽지 못했으면 지우지 않고 실패로 돌려
  // 다시 시도하게 한다("토큰 없음"으로 읽고 지우면 영영 못 끊는다).
  const { data: token, error: tokenError } = await db
    .from("apple_auth_tokens").select("client_id, refresh_token").eq("account_id", user.id).maybeSingle();
  if (tokenError) {
    console.error(`[withdrawal-now] 애플 토큰 조회 실패: ${tokenError.message}, account=${user.id}`);
    return reply(500, { deleted: false, reason: "apple_token_read_failed" });
  }

  try {
    await db.from("account_closures").upsert(
      { user_id: user.id, account_id: user.id, closed_at: new Date().toISOString(), note: "withdrawal_now" },
      { onConflict: "user_id" },
    );
  } catch (e) {
    // 기록 실패로 이용자의 삭제 요청을 막지 않는다.
    console.warn(`[withdrawal-now] 탈퇴 기록 실패(삭제는 진행): ${e instanceof Error ? e.message : e}, account=${user.id}`);
  }

  if (token) {
    const keys: AppleKeys = {
      teamId: await serverConfig("APPLE_TEAM_ID"),
      keyId: await serverConfig("APPLE_KEY_ID"),
      privateKey: await serverConfig("APPLE_PRIVATE_KEY"),
    };
    if (!(keys.teamId && keys.keyId && keys.privateKey)) {
      console.warn(`[withdrawal-now] APPLE_* 값이 없어 애플 연결을 끊지 못했습니다. account=${user.id}`);
    } else {
      // 철회는 이용자 요청인 삭제를 막지 않는다 — 실패·예외여도 삭제는 진행한다.
      try {
        const r = await revokeRefreshToken(keys, token.client_id, token.refresh_token);
        if (!r.ok) console.warn(`[withdrawal-now] 애플 철회 실패(삭제는 진행): ${r.detail}, account=${user.id}`);
      } catch (e) {
        console.warn(`[withdrawal-now] 애플 철회 중 예외(삭제는 진행): ${e instanceof Error ? e.message : e}, account=${user.id}`);
      }
    }
  }

  const del = await db.auth.admin.deleteUser(user.id, false);
  // 그 사이 자정 정리가 먼저 지웠으면 결과는 같다.
  if (del.error && !/not.?found/i.test(del.error.message)) {
    console.error(`[withdrawal-now] 삭제 실패: ${del.error.message}, account=${user.id}`);
    return reply(500, { deleted: false, reason: del.error.message });
  }
  return reply(200, { deleted: true });
});
