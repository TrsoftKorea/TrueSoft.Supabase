import { createClient } from "npm:@supabase/supabase-js@2";
import { compactVerify, decodeProtectedHeader, importX509 } from "npm:jose";

type VerifyRequest = {
  receipt_data?: string;  // StoreKit 1: base64 앱 영수증
  jws_token?: string;     // StoreKit 2: jwsRepresentation (iOS 15+)
  product_id?: string;
  bundle_id?: string;
  session_id?: string;    // 애널리틱스 세션 ID (선택)
};

type VerifyResponse = {
  ok: boolean;
  already_verified?: boolean;
  transaction_id?: string;
  product_id?: string;
  purchase_state?: number;
  reason?: string;
};

// Apple verifyReceipt 응답 내 in_app 항목 (StoreKit 1)
type AppleInAppItem = {
  product_id: string;
  transaction_id: string;
  original_transaction_id: string;
  purchase_date_ms: string;
  quantity: string;
};

type AppleVerifyReceiptResponse = {
  status: number;
  receipt?: {
    bundle_id?: string;
    in_app?: AppleInAppItem[];
  };
  latest_receipt_info?: AppleInAppItem[];
};

// StoreKit 2 JWS 페이로드
type JWSTransactionPayload = {
  productId?: string;
  transactionId?: string;
  bundleId?: string;
  price?: number;      // 밀리유닛 (÷1000 = 실제 원화)
  purchaseDate?: number;
  type?: string;
};

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const publishableKeys     = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const APPLE_SHARED_SECRET = Deno.env.get("APPLE_SHARED_SECRET")!;

const APPLE_VERIFY_URL_PROD    = "https://buy.itunes.apple.com/verifyReceipt";
const APPLE_VERIFY_URL_SANDBOX = "https://sandbox.itunes.apple.com/verifyReceipt";

// ── StoreKit 1: verifyReceipt ─────────────────────────────────────────────────

async function verifyAppleReceipt(
  receiptData: string,
): Promise<AppleVerifyReceiptResponse> {
  const body = JSON.stringify({
    "receipt-data": receiptData,
    "password": APPLE_SHARED_SECRET,
    "exclude-old-transactions": true,
  });

  const prodRes = await fetch(APPLE_VERIFY_URL_PROD, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body,
  });
  const prodJson: AppleVerifyReceiptResponse = await prodRes.json();

  if (prodJson.status === 21007) {
    const sandboxRes = await fetch(APPLE_VERIFY_URL_SANDBOX, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
    });
    return await sandboxRes.json();
  }

  return prodJson;
}

// ── StoreKit 2: JWS 서명 검증 ─────────────────────────────────────────────────

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

  const { receipt_data, jws_token, product_id, bundle_id, session_id } = body;
  if (!product_id) {
    return json({ ok: false, reason: "missing_product_id" } satisfies VerifyResponse, 400);
  }

  // ── StoreKit 2 경로 ────────────────────────────────────────────────────────
  if (jws_token) {
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
      .from("user_profiles").select("user_id").maybeSingle();
    const userId: string | null = profile?.user_id ?? null;

    // price: 밀리유닛 (÷1000 = 실제 원화 정수)
    const priceAmount = typeof jwsData.price === "number"
      ? Math.round(jwsData.price / 1000)
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
        session_id: session_id ?? null,
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
  }

  // ── StoreKit 1 폴백 경로 ───────────────────────────────────────────────────
  if (!receipt_data) {
    return json({ ok: false, reason: "missing_receipt_or_jws" } satisfies VerifyResponse, 400);
  }

  let appleResponse: AppleVerifyReceiptResponse;
  try {
    appleResponse = await verifyAppleReceipt(receipt_data);
  } catch (e) {
    return json({ ok: false, reason: `apple_api_exception: ${e}` } satisfies VerifyResponse, 502);
  }

  if (appleResponse.status !== 0) {
    return json({
      ok: false,
      reason: `apple_status_${appleResponse.status}`,
      purchase_state: -1,
    } satisfies VerifyResponse);
  }

  const inAppItems: AppleInAppItem[] = [
    ...(appleResponse.receipt?.in_app ?? []),
    ...(appleResponse.latest_receipt_info ?? []),
  ];

  const matched = inAppItems
    .filter((item) => item.product_id === product_id)
    .sort((a, b) => Number(b.purchase_date_ms) - Number(a.purchase_date_ms))[0];

  if (!matched) {
    return json({
      ok: false, reason: "product_not_found_in_receipt", purchase_state: -1,
    } satisfies VerifyResponse);
  }

  const transactionId = matched.transaction_id;

  let userId: string | null = null;
  const { data: profile, error: profileError } = await userClient
    .from("user_profiles").select("user_id").maybeSingle();
  if (!profileError) userId = profile?.user_id ?? null;

  const { error: insertError } = await userClient
    .from("purchases")
    .insert({
      account_id: user.id,
      user_id: userId,
      product_id,
      purchase_token: transactionId,
      order_id: transactionId,
      package_name: bundle_id || "unknown",
      purchase_state: 0,
      store: "apple_app_store",
      session_id: session_id ?? null,
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
