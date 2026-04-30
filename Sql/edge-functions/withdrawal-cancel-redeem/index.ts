import { createClient } from "npm:@supabase/supabase-js@2";

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
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.defence_r;
const CANCEL_TOKEN_SECRET = Deno.env.get("CANCEL_TOKEN_SECRET")!;

if (!CANCEL_TOKEN_SECRET) {
  throw new Error("CANCEL_TOKEN_SECRET is required");
}

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

async function verifyCancelToken(token: string): Promise<CancelTokenPayload | null> {
  const parts = token.split(".");
  if (parts.length !== 2) return null;

  const [payloadEncoded, signatureEncoded] = parts;
  if (!payloadEncoded || !signatureEncoded) return null;

  const expectedSig = await hmacSha256Base64Url(CANCEL_TOKEN_SECRET, payloadEncoded);
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

Deno.serve(async (req) => {
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

  const payload = await verifyCancelToken(token);
  if (!payload) {
    return new Response(
      JSON.stringify({ ok: false, reason: "cancel_token_invalid_or_expired" } satisfies RedeemResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  // ✅ 변경: service_role 제거, 사용자 JWT로 새 RPC 호출
  const authHeader = req.headers.get("Authorization") ?? "";
  const supabase = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: authHeader } },
  });

  // ✅ 새 RPC: ts_withdrawal_cancel_redeem (JWT 기반, auth.uid() 사용)
  const { data, error } = await supabase.rpc("ts_withdrawal_cancel_redeem");

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
