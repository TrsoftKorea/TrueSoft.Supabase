// purchase-verify-apple-legacy
// SK1 (verifyReceipt) 기반 Apple IAP 서버 검증.
// Unity IAP v4 또는 iOS 14 이하(forceStoreKit1)에서 생성된 receipt blob을 검증합니다.
//
// 필요 환경 변수:
//   APPLE_SHARED_SECRET  — App Store Connect > 앱 정보 > 공유 암호 (shared secret)
//
// 요청 Body (JSON):
//   receipt    string  — SK1 base64 encoded receipt blob (Unity IAP 영수증의 Payload 필드)
//   product_id string  — 기대하는 상품 ID
//   bundle_id  string  — 앱 Bundle ID (검증에 사용)
//
// purchases 테이블 요구사항:
//   - transaction_id 컬럼에 UNIQUE 제약 조건 필요
//   - RLS: authenticated users는 자신의 account_id로 INSERT 가능

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const PRODUCTION_URL = "https://buy.itunes.apple.com/verifyReceipt";
const SANDBOX_URL    = "https://sandbox.itunes.apple.com/verifyReceipt";

interface RequestBody {
  receipt:    string;
  product_id: string;
  bundle_id:  string;
}

interface VerifyReceiptResponse {
  status:               number;
  bundle_id?:           string;
  latest_receipt_info?: ReceiptInfo[];
}

interface ReceiptInfo {
  product_id:              string;
  transaction_id:          string;
  original_transaction_id: string;
  purchase_date_ms:        string;
}

serve(async (req: Request) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", {
      headers: {
        "Access-Control-Allow-Origin":  "*",
        "Access-Control-Allow-Headers": "authorization, content-type",
      },
    });
  }

  // ── 인증 ───────────────────────────────────────────────────────────────────
  const authHeader = req.headers.get("Authorization");
  if (!authHeader) {
    return jsonResponse({ ok: false, reason: "missing_auth" }, 401);
  }

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_ANON_KEY")!,
    { global: { headers: { Authorization: authHeader } } }
  );

  const { data: { user }, error: userError } = await supabase.auth.getUser();
  if (userError || !user) {
    return jsonResponse({ ok: false, reason: "unauthorized" }, 401);
  }

  // ── 요청 파싱 ─────────────────────────────────────────────────────────────
  let body: RequestBody;
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ ok: false, reason: "invalid_json" }, 400);
  }

  const { receipt, product_id, bundle_id } = body;
  if (!receipt || !product_id || !bundle_id) {
    return jsonResponse({ ok: false, reason: "missing_fields" }, 400);
  }

  const sharedSecret = Deno.env.get("APPLE_SHARED_SECRET");
  if (!sharedSecret) {
    console.error("[purchase-verify-apple-legacy] APPLE_SHARED_SECRET 환경 변수가 없습니다.");
    return jsonResponse({ ok: false, reason: "server_config_error" }, 500);
  }

  // ── Apple verifyReceipt 호출 ───────────────────────────────────────────────
  const applePayload = { "receipt-data": receipt, password: sharedSecret };

  let verifyData: VerifyReceiptResponse;
  try {
    verifyData = await callAppleVerifyReceipt(PRODUCTION_URL, applePayload);

    // 21007 = sandbox receipt가 production 서버로 전송됨 → sandbox로 재시도
    if (verifyData.status === 21007) {
      verifyData = await callAppleVerifyReceipt(SANDBOX_URL, applePayload);
    }
  } catch (e) {
    console.error("[purchase-verify-apple-legacy] Apple API 호출 실패:", e);
    return jsonResponse({ ok: false, reason: "apple_api_error" }, 500);
  }

  if (verifyData.status !== 0) {
    console.warn(`[purchase-verify-apple-legacy] Apple 검증 실패: status=${verifyData.status}`);
    return jsonResponse({
      ok:               false,
      already_verified: false,
      reason:           `apple_status_${verifyData.status}`,
    });
  }

  // bundle_id 검증
  if (verifyData.bundle_id && verifyData.bundle_id !== bundle_id) {
    return jsonResponse({ ok: false, reason: "bundle_id_mismatch" });
  }

  // product_id 일치하는 최신 트랜잭션 추출
  const receipts = verifyData.latest_receipt_info ?? [];
  const match = receipts
    .filter(r => r.product_id === product_id)
    .sort((a, b) => Number(b.purchase_date_ms) - Number(a.purchase_date_ms))[0];

  if (!match) {
    return jsonResponse({ ok: false, reason: "product_not_found_in_receipt" });
  }

  const transactionId = match.transaction_id;

  // ── 중복 검증 체크 + 기록 삽입 ────────────────────────────────────────────
  // 구매 기록은 영수증 검증을 통과한 이 함수만 쓸 수 있어야 하므로 service_role로 기록한다.
  // (유저 직접 INSERT를 막아 가짜 결제 기록을 차단. account_id는 JWT로 검증된 user.id.)
  // purchases 테이블에 transaction_id UNIQUE 제약 조건이 있어야 합니다.
  // INSERT 성공 → 새 검증 / 23505(unique_violation) → 이미 검증됨
  const adminClient = createClient(
    Deno.env.get("SUPABASE_URL")!,
    JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default,
    { auth: { autoRefreshToken: false, persistSession: false } }
  );
  const { error: insertError } = await adminClient
    .from("purchases")
    .insert({
      account_id:     user.id,
      transaction_id: transactionId,
      product_id,
      store:          "apple_app_store_legacy",
    });

  let alreadyVerified = false;
  if (insertError) {
    if (insertError.code === "23505") {
      // unique_violation — 이미 검증된 영수증
      alreadyVerified = true;
    } else {
      console.error("[purchase-verify-apple-legacy] DB 삽입 오류:", insertError.message);
      return jsonResponse({ ok: false, reason: "db_error" }, 500);
    }
  }

  return jsonResponse({
    ok:               true,
    already_verified: alreadyVerified,
    transaction_id:   transactionId,
    product_id,
    reason:           null,
  });
});

// ── 헬퍼 ─────────────────────────────────────────────────────────────────────

async function callAppleVerifyReceipt(
  url: string,
  payload: Record<string, string>
): Promise<VerifyReceiptResponse> {
  const res = await fetch(url, {
    method:  "POST",
    headers: { "Content-Type": "application/json" },
    body:    JSON.stringify(payload),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

function jsonResponse(data: Record<string, unknown>, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type":                 "application/json",
      "Access-Control-Allow-Origin":  "*",
      "Access-Control-Allow-Headers": "authorization, content-type",
    },
  });
}
