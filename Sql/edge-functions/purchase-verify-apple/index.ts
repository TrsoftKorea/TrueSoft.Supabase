import { createClient } from "npm:@supabase/supabase-js@2";

type VerifyRequest = {
  receipt_data?: string;   // Unity IAP receipt의 Payload (base64 앱 영수증)
  product_id?: string;
  bundle_id?: string;      // Application.identifier (클라이언트가 자동 전달)
};

type VerifyResponse = {
  ok: boolean;
  already_verified?: boolean;
  transaction_id?: string;
  product_id?: string;
  purchase_state?: number;
  reason?: string;
};

// Apple verifyReceipt 응답 내 in_app 항목
type AppleInAppItem = {
  product_id: string;
  transaction_id: string;
  original_transaction_id: string;
  purchase_date_ms: string;
  quantity: string;
};

type AppleVerifyReceiptResponse = {
  status: number;           // 0=valid, 21007=sandbox receipt
  receipt?: {
    bundle_id?: string;
    in_app?: AppleInAppItem[];
  };
  latest_receipt_info?: AppleInAppItem[];
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.defence_r;
const APPLE_SHARED_SECRET = Deno.env.get("APPLE_SHARED_SECRET")!;

if (!APPLE_SHARED_SECRET) {
  throw new Error("APPLE_SHARED_SECRET is required");
}

const APPLE_VERIFY_URL_PROD    = "https://buy.itunes.apple.com/verifyReceipt";
const APPLE_VERIFY_URL_SANDBOX = "https://sandbox.itunes.apple.com/verifyReceipt";

// Apple verifyReceipt 호출. 프로덕션 먼저 시도, status 21007이면 샌드박스로 재시도.
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

  // 21007: 샌드박스 영수증이 프로덕션에 제출됨 → 샌드박스로 재시도
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

Deno.serve(async (req) => {
  const json = <T>(body: T, status = 200) =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });

  // JWT 검증
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

  // 요청 파싱
  let body: VerifyRequest;
  try {
    body = await req.json();
  } catch {
    return json({ ok: false, reason: "invalid_json" } satisfies VerifyResponse, 400);
  }

  const { receipt_data, product_id, bundle_id } = body;

  if (!receipt_data || !product_id) {
    return json({ ok: false, reason: "missing_fields" } satisfies VerifyResponse, 400);
  }

  // Apple verifyReceipt 호출
  let appleResponse: AppleVerifyReceiptResponse;
  try {
    appleResponse = await verifyAppleReceipt(receipt_data);
  } catch (e) {
    return json({ ok: false, reason: `apple_api_exception: ${e}` } satisfies VerifyResponse, 502);
  }

  // status 0 이외는 유효하지 않은 영수증
  if (appleResponse.status !== 0) {
    return json({
      ok: false,
      reason: `apple_status_${appleResponse.status}`,
      purchase_state: -1,
    } satisfies VerifyResponse);
  }

  // in_app 배열에서 product_id가 일치하는 최신 트랜잭션 탐색
  const inAppItems: AppleInAppItem[] = [
    ...(appleResponse.receipt?.in_app ?? []),
    ...(appleResponse.latest_receipt_info ?? []),
  ];

  const matched = inAppItems
    .filter((item) => item.product_id === product_id)
    .sort((a, b) => Number(b.purchase_date_ms) - Number(a.purchase_date_ms))[0];

  if (!matched) {
    return json({
      ok: false,
      reason: "product_not_found_in_receipt",
      purchase_state: -1,
    } satisfies VerifyResponse);
  }

  const transactionId = matched.transaction_id;

  // user_id 조회
  let userId: string | null = null;
  const { data: profile, error: profileError } = await userClient
    .from("user_profiles")
    .select("user_id")
    .maybeSingle();
  if (!profileError) {
    userId = profile?.user_id ?? null;
  } else {
    console.error(`[purchase-verify-apple] profile_query_error: ${profileError.message}`);
  }

  // 구매 기록 INSERT (purchase_token = transaction_id, UNIQUE 충돌 시 already_verified)
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
    });

  if (insertError) {
    if (insertError.code === "23505") {
      return json({
        ok: true,
        already_verified: true,
        transaction_id: transactionId,
        product_id,
        purchase_state: 0,
      } satisfies VerifyResponse);
    }
    return json({ ok: false, reason: insertError.message } satisfies VerifyResponse, 500);
  }

  return json({
    ok: true,
    already_verified: false,
    transaction_id: transactionId,
    product_id,
    purchase_state: 0,
  } satisfies VerifyResponse);
});
