import { createClient } from "npm:@supabase/supabase-js@2";

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
const CANCEL_TOKEN_SECRET = Deno.env.get("CANCEL_TOKEN_SECRET")!;
const CANCEL_TOKEN_TTL_SECONDS = Number(Deno.env.get("CANCEL_TOKEN_TTL_SECONDS") ?? "900");

if (!CANCEL_TOKEN_SECRET) {
  throw new Error("CANCEL_TOKEN_SECRET is required");
}

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

async function signCancelToken(payload: CancelTokenPayload): Promise<string> {
  const payloadEncoded = toBase64Url(utf8(JSON.stringify(payload)));
  const signature = await hmacSha256Base64Url(CANCEL_TOKEN_SECRET, payloadEncoded);
  return `${payloadEncoded}.${signature}`;
}

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

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });
  // ❌ 제거: adminClient 불필요 (service_role 제거)

  const userRes = await userClient.auth.getUser();
  const user = userRes.data.user;
  if (!user) {
    return new Response(
      JSON.stringify({ ok: false, reason: "user_not_found" } satisfies IssueResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  // user_profiles는 SELECT가 공개(프로필 표시용)이므로 account_id로 본인 행을 명시 필터
  const profileRes = await userClient
    .from("user_profiles")
    .select("withdrawn_at")
    .eq("account_id", user.id)
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

  const token = await signCancelToken(payload);
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
