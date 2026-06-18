import { createClient } from "npm:@supabase/supabase-js@2";
import { compactVerify, decodeProtectedHeader, importX509 } from "npm:jose";

type VerifyRequest = {
  jws_token?: string;   // StoreKit 2: jwsRepresentation (iOS 15+)
  product_id?: string;
  bundle_id?: string;
};

type VerifyResponse = {
  ok: boolean;
  already_verified?: boolean;
  transaction_id?: string;
  product_id?: string;
  purchase_state?: number;
  reason?: string;
};

// StoreKit 2 JWS 페이로드
type JWSTransactionPayload = {
  productId?: string;
  transactionId?: string;
  bundleId?: string;
  price?: number;      // 밀리유닛 (÷1000 = 실제 금액 정수)
  currency?: string;   // ISO 4217 통화 코드 (예: "KRW", "USD")
  purchaseDate?: number;
  type?: string;
};

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const publishableKeys     = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;

// ── StoreKit 2: JWS 서명 검증 ─────────────────────────────────────────────────

// 구매 금액을 KRW로 환산합니다 (frankfurter.app — 무료, API 키 불필요, ECB 일 1회 갱신).
// KRW이면 그대로 반환. 환율 API 실패 시 null 반환.
async function convertToKrw(amount: number, currency: string): Promise<number | null> {
  if (!currency || currency.toUpperCase() === "KRW") return amount;
  try {
    const res = await fetch(
      `https://api.frankfurter.app/latest?from=${encodeURIComponent(currency)}&to=KRW`,
      { signal: AbortSignal.timeout(3000) },
    );
    if (!res.ok) return null;
    const data = await res.json();
    const rate = data?.rates?.KRW;
    if (typeof rate !== "number") return null;
    return Math.round(amount * rate);
  } catch {
    return null;
  }
}

async function verifyAppleJWS(jws: string): Promise<JWSTransactionPayload> {
  // x5c 인증서 체인에서 공개키 추출
  const header = decodeProtectedHeader(jws) as { x5c?: string[] };
  if (!header.x5c?.length) throw new Error("jws_missing_x5c");

  const pem = `-----BEGIN CERTIFICATE-----\n${header.x5c[0]}\n-----END CERTIFICATE-----`;
  const publicKey = await importX509(pem, "ES256");

  const { payload } = await compactVerify(jws, publicKey);
  return JSON.parse(new TextDecoder().decode(payload)) as JWSTransactionPayload;
}

// ── 메인 핸들러 ───────────────────────────────────────────────────────────────

Deno.serve(async (req) => {
  const json = <T>(body: T, status = 200) =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });

  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ") ? authHeader.slice(7) : "";
  if (!jwt) {
    return json({ ok: false, reason: "missing_jwt" } satisfies VerifyResponse, 401);
  }

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });

  const { data: { user }, error: authError } = await userClient.auth.getUser();
  if (authError || !user) {
    return json({ ok: false, reason: "user_not_found" } satisfies VerifyResponse, 401);
  }

  let body: VerifyRequest;
  try {
    body = await req.json();
  } catch {
    return json({ ok: false, reason: "invalid_json" } satisfies VerifyResponse, 400);
  }

  const { jws_token, product_id, bundle_id } = body;

  if (!product_id) {
    return json({ ok: false, reason: "missing_product_id" } satisfies VerifyResponse, 400);
  }

  if (!jws_token) {
    return json({ ok: false, reason: "missing_jws_token" } satisfies VerifyResponse, 400);
  }

  // ── StoreKit 2 JWS 검증 ───────────────────────────────────────────────────
  let jwsData: JWSTransactionPayload;
  try {
    jwsData = await verifyAppleJWS(jws_token);
  } catch (e) {
    return json({ ok: false, reason: `jws_verify_failed: ${e}` } satisfies VerifyResponse, 502);
  }

  if (jwsData.productId !== product_id) {
    return json({ ok: false, reason: "product_id_mismatch" } satisfies VerifyResponse);
  }

  const transactionId = jwsData.transactionId ?? "";
  if (!transactionId) {
    return json({ ok: false, reason: "jws_missing_transaction_id" } satisfies VerifyResponse);
  }

  const { data: profile } = await userClient
    .from("user_profiles").select("user_id").eq("account_id", user.id).maybeSingle();
  const userId: string | null = profile?.user_id ?? null;

  // price: 밀리유닛 (÷1000 = 실제 금액 정수), currency: ISO 4217 코드
  const priceAmount    = typeof jwsData.price === "number"
    ? Math.round(jwsData.price / 1000)
    : null;
  const priceCurrency  = jwsData.currency || null;
  const priceAmountKrw = priceAmount !== null
    ? await convertToKrw(priceAmount, priceCurrency || "KRW")
    : null;

  const { error: insertError } = await userClient
    .from("purchases")
    .insert({
      account_id: user.id,
      user_id: userId,
      product_id,
      purchase_token: transactionId,
      order_id: transactionId,
      package_name: bundle_id || jwsData.bundleId || "unknown",
      purchase_state: 0,
      store: "apple_app_store",
      price_amount: priceAmount,
      price_currency: priceCurrency,
      price_amount_krw: priceAmountKrw,
    });

  if (insertError) {
    if (insertError.code === "23505") {
      return json({
        ok: true, already_verified: true,
        transaction_id: transactionId, product_id, purchase_state: 0,
      } satisfies VerifyResponse);
    }
    return json({ ok: false, reason: insertError.message } satisfies VerifyResponse, 500);
  }

  return json({
    ok: true, already_verified: false,
    transaction_id: transactionId, product_id, purchase_state: 0,
  } satisfies VerifyResponse);
});
