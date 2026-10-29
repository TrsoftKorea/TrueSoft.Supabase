// purchase-verify-apple-legacy
// SK1 (verifyReceipt) 기반 Apple IAP 서버 검증.
// iOS 14 이하 또는 forceStoreKit1 경로에서 생성된 receipt blob을 검증합니다.
//
// 필요한 값(운영 콘솔 "설정" 또는 Edge Function Secrets):
//   APPLE_SHARED_SECRET  — App Store Connect > 앱 정보 > 공유 암호 (shared secret)
//   APPLE_BUNDLE_ID      — 이 프로젝트가 받는 앱 번들 ID. 여럿이면 쉼표로 구분.
//                          verifyReceipt 는 어느 앱의 영수증이든 진짜면 status 0 을 준다(소모성 상품은
//                          공유 암호도 안 본다). 서버가 번들 ID를 대조하지 않으면 다른 앱 영수증이 통과한다.
//   APPLE_IAP_ISSUER_ID·APPLE_IAP_KEY_ID·APPLE_IAP_PRIVATE_KEY — 선택. App Store Connect 의 In-App Purchase 키.
//                          SK1 영수증에는 가격이 없어, 있으면 기록 뒤(응답을 돌려준 다음) 애플에 그 거래의 실제
//                          결제 금액을 물어 채운다. 없거나 실패하면 purchase-price-sync 가 하루 한 번 다시 묻는다.
//
// 요청 Body (JSON):
//   receipt    string  — SK1 base64 encoded receipt blob (Unity IAP 영수증의 Payload 필드)
//   product_id string  — 기대하는 상품 ID
//   bundle_id  string  — 앱 Bundle ID. 앱이 보내는 값이라 대조에는 쓰지 않는다(호환용으로만 받는다)
//
// 기록은 purchases.purchase_token UNIQUE 로 중복을 막습니다. 유저 직접 INSERT 정책은 없고
// 이 함수가 service_role 로만 기록합니다(가짜 결제 기록 차단).

import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN server-config ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 스토어·애플 연동 값. 운영 콘솔 "설정"에서 넣은 값(DB Vault)을 먼저 쓰고, 없으면 Edge Function Secrets 를 쓴다.
// 1분 동안 기억해 둔다 — 콘솔에서 바꾼 값은 늦어도 1분 뒤부터 걸린다. 읽기에 실패하면 이미 기억해 둔 값을 계속 쓰고,
// 기억해 둔 값이 없을 때만(함수가 막 깨어났을 때) Secrets 로 넘어간다.
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

// ═══ BEGIN krw-rate ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 결제 금액(micros = 주 단위 ×1,000,000)을 원화 정수로 바꾼다. KRW 면 그대로, 통화를 모르면 null(원화로 치지 않는다).
// 환율은 fx_rates(purchase-price-sync 가 하루 한 번 받아 두는 표)에서 결제 날짜(UTC) 이전 4일 안의 가장 가까운 값 —
// 주말·연휴로 발표가 없는 날을 넘기는 만큼 — 을 쓰고, 표에 없으면 Frankfurter(키 없음, 중앙은행·공식 출처 환율)에
// 그 날짜를 물어 표에 남긴다. 둘 다 안 되면 null — purchase-price-sync 가 다음 실행에서 채운다.
async function toKrw(db: SupabaseClient, micros: number, currency: string | null, at: Date): Promise<number | null> {
  const major = micros / 1_000_000;
  if (!currency) return null;
  const cur = currency.toUpperCase();
  if (cur === "KRW") return Math.round(major);
  const day = at.toISOString().slice(0, 10);
  const windowStart = new Date(at.getTime() - 4 * 86_400_000).toISOString().slice(0, 10);
  try {
    const { data: stored } = await db.from("fx_rates").select("krw_per_unit")
      .eq("currency", cur).lte("rate_date", day).gte("rate_date", windowStart)
      .order("rate_date", { ascending: false }).limit(1).maybeSingle();
    if (stored) return Math.round(major * Number(stored.krw_per_unit));

    const res = await fetch(
      `https://api.frankfurter.dev/v2/rates?base=${encodeURIComponent(cur)}&quotes=KRW&date=${day}`,
      { signal: AbortSignal.timeout(3000) },
    );
    if (!res.ok) {
      await res.body?.cancel();
      return null;
    }
    const rows = (await res.json()) as { date?: string; quote?: string; rate?: number }[];
    const hit = Array.isArray(rows) ? rows.find((r) => r.quote === "KRW") : undefined;
    if (!hit || typeof hit.rate !== "number" || !(hit.rate > 0) || !hit.date) return null;
    await db.from("fx_rates").upsert(
      { currency: cur, rate_date: hit.date, krw_per_unit: hit.rate },
      { onConflict: "currency,rate_date", ignoreDuplicates: true },
    );
    return Math.round(major * hit.rate);
  } catch {
    return null;
  }
}
// ═══ END krw-rate ═══

