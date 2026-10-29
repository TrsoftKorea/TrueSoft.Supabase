import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";

type IssueResponse = {
  ok: boolean;
  cancel_token?: string;
  expires_at?: string;
  reason?: string;
};

type CancelTokenPayload = {
  typ: "withdrawal_cancel";
  sub: string;
  iat: number;
  exp: number;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;
const CANCEL_TOKEN_TTL_SECONDS = Number(Deno.env.get("CANCEL_TOKEN_TTL_SECONDS") ?? "900");

// ═══ BEGIN cancel-secret ═══
// 탈퇴 취소 토큰 서명 비밀값. Secrets 의 CANCEL_TOKEN_SECRET 을 먼저 쓰고(이미 넣어 둔 게임은 그 값 그대로 —
// 바꾸면 발급된 토큰이 무효가 된다), 없으면 설치 때 DB 가 만들어 둔 값(ts_withdrawal_cancel_secret)을 쓴다.
let cancelSecretCache: string | null = null;
async function cancelTokenSecret(db: SupabaseClient): Promise<string> {
  if (cancelSecretCache) return cancelSecretCache;
  const fromEnv = (Deno.env.get("CANCEL_TOKEN_SECRET") ?? "").trim();
  if (fromEnv) return (cancelSecretCache = fromEnv);
  const { data, error } = await db.rpc("ts_withdrawal_cancel_secret");
  if (error) throw new Error(`cancel secret: ${error.message}`);
  if (typeof data !== "string" || !data) throw new Error("cancel secret missing");
  return (cancelSecretCache = data);
}
// ═══ END cancel-secret ═══

function toBase64Url(bytes: Uint8Array): string {
  const b64 = btoa(String.fromCharCode(...bytes));
  return b64.replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function utf8(input: string): Uint8Array {
  return new TextEncoder().encode(input);
}

async function hmacSha256Base64Url(secret: string, message: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    utf8(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const sig = await crypto.subtle.sign("HMAC", key, utf8(message));
  return toBase64Url(new Uint8Array(sig));
}

async function signCancelToken(payload: CancelTokenPayload, secret: string): Promise<string> {
  const payloadEncoded = toBase64Url(utf8(JSON.stringify(payload)));
  const signature = await hmacSha256Base64Url(secret, payloadEncoded);
  return `${payloadEncoded}.${signature}`;
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
  const jwt = authHeader.startsWith("Bearer ")
    ? authHeader.slice("Bearer ".length)
    : "";

  if (!jwt) {
    return new Response(
      JSON.stringify({ ok: false, reason: "missing_jwt" } satisfies IssueResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }
  if (await requestLimited(authHeader)) {
    return new Response(
      JSON.stringify({ ok: false, reason: "request_limited" } satisfies IssueResponse),
      { status: 429, headers: { "Content-Type": "application/json" } },
    );
  }

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });

  const userRes = await userClient.auth.getUser();
  const user = userRes.data.user;
  if (!user) {
    return new Response(
      JSON.stringify({ ok: false, reason: "user_not_found" } satisfies IssueResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  // user_profiles는 SELECT가 공개(프로필 표시용)이므로 account_id로 본인 행을 명시 필터
  // account_id 하나에 프로필 행이 여러 개(서버별 등)일 수 있으므로 예약된 행으로 좁혀 단일 행을 보장한다.
  // (필터 없이 maybeSingle 하면 여러 행에서 PGRST116 "multiple rows returned" 500)
  const profileRes = await userClient
    .from("user_profiles")
    .select("withdrawn_at")
    .eq("account_id", user.id)
    .not("withdrawn_at", "is", null)
    .order("withdrawn_at", { ascending: false })
    .limit(1)
    .maybeSingle();

  if (profileRes.error) {
    return new Response(
      JSON.stringify({ ok: false, reason: profileRes.error.message } satisfies IssueResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  const withdrawnAt = profileRes.data?.withdrawn_at
    ? new Date(profileRes.data.withdrawn_at as string)
    : null;
  if (!withdrawnAt || withdrawnAt.getTime() <= Date.now()) {
    return new Response(
      JSON.stringify({ ok: false, reason: "withdrawal_not_scheduled" } satisfies IssueResponse),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }

  const ttl = Number.isFinite(CANCEL_TOKEN_TTL_SECONDS) && CANCEL_TOKEN_TTL_SECONDS > 0
    ? Math.floor(CANCEL_TOKEN_TTL_SECONDS)
    : 900;
  const nowSec = Math.floor(Date.now() / 1000);
  const payload: CancelTokenPayload = {
    typ: "withdrawal_cancel",
    sub: user.id,
    iat: nowSec,
    exp: nowSec + ttl,
  };

  // 비밀값은 DB 에서 읽을 수 있어 secret key 클라이언트가 필요하다(그 함수는 service_role 전용).
  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
  let secret: string;
  try {
    secret = await cancelTokenSecret(adminClient);
  } catch (e) {
    console.error(e);
    return new Response(
      JSON.stringify({ ok: false, reason: "server_config_error" } satisfies IssueResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  const token = await signCancelToken(payload, secret);
  const expiresAt = new Date(payload.exp * 1000).toISOString();

  return new Response(
    JSON.stringify({
      ok: true,
      cancel_token: token,
      expires_at: expiresAt,
    } satisfies IssueResponse),
    { headers: { "Content-Type": "application/json" } },
  );
});
