// purchase-verify-apple-legacy
// SK1 (verifyReceipt) 기반 Apple IAP 서버 검증.
// iOS 14 이하 또는 forceStoreKit1 경로에서 생성된 receipt blob을 검증합니다.
//
// 필요한 값(운영 콘솔 "설정" 또는 Edge Function Secrets):
//   APPLE_SHARED_SECRET  — App Store Connect > 앱 정보 > 공유 암호 (shared secret)
//   APPLE_BUNDLE_ID      — 이 프로젝트가 받는 앱 번들 ID. 여럿이면 쉼표로 구분.
//                          verifyReceipt 는 어느 앱의 영수증이든 진짜면 status 0 을 준다(소모성 상품은
//                          공유 암호도 안 본다). 서버가 번들 ID를 대조하지 않으면 다른 앱 영수증이 통과한다.
//
// 요청 Body (JSON):
//   receipt    string  — SK1 base64 encoded receipt blob (Unity IAP 영수증의 Payload 필드)
//   product_id string  — 기대하는 상품 ID
//   bundle_id  string  — 앱 Bundle ID. 앱이 보내는 값이라 대조에는 쓰지 않는다(호환용으로만 받는다)
//
// 기록은 purchases.purchase_token UNIQUE 로 중복을 막습니다. 유저 직접 INSERT 정책은 없고
// 이 함수가 service_role 로만 기록합니다(가짜 결제 기록 차단).

import { createClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN server-config ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 스토어·애플 연동 값. 운영 콘솔 "설정"에서 넣은 값(DB Vault)을 먼저 쓰고, 없으면 Edge Function Secrets 를 쓴다.
// 1분 동안 기억해 둔다 — 콘솔에서 바꾼 값은 늦어도 1분 뒤부터 걸린다. 읽기에 실패하면 기억하지 않고 Secrets 로 넘어간다.
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

const PRODUCTION_URL = "https://buy.itunes.apple.com/verifyReceipt";
const SANDBOX_URL    = "https://sandbox.itunes.apple.com/verifyReceipt";

const SUPABASE_URL             = Deno.env.get("SUPABASE_URL")!;
const publishableKeys          = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys               = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY      = secretKeys.default;

interface RequestBody {
  receipt:    string;
  product_id: string;
  bundle_id?: string;
}

// 번들 ID와 거래 목록은 최상위가 아니라 receipt 안에 있다(애플 문서 responseBody.Receipt).
// 거래는 latest_receipt_info 와 receipt.in_app 양쪽을 본다 — 어느 쪽에 오는지가 상품 종류·상태에 따라
// 달라, 한쪽만 보면 소모성 상품을 못 찾을 수 있다.
interface VerifyReceiptResponse {
  status:               number;
  environment?:         string;
  receipt?: {
    bundle_id?: string;
    in_app?:    ReceiptInfo[];
  };
  latest_receipt_info?: ReceiptInfo[];
}

interface ReceiptInfo {
  product_id:              string;
  transaction_id:          string;
  original_transaction_id: string;
  purchase_date_ms:        string;
}

Deno.serve(async (req: Request) => {
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

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: authHeader } },
  });

  const { data: { user }, error: userError } = await userClient.auth.getUser();
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

  const { receipt, product_id } = body;
  if (!receipt || !product_id) {
    return jsonResponse({ ok: false, reason: "missing_fields" }, 400);
  }

  const sharedSecret = await serverConfig("APPLE_SHARED_SECRET");
  if (!sharedSecret) {
    console.error("[purchase-verify-apple-legacy] APPLE_SHARED_SECRET 가 콘솔 설정에도 Secrets 에도 없습니다.");
    return jsonResponse({ ok: false, reason: "server_config_error" }, 500);
  }

  const allowedBundleIds = await serverConfigList("APPLE_BUNDLE_ID");
  if (allowedBundleIds.length === 0) {
    console.error("[purchase-verify-apple-legacy] APPLE_BUNDLE_ID 가 콘솔 설정에도 Secrets 에도 없습니다.");
    return jsonResponse({ ok: false, reason: "server_config_error" }, 500);
  }

  // ── Apple verifyReceipt 호출 ───────────────────────────────────────────────
  const applePayload = { "receipt-data": receipt, password: sharedSecret };

  let verifyData: VerifyReceiptResponse;
  let viaSandbox = false;
  try {
    verifyData = await callAppleVerifyReceipt(PRODUCTION_URL, applePayload);

    // 21007 = sandbox receipt가 production 서버로 전송됨 → sandbox로 재시도
    if (verifyData.status === 21007) {
      verifyData = await callAppleVerifyReceipt(SANDBOX_URL, applePayload);
      viaSandbox = true;
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

  const receiptBundleId = verifyData.receipt?.bundle_id;
  if (!receiptBundleId || !allowedBundleIds.includes(receiptBundleId)) {
    console.warn(`[purchase-verify-apple-legacy] bundle_id_mismatch: receipt=${receiptBundleId}, caller=${user.id}`);
    return jsonResponse({ ok: false, reason: "bundle_id_mismatch" });
  }

  // product_id 일치하는 트랜잭션 중, 아직 기록되지 않은 가장 최근 것을 고른다.
  // 영수증에는 같은 상품 거래가 여럿 들어 있을 수 있다. 무조건 최신 것만 고르면 같은 소모품이 두 건 대기 중일 때
  // 두 번째 주문도 첫 거래로 풀려 already_granted 가 되고, 게임이 지급을 건너뛰어 한 건을 잃는다.
  const receipts = [...(verifyData.latest_receipt_info ?? []), ...(verifyData.receipt?.in_app ?? [])];
  const candidates = receipts
    .filter(r => r.product_id === product_id && r.transaction_id)
    .sort((a, b) => Number(b.purchase_date_ms) - Number(a.purchase_date_ms));

  if (candidates.length === 0) {
    return jsonResponse({ ok: false, reason: "product_not_found_in_receipt" });
  }

  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const { data: recorded, error: recordedError } = await adminClient
    .from("purchases")
    .select("purchase_token")
    .in("purchase_token", candidates.map(r => r.transaction_id));
  if (recordedError) {
    console.error("[purchase-verify-apple-legacy] 기록 조회 오류:", recordedError.message);
    return jsonResponse({ ok: false, reason: "db_error" }, 500);
  }
  const recordedIds = new Set((recorded ?? []).map(r => r.purchase_token as string));
  // 전부 기록됐으면 최신 것으로 — 아래 UNIQUE 경로가 "이미 검증됨"(주인 확인 포함)으로 답한다.
  const match = candidates.find(r => !recordedIds.has(r.transaction_id)) ?? candidates[0];

  const transactionId = match.transaction_id;

  const { data: profile } = await userClient
    .from("user_profiles").select("user_id").eq("account_id", user.id).maybeSingle();
  const userId: string | null = profile?.user_id ?? null;

  // ── 중복 검증 체크 + 기록 삽입 ────────────────────────────────────────────
  // 구매 기록은 영수증 검증을 통과한 이 함수만 쓸 수 있어야 하므로 service_role로 기록한다.
  // (유저 직접 INSERT를 막아 가짜 결제 기록을 차단. account_id는 JWT로 검증된 user.id.)
  // purchase_token UNIQUE — INSERT 성공은 새 검증, 23505는 이미 검증된 영수증.
  // SK1 영수증에는 가격이 없어 price_* 는 기록하지 않는다.
  const { error: insertError } = await adminClient
    .from("purchases")
    .insert({
      account_id:     user.id,
      user_id:        userId,
      product_id,
      purchase_token: transactionId,
      order_id:       transactionId,
      package_name:   receiptBundleId,
      store:          "apple_app_store",
      // 시험 결제(TestFlight·앱 심사·샌드박스 계정)는 매출에서 뺀다. 샌드박스 주소로 다시 물어 통과했으면 시험이 확실하다.
      // 응답에 environment 가 없고 운영 주소에서 통과했으면 모름(null).
      is_test:        viaSandbox || verifyData.environment === "Sandbox" ? true
                    : verifyData.environment ? false : null,
    });

  let alreadyVerified = false;
  let alreadyGranted = false;
  if (insertError) {
    if (insertError.code === "23505") {
      // unique_violation — 이미 검증된 영수증. 단, 기록의 주인이 이 계정일 때만 재처리로 인정한다.
      // (다른 계정의 토큰을 보낸 경우까지 ok=true 로 답하면, 크래시 복구 지침대로 구현한 게임이 남의 결제로 지급한다)
      const { data: owner } = await adminClient
        .from("purchases")
        .select("account_id, granted_at")
        .eq("purchase_token", transactionId)
        .maybeSingle();
      if (owner && owner.account_id !== user.id) {
        return jsonResponse({ ok: false, reason: "purchase_owned_by_other_account" }, 409);
      }
      alreadyVerified = true;
      alreadyGranted = !!owner?.granted_at;
    } else {
      console.error("[purchase-verify-apple-legacy] DB 삽입 오류:", insertError.message);
      return jsonResponse({ ok: false, reason: "db_error" }, 500);
    }
  }

  return jsonResponse({
    ok:               true,
    already_verified: alreadyVerified,
    already_granted:  alreadyGranted,
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