// ═══ BEGIN apple-transaction ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 애플 App Store Server API "Get Transaction Info" 로 거래 하나의 실제 결제 금액을 읽는다. 옛 방식(StoreKit 1)으로
// 끝낸 거래도 된다(애플 문서). In-App Purchase 키 — 애플 로그인 키와 다른 키 — 의 발급자 ID·키 ID·.p8 이 필요하다.
// 응답의 서명(JWS)은 검증하지 않는다. 우리 키로 애플 주소에 직접 물어 받은 값이라 중간에 바뀔 길이 없다.
type AppleIapKey = { issuerId: string; keyId: string; privateKey: string };
type AppleTxPrice = { priceMicros: number | null; currency: string | null; environment: string | null };

const APPLE_TX_HOSTS = { production: "https://api.storekit.apple.com", sandbox: "https://api.storekit-sandbox.apple.com" };

async function appleIapKey(): Promise<AppleIapKey | null> {
  const [issuerId, keyId, privateKey] = await Promise.all([
    serverConfig("APPLE_IAP_ISSUER_ID"), serverConfig("APPLE_IAP_KEY_ID"), serverConfig("APPLE_IAP_PRIVATE_KEY"),
  ]);
  return issuerId && keyId && privateKey ? { issuerId, keyId, privateKey } : null;
}

const appleTxB64url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

// 애플 문서 "Generating JSON Web Tokens for API requests": ES256, kid=키 ID, iss=발급자 ID,
// aud=appstoreconnect-v1, bid=번들 ID, 만료는 발급 뒤 60분 이내.
async function appleServerToken(key: AppleIapKey, bundleId: string): Promise<string> {
  // 붙여넣다 줄바꿈이 글자 그대로의 "\n" 으로 들어가는 경우가 있어 그것도 걷어 낸다.
  const pem = key.privateKey.replace(/-----[^-]+-----/g, "").replace(/\\n/g, "").replace(/\s+/g, "");
  const der = Uint8Array.from(atob(pem), (c) => c.charCodeAt(0));
  const signKey = await crypto.subtle.importKey("pkcs8", der, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const now = Math.floor(Date.now() / 1000);
  const enc = (o: unknown) => appleTxB64url(new TextEncoder().encode(JSON.stringify(o)));
  const signingInput =
    enc({ alg: "ES256", kid: key.keyId, typ: "JWT" }) + "." +
    enc({ iss: key.issuerId, iat: now, exp: now + 600, aud: "appstoreconnect-v1", bid: bundleId });
  // WebCrypto ECDSA 서명은 JWS 가 요구하는 r||s(64바이트) 형식 그대로다.
  const sig = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, signKey, new TextEncoder().encode(signingInput)));
  return `${signingInput}.${appleTxB64url(sig)}`;
}

