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
  already_granted?: boolean;
  transaction_id?: string;
  product_id?: string;
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
const secretKeys          = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

// ── StoreKit 2: JWS 서명 검증 ─────────────────────────────────────────────────

// price_amount(micros = 주 단위 ×1,000,000)를 KRW 정수(원)로 환산합니다 (frankfurter.app — 무료, ECB 일 1회 갱신).
// KRW이면 환율 없이, 그 외는 환율 API. 실패 시 null 반환.
async function convertToKrw(micros: number, currency: string): Promise<number | null> {
  const major = micros / 1_000_000;   // micros → 주 단위
  if (!currency || currency.toUpperCase() === "KRW") return Math.round(major);
  try {
    const res = await fetch(
      `https://api.frankfurter.app/latest?from=${encodeURIComponent(currency)}&to=KRW`,
      { signal: AbortSignal.timeout(3000) },
    );
    if (!res.ok) return null;
    const data = await res.json();
    const rate = data?.rates?.KRW;
    if (typeof rate !== "number") return null;
    return Math.round(major * rate);
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

  // JWS price: 밀리유닛(÷1000=주 단위). micros(주 단위 ×1,000,000 = millis ×1000)로 통일해 정밀 유지.
  const priceAmount    = typeof jwsData.price === "number"
    ? jwsData.price * 1000
    : null;
  const priceCurrency  = jwsData.currency || null;
  const priceAmountKrw = priceAmount !== null
    ? await convertToKrw(priceAmount, priceCurrency || "KRW")
    : null;

  // 구매 기록은 영수증 검증을 통과한 이 함수만 쓸 수 있어야 하므로 service_role로 기록한다.
  // (유저 직접 INSERT를 막아 total_paid_krw 조작·가짜 결제 기록을 차단. account_id는 JWT로 검증된 user.id.)
  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
  const { error: insertError } = await adminClient
    .from("purchases")
    .insert({
      account_id: user.id,
      user_id: userId,
      product_id,
      purchase_token: transactionId,
      order_id: transactionId,
      package_name: bundle_id || jwsData.bundleId || "unknown",
      store: "apple_app_store",
      price_amount: priceAmount,
      price_currency: priceCurrency,
      price_amount_krw: priceAmountKrw,
    });

  if (insertError) {
    // UNIQUE 위반 → 이미 검증된 영수증. 단, 기록의 주인이 이 계정일 때만 재처리로 인정한다.
    // (다른 계정의 토큰을 보낸 경우까지 ok=true 로 답하면, 크래시 복구 지침대로 구현한 게임이 남의 결제로 지급한다)
    if (insertError.code === "23505") {
      const { data: owner } = await adminClient
        .from("purchases")
        .select("account_id, granted_at")
        .eq("purchase_token", transactionId)
        .maybeSingle();
      if (owner && owner.account_id !== user.id) {
        return json({ ok: false, reason: "purchase_owned_by_other_account" } satisfies VerifyResponse, 409);
      }
      return json({
        ok: true, already_verified: true,
        already_granted: !!owner?.granted_at,
        transaction_id: transactionId, product_id,
      } satisfies VerifyResponse);
    }
    return json({ ok: false, reason: insertError.message } satisfies VerifyResponse, 500);
  }

  return json({
    ok: true, already_verified: false,
    already_granted: false,
    transaction_id: transactionId, product_id,
  } satisfies VerifyResponse);
});
