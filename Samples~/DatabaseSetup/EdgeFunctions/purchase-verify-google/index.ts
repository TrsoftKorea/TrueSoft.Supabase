import { createClient } from "npm:@supabase/supabase-js@2";

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
  order_id?: string;
  purchase_state?: number;
  reason?: string;
};

// Google Play Developer API 응답 (inapp product)
type GoogleProductPurchase = {
  purchaseState?: number;  // 0=purchased, 1=cancelled, 2=pending
  orderId?: string;
  consumptionState?: number;
  purchaseTimeMillis?: string;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const GOOGLE_SERVICE_ACCOUNT_JSON = Deno.env.get("GOOGLE_SERVICE_ACCOUNT_JSON")!;

if (!GOOGLE_SERVICE_ACCOUNT_JSON) {
  throw new Error("GOOGLE_SERVICE_ACCOUNT_JSON is required");
}

// Google 서비스 계정 JSON → OAuth2 액세스 토큰 발급
// RS256 JWT를 서명해 google token endpoint에 교환 요청합니다.
async function getGoogleAccessToken(): Promise<string> {
  const sa = JSON.parse(GOOGLE_SERVICE_ACCOUNT_JSON);
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

  // Google Play Developer API 호출
  let googlePurchase: GoogleProductPurchase;
  try {
    const accessToken = await getGoogleAccessToken();
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

  // 구매 상태 확인 (0 = purchased)
  if (googlePurchase.purchaseState !== 0) {
    return json({
      ok: false,
      purchase_state: googlePurchase.purchaseState ?? -1,
      reason: "not_purchased",
    } satisfies VerifyResponse);
  }

  // user_id 조회 (RLS 적용 userClient 사용)
  let userId: string | null = null;
  const { data: profile, error: profileError } = await userClient
    .from("user_profiles")
    .select("user_id")
    .maybeSingle();
  if (!profileError) {
    userId = profile?.user_id ?? null;
  } else {
    // RLS 실패 등의 조회 오류는 로그만 남기고 계속 진행 (user_id = null 허용)
    console.error(`[purchase-verify-google] profile_query_error: ${profileError.message}`);
  }

  // KRW 환산 (price_amount가 있을 때만)
  const priceAmountKrw = (price_amount && price_amount > 0)
    ? await convertToKrw(price_amount, price_currency || "KRW")
    : null;

  // 구매 기록 INSERT (purchase_token UNIQUE — 충돌 시 중복 요청으로 처리)
  const { error: insertError } = await userClient
    .from("purchases")
    .insert({
      account_id: user.id,
      user_id: userId,
      product_id,
      purchase_token,
      order_id: googlePurchase.orderId ?? null,
      package_name,
      purchase_state: googlePurchase.purchaseState,
      price_amount: (price_amount && price_amount > 0) ? price_amount : null,
      price_currency: price_currency || null,
      price_amount_krw: priceAmountKrw,
    });

  if (insertError) {
    // UNIQUE 위반 → 이미 검증된 영수증
    if (insertError.code === "23505") {
      return json({
        ok: true,
        already_verified: true,
        order_id: googlePurchase.orderId,
        purchase_state: googlePurchase.purchaseState,
      } satisfies VerifyResponse);
    }
    return json({ ok: false, reason: insertError.message } satisfies VerifyResponse, 500);
  }

  return json({
    ok: true,
    already_verified: false,
    order_id: googlePurchase.orderId,
    purchase_state: googlePurchase.purchaseState,
  } satisfies VerifyResponse);
});
