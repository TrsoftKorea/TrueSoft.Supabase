// apple-token
// Sign in with Apple 토큰 보관·철회.
//
// 애플은 계정을 지울 때 Sign in with Apple 토큰을 REST API 로 철회하라고 요구한다(앱 심사 기준).
// 철회에는 refresh token 이 필요한데, 로그인에 쓰는 ID 토큰에는 들어 있지 않다. 그래서 로그인 때
// 받은 일회용 authorization code(5분 유효)를 여기서 refresh token 으로 바꿔 apple_auth_tokens 에
// 보관해 두고, 탈퇴할 때 그 토큰으로 철회한다.
//
// 필요한 값(운영 콘솔 "설정" 또는 Edge Function Secrets):
//   APPLE_BUNDLE_ID    — 앱 번들 ID(purchase-verify-apple 과 같은 값). 여럿이면 쉼표로 구분
//   APPLE_TEAM_ID      — Apple Developer 팀 ID(10자)
//   APPLE_KEY_ID       — Sign in with Apple 용 키의 Key ID(10자)
//   APPLE_PRIVATE_KEY  — 그 키의 .p8 파일 내용 전체(-----BEGIN PRIVATE KEY----- 포함)
//
// 요청 Body (JSON, 사용자 JWT 필수):
//   { "action": "store", "authorization_code": "...", "client_id": "번들 ID(생략 시 APPLE_BUNDLE_ID 첫 값)" }
//   { "action": "revoke" }
//
// 호출하는 곳: SDK(애플 로그인·연동 성공 직후 store, 탈퇴 신청 성공 직후 revoke),
//             withdrawal-guard(계정 최종 삭제 직전 revoke — 신청 때 놓친 경우를 막는다).

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

type TokenRequest = {
  action?: string;
  authorization_code?: string;
  client_id?: string;
};

type TokenResponse = {
  ok: boolean;
  stored?: boolean;
  revoked?: boolean;
  reason?: string;
};

const SUPABASE_URL             = Deno.env.get("SUPABASE_URL")!;
const publishableKeys          = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys               = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY      = secretKeys.default;

const APPLE_TOKEN_URL = "https://appleid.apple.com/auth/token";

const json = (body: TokenResponse, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

function decodeJwtPayload(jwt: string): Record<string, unknown> | null {
  const part = jwt.split(".")[1];
  if (!part) return null;
  try {
    const b64 = part.replace(/-/g, "+").replace(/_/g, "/");
    return JSON.parse(atob(b64 + "=".repeat((4 - (b64.length % 4)) % 4)));
  } catch {
    return null;
  }
}

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

// ═══ BEGIN request-limit ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 콘솔 "설정 › 요청 제한" — 이 함수 호출도 게임의 데이터 요청과 같은 기준으로 센다. 빈 DB 함수(ts_request_limit_ping)를
// 사용자 로그인으로 불러 DB 의 사전 검사(ts_request_limit_check)를 태운다. 제한 중이면 그쪽이 429 로 거절한다.
// 검사 자체가 실패하거나 2초 안에 답이 없으면 막지 않는다 — 제한 기능이 고장 나도 게임이 멈추지 않게.
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
      signal: AbortSignal.timeout(2000),
    });
    await res.body?.cancel();
    return res.status === 429;
  } catch {
    return false;
  }
}
// ═══ END request-limit ═══

Deno.serve(async (req) => {
  try {
    return await handle(req);
  } catch (e) {
    // 키 형식 오류(atob·importKey)·애플 시간 초과 등. 사유 없는 500 대신 원인을 남긴다.
    console.error(`[apple-token] 처리 중 예외: ${e}`);
    return json({ ok: false, reason: `apple_token_exception: ${e instanceof Error ? e.message : String(e)}` }, 500);
  }
});

