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

type VerifyRequest = {
  purchase_token?: string;
  product_id?: string;
  package_name?: string;
  price_amount?: number;
  price_currency?: string;
};

type VerifyResponse = {
  ok: boolean;
  already_verified?: boolean;
  already_granted?: boolean;
  order_id?: string;
  reason?: string;
};

// Google Play Developer API 응답 (inapp product)
type GoogleProductPurchase = {
  purchaseState?: number;  // 0=purchased, 1=cancelled, 2=pending
  orderId?: string;
  consumptionState?: number;
  purchaseTimeMillis?: string;
  // 일반 결제가 아닐 때만 온다. 0 = 라이선스 테스트 계정의 시험 결제, 1 = 프로모션 코드, 2 = 광고 보상.
  purchaseType?: number;
  obfuscatedExternalAccountId?: string;  // 결제 시점에 클라이언트가 SetObfuscatedAccountId로 심어둔 계정 id
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;
// ═══ BEGIN google-token ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// Google 서비스 계정 JSON → OAuth2 액세스 토큰 발급
// RS256 JWT를 서명해 google token endpoint에 교환 요청합니다.
async function getGoogleAccessToken(serviceAccountJson: string): Promise<string> {
  const sa = JSON.parse(serviceAccountJson);
  const scope = "https://www.googleapis.com/auth/androidpublisher";
  const aud = "https://oauth2.googleapis.com/token";

  const now = Math.floor(Date.now() / 1000);
  const header = { alg: "RS256", typ: "JWT" };
  const payload = {
    iss: sa.client_email,
    scope,
    aud,
    iat: now,
    exp: now + 3600,
  };

  function b64url(obj: unknown): string {
    const json = JSON.stringify(obj);
    const bytes = new TextEncoder().encode(json);
    return btoa(String.fromCharCode(...bytes))
      .replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
  }

  const signingInput = `${b64url(header)}.${b64url(payload)}`;

  // PEM private key → CryptoKey
  const pemBody = sa.private_key
    .replace(/-----BEGIN PRIVATE KEY-----/, "")
    .replace(/-----END PRIVATE KEY-----/, "")
    .replace(/\s+/g, "");
  const keyDer = Uint8Array.from(atob(pemBody), (c) => c.charCodeAt(0));
  const cryptoKey = await crypto.subtle.importKey(
    "pkcs8",
    keyDer,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  );

  const sigBytes = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    cryptoKey,
    new TextEncoder().encode(signingInput),
  );
  const sig = btoa(String.fromCharCode(...new Uint8Array(sigBytes)))
    .replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");

  const assertion = `${signingInput}.${sig}`;

  const tokenRes = await fetch(aud, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
      assertion,
    }),
  });

  const tokenJson = await tokenRes.json();
  if (!tokenJson.access_token) {
    throw new Error(`google_token_error: ${JSON.stringify(tokenJson)}`);
  }
  return tokenJson.access_token as string;
}
// ═══ END google-token ═══

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

  const { purchase_token, product_id, package_name, price_amount, price_currency } = body;
  if (!purchase_token || !product_id || !package_name) {
    return json({ ok: false, reason: "missing_fields" } satisfies VerifyResponse, 400);
  }

  // 이 게임의 앱 결제만 받는다 — 게임이 보낸 패키지 이름을 그대로 믿으면 구글이 확인해 주는 다른 앱(같은 개발자 계정의
  // 다른 게임·시험 앱)의 결제도 기록돼 매출에 섞였다(2026-10-29). 애플의 번들 ID 검사와 같은 자리다. 비어 있으면 전부 거절한다.
  const allowedPackages = await serverConfigList("GOOGLE_PACKAGE_NAME");
  if (allowedPackages.length === 0) {
    console.error("[purchase-verify-google] GOOGLE_PACKAGE_NAME 이 콘솔 설정에도 Secrets 에도 없습니다.");
    return json({ ok: false, reason: "server_config_error" } satisfies VerifyResponse, 500);
  }
  // 대소문자는 가리지 않는다 — 콘솔에 소문자로 넣어도 결제가 전부 막히지 않게. 아래 구글 조회가 게임이 보낸 이름 그대로
  // 토큰을 확인하므로 이걸로 다른 앱 결제가 들어오지는 않는다.
  if (!allowedPackages.some((p) => p.toLowerCase() === package_name.toLowerCase())) {
    console.warn(`[purchase-verify-google] package_name_mismatch: ${package_name}`);
    return json({ ok: false, reason: "package_name_mismatch" } satisfies VerifyResponse);
  }

  // Google Play Developer API 호출
  const serviceAccountJson = await serverConfig("GOOGLE_SERVICE_ACCOUNT_JSON");
  if (!serviceAccountJson) {
    console.error("[purchase-verify-google] GOOGLE_SERVICE_ACCOUNT_JSON 이 콘솔 설정에도 Secrets 에도 없습니다.");
    return json({ ok: false, reason: "server_config_error" } satisfies VerifyResponse, 500);
  }

  let googlePurchase: GoogleProductPurchase;
  try {
    const accessToken = await getGoogleAccessToken(serviceAccountJson);
    const apiUrl =
      `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${encodeURIComponent(package_name)}/purchases/products/${encodeURIComponent(product_id)}/tokens/${encodeURIComponent(purchase_token)}`;

    const apiRes = await fetch(apiUrl, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });

    if (!apiRes.ok) {
      const errText = await apiRes.text();
      return json(
        { ok: false, reason: `google_api_error_${apiRes.status}: ${errText}` } satisfies VerifyResponse,
        502,
      );
    }
    googlePurchase = await apiRes.json();
  } catch (e) {
    return json({ ok: false, reason: `google_api_exception: ${e}` } satisfies VerifyResponse, 502);
  }

  // 구매 상태 확인 (0 = purchased) — 완료 건만 기록, 취소·대기는 저장하지 않음
  if (googlePurchase.purchaseState !== 0) {
    return json({
      ok: false,
      reason: "not_purchased",
    } satisfies VerifyResponse);
  }

  // 결제 시점에 클라이언트가 SetObfuscatedAccountId로 심어둔 계정과 지금 검증을 요청한 계정이
  // 다르면 거부한다. 기기에서 계정을 바꿔 로그인한 사이, 이전 계정이 결제만 하고 검증 전에 남긴
  // 미처리 주문이 새 계정으로 넘어와 그대로 지급되는 것을 막는다. (아직 한 번도 검증되지 않은
  // 주문이라 purchase_token UNIQUE 검사만으로는 못 막는다 — 이 주문의 DB 행이 아직 없기 때문.)
  if (googlePurchase.obfuscatedExternalAccountId && googlePurchase.obfuscatedExternalAccountId !== user.id) {
    return json({ ok: false, reason: "purchase_owned_by_other_account" } satisfies VerifyResponse, 409);
  }

  // user_profiles는 SELECT가 공개이므로 account_id로 본인 행을 명시 필터
  let userId: string | null = null;
  const { data: profile, error: profileError } = await userClient
    .from("user_profiles")
    .select("user_id")
    .eq("account_id", user.id)
    .maybeSingle();
  if (!profileError) {
    userId = profile?.user_id ?? null;
  } else {
    // RLS 실패 등의 조회 오류는 로그만 남기고 계속 진행 (user_id = null 허용)
    console.error(`[purchase-verify-google] profile_query_error: ${profileError.message}`);
  }

  // 구매 기록은 영수증 검증을 통과한 이 함수만 쓸 수 있어야 하므로 service_role로 기록한다.
  // (유저 직접 INSERT를 막아 total_paid_krw 조작·가짜 결제 기록을 차단. account_id는 JWT로 검증된 user.id.)
  // purchase_token UNIQUE — 충돌 시 중복 요청으로 처리
  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  // KRW 환산 (price_amount가 있을 때만). 통화를 모르면 원화로 치지 않는다 — null 로 남겨 "금액 모름"으로 센다.
  const priceAmountKrw = (price_amount && price_amount > 0)
    ? await toKrw(adminClient, price_amount, price_currency || null, new Date())
    : null;
  const { error: insertError } = await adminClient
    .from("purchases")
    .insert({
      account_id: user.id,
      user_id: userId,
      product_id,
      purchase_token,
      order_id: googlePurchase.orderId ?? null,
      package_name,
      price_amount: (price_amount && price_amount > 0) ? price_amount : null,
      price_currency: price_currency || null,
      price_amount_krw: priceAmountKrw,
      // 라이선스 테스트 계정의 시험 결제, 프로모션 코드·광고 보상(무료)은 매출에서 뺀다.
      is_test: googlePurchase.purchaseType === 0,
      is_free: googlePurchase.purchaseType === 1 || googlePurchase.purchaseType === 2,
    });

  if (insertError) {
    // UNIQUE 위반 → 이미 검증된 영수증. 단, 기록의 주인이 이 계정일 때만 재처리로 인정한다.
    // (다른 계정의 토큰을 보낸 경우까지 ok=true 로 답하면, 크래시 복구 지침대로 구현한 게임이 남의 결제로 지급한다)
    if (insertError.code === "23505") {
      const { data: owner } = await adminClient
        .from("purchases")
        .select("account_id, granted_at")
        .eq("purchase_token", purchase_token)
        .maybeSingle();
      if (owner && owner.account_id !== user.id) {
        return json({ ok: false, reason: "purchase_owned_by_other_account" } satisfies VerifyResponse, 409);
      }
      return json({
        ok: true,
        already_verified: true,
        already_granted: !!owner?.granted_at,
        order_id: googlePurchase.orderId,
      } satisfies VerifyResponse);
    }
    return json({ ok: false, reason: insertError.message } satisfies VerifyResponse, 500);
  }

  return json({
    ok: true,
    already_verified: false,
    already_granted: false,
    order_id: googlePurchase.orderId,
  } satisfies VerifyResponse);
});
