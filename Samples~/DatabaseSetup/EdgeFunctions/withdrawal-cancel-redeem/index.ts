import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";

type RedeemRequest = {
  cancel_token?: string;
};

type RedeemResponse = {
  ok: boolean;
  reason?: string;
};

type CancelTokenPayload = {
  typ: "withdrawal_cancel";
  sub: string;
  iat: number;
  exp: number;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

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

function utf8(input: string): Uint8Array {
  return new TextEncoder().encode(input);
}

function fromBase64Url(input: string): Uint8Array {
  const normalized = input.replaceAll("-", "+").replaceAll("_", "/");
  const padded = normalized + "=".repeat((4 - (normalized.length % 4)) % 4);
  const bin = atob(padded);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) {
    bytes[i] = bin.charCodeAt(i);
  }
  return bytes;
}

function toBase64Url(bytes: Uint8Array): string {
  const b64 = btoa(String.fromCharCode(...bytes));
  return b64.replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
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

async function verifyCancelToken(token: string, secret: string): Promise<CancelTokenPayload | null> {
  const parts = token.split(".");
  if (parts.length !== 2) return null;

  const [payloadEncoded, signatureEncoded] = parts;
  if (!payloadEncoded || !signatureEncoded) return null;

  const expectedSig = await hmacSha256Base64Url(secret, payloadEncoded);
  if (expectedSig !== signatureEncoded) return null;

  try {
    const payloadJson = new TextDecoder().decode(fromBase64Url(payloadEncoded));
    const payload = JSON.parse(payloadJson) as CancelTokenPayload;
    if (payload?.typ !== "withdrawal_cancel") return null;
    if (!payload?.sub) return null;
    if (!payload?.exp || typeof payload.exp !== "number") return null;

    const nowSec = Math.floor(Date.now() / 1000);
    if (payload.exp <= nowSec) return null;

    return payload;
  } catch {
    return null;
  }
}

// ═══ BEGIN usage-count ═══ (Tools~/SdkAudit 가 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 운영 콘솔 "서버 상태"의 서버 함수 호출 수에 1 을 더한다. 응답을 기다리지 않고, 실패해도 이 함수의 동작에는 영향이 없다.
// 미리 거치는 OPTIONS 요청은 Supabase 요금도 세지 않아 빼고 센다.
function countCall(name: string, req: Request): void {
  if (req.method === "OPTIONS") return;
  try {
    const key = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS") ?? "{}").default ?? "";
    const done = fetch(`${Deno.env.get("SUPABASE_URL")}/rest/v1/rpc/ts_usage_count_call`, {
      method: "POST",
      headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
      body: JSON.stringify({ p_name: name }),
    }).then((r) => r.body?.cancel()).catch(() => {});
    // 응답을 돌려준 뒤에도 이 요청이 끝날 때까지 함수가 살아 있게 한다.
    (globalThis as { EdgeRuntime?: { waitUntil(p: Promise<unknown>): void } }).EdgeRuntime?.waitUntil(done);
  } catch {
    // 세기 실패는 무시한다.
  }
}
// ═══ END usage-count ═══

Deno.serve(async (req) => {
  countCall("withdrawal-cancel-redeem", req);
  let body: RedeemRequest | null = null;
  try {
    body = await req.json();
  } catch {
    body = null;
  }

  const token = body?.cancel_token?.trim();
  if (!token) {
    return new Response(
      JSON.stringify({ ok: false, reason: "cancel_token_empty" } satisfies RedeemResponse),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }

  // 취소는 비로그인 상태에서 이뤄지므로 사용자 JWT가 없다.
  // 토큰의 HMAC 서명이 이미 본인 인증을 대신하므로, 서명에서 얻은 sub(account_id)를
  // secret key(RLS 우회)로 RPC에 넘겨 해당 계정의 탈퇴 예약을 해제한다. 서명 비밀값도 이 클라이언트로 읽는다.
  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  let secret: string;
  try {
    secret = await cancelTokenSecret(adminClient);
  } catch (e) {
    console.error(e);
    return new Response(
      JSON.stringify({ ok: false, reason: "server_config_error" } satisfies RedeemResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  const payload = await verifyCancelToken(token, secret);
  if (!payload) {
    return new Response(
      JSON.stringify({ ok: false, reason: "cancel_token_invalid_or_expired" } satisfies RedeemResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  const { data, error } = await adminClient.rpc("ts_withdrawal_cancel_redeem", {
    p_account_id: payload.sub,
  });

  if (error) {
    return new Response(
      JSON.stringify({ ok: false, reason: error.message } satisfies RedeemResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  if (!data?.ok) {
    return new Response(
      JSON.stringify({ ok: false, reason: data?.reason || "redeem_failed" } satisfies RedeemResponse),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }

  return new Response(
    JSON.stringify({ ok: true } satisfies RedeemResponse),
    { headers: { "Content-Type": "application/json" } },
  );
});