async function handle(req: Request): Promise<Response> {
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ") ? authHeader.slice(7) : "";
  if (!jwt) return json({ ok: false, reason: "missing_jwt" }, 401);
  if (await requestLimited(authHeader)) return json({ ok: false, reason: "request_limited" }, 429);

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });
  const { data: { user }, error: authError } = await userClient.auth.getUser();
  if (authError || !user) return json({ ok: false, reason: "user_not_found" }, 401);

  let body: TokenRequest;
  try {
    body = await req.json();
  } catch {
    return json({ ok: false, reason: "invalid_json" }, 400);
  }

  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const ALLOWED_CLIENT_IDS = await serverConfigList("APPLE_BUNDLE_ID");
  const keys: AppleKeys = {
    teamId: await serverConfig("APPLE_TEAM_ID"),
    keyId: await serverConfig("APPLE_KEY_ID"),
    privateKey: await serverConfig("APPLE_PRIVATE_KEY"),
  };
  const configured = ALLOWED_CLIENT_IDS.length > 0 && keys.teamId && keys.keyId && keys.privateKey;

  // ── 철회 ─────────────────────────────────────────────────────────────────
  if (body.action === "revoke") {
    const { data: row, error: readError } = await adminClient
      .from("apple_auth_tokens").select("client_id, refresh_token").eq("account_id", user.id).maybeSingle();
    if (readError) return json({ ok: false, reason: readError.message }, 500);
    if (!row) return json({ ok: true, revoked: false, reason: "no_token" });

    if (!configured) {
      console.error("[apple-token] APPLE_* 값이 없어 철회하지 못했습니다.");
      return json({ ok: false, reason: "server_config_error" }, 500);
    }

    const r = await revokeRefreshToken(keys, row.client_id, row.refresh_token);
    if (!r.ok) {
      console.warn(`[apple-token] 철회 실패: ${r.detail}, account=${user.id}`);
      return json({ ok: false, reason: `apple_revoke_failed: ${r.detail}` }, 502);
    }

    await adminClient.from("apple_auth_tokens").delete().eq("account_id", user.id);
    return json({ ok: true, revoked: true });
  }

  // ── 보관 ─────────────────────────────────────────────────────────────────
  if (body.action === "store") {
    const code = body.authorization_code?.trim();
    if (!code) return json({ ok: false, reason: "missing_authorization_code" }, 400);

    if (!configured) {
      console.error("[apple-token] APPLE_* 값이 없어 토큰을 보관하지 못했습니다.");
      return json({ ok: false, reason: "server_config_error" }, 500);
    }

    const clientId = body.client_id?.trim() || ALLOWED_CLIENT_IDS[0];
    if (!ALLOWED_CLIENT_IDS.includes(clientId)) return json({ ok: false, reason: "client_id_not_allowed" }, 400);

    const res = await postForm(APPLE_TOKEN_URL, {
      client_id: clientId,
      client_secret: await createClientSecret(keys, clientId),
      code,
      grant_type: "authorization_code",
    });
    if (!res.ok) {
      const detail = `${res.status} ${await res.text()}`;
      console.warn(`[apple-token] 코드 교환 실패: ${detail}, account=${user.id}`);
      return json({ ok: false, reason: `apple_token_failed: ${detail}` }, 502);
    }

    const tokens = await res.json() as { refresh_token?: string; id_token?: string };
    if (!tokens.refresh_token) return json({ ok: false, reason: "apple_no_refresh_token" }, 502);

    // 이 코드가 호출한 계정에 연동된 애플 계정의 것인지 확인한다. 남의 코드를 보관하면 탈퇴 때
    // 엉뚱한 사람의 애플 연결을 끊게 된다 — 그 경우 받은 토큰은 바로 철회하고 버린다.
    const appleSub = decodeJwtPayload(tokens.id_token ?? "")?.sub;
    const linkedSubs = (user.identities ?? [])
      .filter((i) => i.provider === "apple")
      .flatMap((i) => [i.identity_data?.sub, i.id])
      .filter((s): s is string => typeof s === "string" && s.length > 0);
    if (typeof appleSub !== "string" || !linkedSubs.includes(appleSub)) {
      await revokeRefreshToken(keys, clientId, tokens.refresh_token);
      console.warn(`[apple-token] 연동된 애플 계정과 다른 코드: account=${user.id}`);
      return json({ ok: false, reason: "apple_identity_mismatch" }, 403);
    }

    const { error: upsertError } = await adminClient.from("apple_auth_tokens").upsert({
      account_id: user.id,
      client_id: clientId,
      refresh_token: tokens.refresh_token,
      updated_at: new Date().toISOString(),
    }, { onConflict: "account_id" });
    if (upsertError) return json({ ok: false, reason: upsertError.message }, 500);

    return json({ ok: true, stored: true });
  }

  return json({ ok: false, reason: "unknown_action" }, 400);
}