// 테스트 결제는 테스트 주소에만 있다. 한쪽에서 못 찾으면(404) 다른 쪽에 묻고, 둘 다 없으면 null.
// 키가 틀렸거나 권한이 없으면(401 등) 예외 — 호출한 쪽이 실패로 남긴다.
async function appleTransactionPrice(
  key: AppleIapKey, bundleId: string, transactionId: string, sandboxFirst: boolean,
): Promise<AppleTxPrice | null> {
  const token = await appleServerToken(key, bundleId);
  const hosts = sandboxFirst
    ? [APPLE_TX_HOSTS.sandbox, APPLE_TX_HOSTS.production]
    : [APPLE_TX_HOSTS.production, APPLE_TX_HOSTS.sandbox];
  for (const host of hosts) {
    const res = await fetch(`${host}/inApps/v1/transactions/${encodeURIComponent(transactionId)}`, {
      headers: { Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5000),
    });
    if (res.status === 404) {
      await res.body?.cancel();
      continue;
    }
    if (!res.ok) {
      const detail = (await res.text()).slice(0, 200);
      throw new Error(`apple_http_${res.status} ${detail}`);
    }
    const body = (await res.json()) as { signedTransactionInfo?: string };
    const part = body.signedTransactionInfo?.split(".")[1];
    if (!part) throw new Error("apple_no_transaction_info");
    const b64 = part.replace(/-/g, "+").replace(/_/g, "/");
    const tx = JSON.parse(new TextDecoder().decode(
      Uint8Array.from(atob(b64 + "=".repeat((4 - (b64.length % 4)) % 4)), (c) => c.charCodeAt(0)),
    )) as { price?: number; currency?: string; environment?: string };
    // price 는 1/1000 단위(milliunits). micros(1/1,000,000)로 맞춘다. 통화가 없으면 금액도 버린다(원화로 오인하지 않게).
    const currency = typeof tx.currency === "string" && tx.currency ? tx.currency : null;
    return {
      priceMicros: typeof tx.price === "number" && currency ? tx.price * 1000 : null,
      currency,
      environment: typeof tx.environment === "string" ? tx.environment : null,
    };
  }
  return null;
}
// ═══ END apple-transaction ═══

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

  // 프로필은 서버 키로 읽는다 — 플레이어 로그인으로 읽으면 요청 제한에 걸린 동안 거절돼 user_id 가 빈 채
  // 기록된다(2026-10-29 리뷰). account_id 는 JWT 로 확인한 user.id 다.
  const { data: profile } = await adminClient
    .from("user_profiles").select("user_id").eq("account_id", user.id).maybeSingle();
  const userId: string | null = profile?.user_id ?? null;

  // ── 중복 검증 체크 + 기록 삽입 ────────────────────────────────────────────
  // 구매 기록은 영수증 검증을 통과한 이 함수만 쓸 수 있어야 하므로 service_role로 기록한다.
  // (유저 직접 INSERT를 막아 가짜 결제 기록을 차단. account_id는 JWT로 검증된 user.id.)
  // purchase_token UNIQUE — INSERT 성공은 새 검증, 23505는 이미 검증된 영수증.
  // 금액은 여기서 묻지 않는다 — 위 "기록됐는지 조회"와 이 기록 사이가 길어지면, 같은 상품 두 건을 동시에 확인할 때
  // 둘 다 같은 거래를 골라 한 건을 잃는다. 기록한 뒤 아래에서 따로 채운다.
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
      // 응답에 environment 가 없고 운영 주소에서 통과했으면 모름(null) — 아래 금액 조회가 애플 거래 정보로 채운다.
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
  } else {
    // 새로 기록한 결제만, 응답을 돌려준 뒤 애플에 실제 결제 금액을 물어 채운다(게임은 기다리지 않는다).
    // 키가 없거나 실패하면 그대로 두고 purchase-price-sync 가 하루 한 번 다시 묻는다.
    runAfterResponse(fillPriceFromApple(adminClient, transactionId, receiptBundleId, viaSandbox));
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

// Supabase 문서 "Background Tasks": EdgeRuntime.waitUntil 에 넘긴 작업은 응답 뒤에도 끝까지 돈다.
// 그 기능이 없는 곳(로컬 실행 등)에서는 돌게만 둔다 — 중간에 끊기면 purchase-price-sync 가 대신 채운다.
function runAfterResponse(task: Promise<void>): void {
  const runtime = (globalThis as unknown as { EdgeRuntime?: { waitUntil(p: Promise<unknown>): void } }).EdgeRuntime;
  if (runtime?.waitUntil) runtime.waitUntil(task);
  else void task;
}

async function fillPriceFromApple(
  db: SupabaseClient, transactionId: string, bundleId: string, sandboxFirst: boolean,
): Promise<void> {
  try {
    const key = await appleIapKey();
    if (!key) return;
    const price = await appleTransactionPrice(key, bundleId, transactionId, sandboxFirst);
    const update: Record<string, unknown> = { price_checked_at: new Date().toISOString() };
    if (price?.priceMicros != null) {
      update.price_amount = price.priceMicros;
      update.price_currency = price.currency;
      update.price_amount_krw = await toKrw(db, price.priceMicros, price.currency, new Date());
    }
    const { error } = await db.from("purchases").update(update)
      .eq("purchase_token", transactionId).eq("store", "apple_app_store").is("price_amount", null);
    if (error) throw new Error(error.message);
    if (price?.environment) {
      await db.from("purchases").update({ is_test: price.environment === "Sandbox" })
        .eq("purchase_token", transactionId).eq("store", "apple_app_store").is("is_test", null);
    }
  } catch (e) {
    console.warn(`[purchase-verify-apple-legacy] 결제 금액 조회 실패(하루 한 번 다시 묻는다): ${e instanceof Error ? e.message : e}`);
  }
}

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
