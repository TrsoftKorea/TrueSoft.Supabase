// purchase-verify-apple
// StoreKit 2 (JWS) 기반 Apple IAP 서버 검증.
//
// 필요 환경 변수:
//   APPLE_BUNDLE_ID  — 이 프로젝트가 받는 앱 번들 ID. 여럿이면 쉼표로 구분.
//                      영수증은 그 안에 모든 정보가 들어 있어, 서버가 번들 ID를 따로 알고 있지 않으면
//                      다른 앱(샌드박스에서 공짜로 산 것 포함)의 진짜 영수증을 우리 결제로 인정하게 된다.
//                      앱이 보내는 bundle_id 는 위조할 수 있어 대조에 쓰지 않는다. 없으면 검증을 거부한다.

import { createClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN apple-jws ═══ (Tools~/AppleJwsCheck 가 이 구간만 떼어 내 Node 로 시험한다 — 구간 밖의 것을 참조하지 말 것)
// StoreKit 2 JWS(signedTransactionInfo) 검증.
//
// JWS 헤더의 x5c 는 [리프, 중간, 루트] 인증서 체인이다. 헤더에 딸려 온 인증서로 서명만 확인하면
// 누구든 자기 인증서를 넣어 서명한 가짜 영수증이 통과한다 — 그래서 아래를 모두 확인한다.
//   1) 루트가 애플 루트(Apple Root CA - G3)와 바이트 단위로 같다
//   2) 중간 인증서가 루트로, 리프가 중간 인증서로 서명됐다
//   3) 애플이 정한 표식 확장(OID)이 중간·리프에 있다
//   4) JWS 서명이 리프 키로 맞는다
//   5) 세 인증서가 거래 서명 시각(signedDate)에 유효했다 — 오래된 거래를 다시 검증해도 통과하도록
//      "지금"이 아니라 서명 시각을 기준으로 삼는다(애플 공식 라이브러리와 같은 기준)
//
// 외부 라이브러리 없이 WebCrypto 만 쓴다 — Deno(엣지 함수)와 Node(로컬 시험)에서 같은 코드가 돈다.

// https://www.apple.com/certificateauthority/AppleRootCA-G3.cer
// SHA-256 63:34:3A:BF:B8:9A:6A:03:EB:B5:7E:9B:3F:5F:A7:BE:7C:4F:5C:75:6F:30:17:B3:A8:C4:88:C3:65:3E:91:79
const APPLE_ROOT_CA_G3_B64 =
  "MIICQzCCAcmgAwIBAgIILcX8iNLFS5UwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMTQwNDMwMTgxOTA2WhcNMzkwNDMwMTgxOTA2WjBnMRswGQYDVQQDDBJBcHBsZSBSb290IENBIC0gRzMxJjAkBgNVBAsMHUFwcGxlIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzB2MBAGByqGSM49AgEGBSuBBAAiA2IABJjpLz1AcqTtkyJygRMc3RCV8cWjTnHcFBbZDuWmBSp3ZHtfTjjTuxxEtX/1H7YyYl3J6YRbTzBPEVoA/VhYDKX1DyxNB0cTddqXl5dvMVztK517IDvYuVTZXpmkOlEKMaNCMEAwHQYDVR0OBBYEFLuw3qFYM4iapIqZ3r6966/ayySrMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMDA2gAMGUCMQCD6cHEFl4aXTQY2e3v9GwOAEZLuN+yRhHFD/3meoyhpmvOwgPUnPWTxnS4at+qIxUCMG1mihDK1A3UT82NQz60imOlM27jbdoXt2QfyFMm+YhidDkLF1vLUagM6BgD56KyKA==";

// 애플이 App Store 서명 인증서에 넣는 표식. 없으면 애플 루트 아래의 다른 용도 인증서다.
const OID_APPLE_RECEIPT_SIGNING_LEAF = "1.2.840.113635.100.6.11.1";
const OID_APPLE_WWDR_INTERMEDIATE    = "1.2.840.113635.100.6.2.1";

const OID_ECDSA_SHA256 = "1.2.840.10045.4.3.2";
const OID_ECDSA_SHA384 = "1.2.840.10045.4.3.3";
const OID_CURVE_P256   = "1.2.840.10045.3.1.7";
const OID_CURVE_P384   = "1.3.132.0.34";

export class AppleJwsError extends Error {}

type Tlv = { tag: number; start: number; end: number; headerStart: number };

// 입력은 공격자가 보낸 바이트다. 모든 길이를 부모 경계(limit) 안으로 가두고, 각 원소가 반드시 앞으로
// 나아가게 한다 — 그러지 않으면 음수·0 길이로 children 이 제자리를 돌며 메모리를 다 먹는다.
function readTlv(b: Uint8Array, pos: number, limit: number = b.length): Tlv {
  const headerStart = pos;
  if (pos + 2 > limit) throw new AppleJwsError("der_truncated");
  const tag = b[pos++];
  let len = b[pos++];
  if (len & 0x80) {
    const n = len & 0x7f;
    if (n === 0 || n > 3) throw new AppleJwsError("der_bad_length");   // 인증서는 16MB 를 넘지 않는다
    if (pos + n > limit) throw new AppleJwsError("der_truncated");
    len = 0;
    for (let i = 0; i < n; i++) len = len * 256 + b[pos++];
  }
  const end = pos + len;
  if (end > limit) throw new AppleJwsError("der_truncated");
  return { tag, start: pos, end, headerStart };
}

function children(b: Uint8Array, parent: Tlv): Tlv[] {
  const out: Tlv[] = [];
  for (let p = parent.start; p < parent.end;) {
    const t = readTlv(b, p, parent.end);
    out.push(t);
    p = t.end;   // 헤더가 최소 2바이트라 t.end > p 가 보장된다
  }
  return out;
}

function oidToString(b: Uint8Array, t: Tlv): string {
  if (t.tag !== 0x06) throw new AppleJwsError("der_expected_oid");
  const v = b.subarray(t.start, t.end);
  const parts = [Math.floor(v[0] / 40), v[0] % 40];
  let acc = 0;
  for (let i = 1; i < v.length; i++) {
    acc = acc * 128 + (v[i] & 0x7f);
    if ((v[i] & 0x80) === 0) { parts.push(acc); acc = 0; }
  }
  return parts.join(".");
}

function parseTime(b: Uint8Array, t: Tlv): number {
  const s = new TextDecoder().decode(b.subarray(t.start, t.end));
  let y: number, rest: string;
  if (t.tag === 0x17) {           // UTCTime YYMMDDHHMMSSZ
    const yy = Number(s.slice(0, 2));
    y = yy >= 50 ? 1900 + yy : 2000 + yy;
    rest = s.slice(2);
  } else if (t.tag === 0x18) {    // GeneralizedTime YYYYMMDDHHMMSSZ
    y = Number(s.slice(0, 4));
    rest = s.slice(4);
  } else {
    throw new AppleJwsError("der_bad_time");
  }
  return Date.UTC(y, Number(rest.slice(0, 2)) - 1, Number(rest.slice(2, 4)),
    Number(rest.slice(4, 6)), Number(rest.slice(6, 8)), Number(rest.slice(8, 10)));
}

type ParsedCert = {
  der: Uint8Array;
  tbs: Uint8Array;
  sigAlg: string;
  sigDer: Uint8Array;
  notBefore: number;
  notAfter: number;
  spki: Uint8Array;
  curve: string;
  extensionOids: Set<string>;
};

function parseCert(der: Uint8Array): ParsedCert {
  const cert = readTlv(der, 0);
  const [tbsT, sigAlgT, sigT] = children(der, cert);
  if (!tbsT || !sigAlgT || !sigT || sigT.tag !== 0x03) throw new AppleJwsError("cert_malformed");

  const sigAlg = oidToString(der, children(der, sigAlgT)[0]);
  const sigDer = der.subarray(sigT.start + 1, sigT.end);   // BIT STRING 첫 바이트(남는 비트 수) 제외

  let tbsItems = children(der, tbsT);
  if (tbsItems[0].tag === 0xa0) tbsItems = tbsItems.slice(1);   // [0] version
  // serial, signature, issuer, validity, subject, spki, [extensions...]
  const validity = children(der, tbsItems[3]);
  const spkiT = tbsItems[5];
  const spkiAlg = children(der, children(der, spkiT)[0]);
  const curve = spkiAlg[1] ? oidToString(der, spkiAlg[1]) : "";

  const extensionOids = new Set<string>();
  const extWrap = tbsItems.find((t) => t.tag === 0xa3);
  if (extWrap) {
    for (const ext of children(der, children(der, extWrap)[0])) {
      extensionOids.add(oidToString(der, children(der, ext)[0]));
    }
  }

  return {
    der,
    tbs: der.subarray(tbsT.headerStart, tbsT.end),
    sigAlg,
    sigDer,
    notBefore: parseTime(der, validity[0]),
    notAfter: parseTime(der, validity[1]),
    spki: der.subarray(spkiT.headerStart, spkiT.end),
    curve,
    extensionOids,
  };
}

// DER ECDSA-Sig-Value(SEQUENCE{r,s}) → WebCrypto 가 받는 고정 길이 r||s
function ecdsaDerToRaw(sig: Uint8Array, size: number): Uint8Array {
  const seq = readTlv(sig, 0);
  const [r, s] = children(sig, seq);
  const out = new Uint8Array(size * 2);
  const put = (t: Tlv, offset: number) => {
    let v = sig.subarray(t.start, t.end);
    while (v.length > size && v[0] === 0) v = v.subarray(1);
    if (v.length > size) throw new AppleJwsError("ecdsa_bad_integer");
    out.set(v, offset + size - v.length);
  };
  put(r, 0);
  put(s, size);
  return out;
}

async function importEcKey(cert: ParsedCert): Promise<{ key: CryptoKey; size: number }> {
  const namedCurve = cert.curve === OID_CURVE_P256 ? "P-256"
    : cert.curve === OID_CURVE_P384 ? "P-384" : null;
  if (!namedCurve) throw new AppleJwsError("cert_unsupported_curve");
  const key = await crypto.subtle.importKey("spki", cert.spki, { name: "ECDSA", namedCurve }, false, ["verify"]);
  return { key, size: namedCurve === "P-256" ? 32 : 48 };
}

async function assertSignedBy(child: ParsedCert, issuer: ParsedCert, failCode: string): Promise<void> {
  const hash = child.sigAlg === OID_ECDSA_SHA256 ? "SHA-256"
    : child.sigAlg === OID_ECDSA_SHA384 ? "SHA-384" : null;
  if (!hash) throw new AppleJwsError("cert_unsupported_signature");
  const { key, size } = await importEcKey(issuer);
  const ok = await crypto.subtle.verify({ name: "ECDSA", hash }, key, ecdsaDerToRaw(child.sigDer, size), child.tbs);
  if (!ok) throw new AppleJwsError(failCode);
}

function b64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

function b64UrlToBytes(s: string): Uint8Array {
  const b64 = s.replace(/-/g, "+").replace(/_/g, "/");
  return b64ToBytes(b64 + "=".repeat((4 - (b64.length % 4)) % 4));
}

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

export type AppleJwsOptions = {
  // 시험용으로만 바꾼다. 운영에서는 기본값(애플 루트)을 쓴다.
  rootCertificateB64?: string;
};

/**
 * StoreKit 2 JWS 를 검증하고 페이로드를 돌려준다. 하나라도 어긋나면 AppleJwsError 를 던진다.
 * 번들 ID·환경 대조는 호출부 몫이다(서버 설정을 아는 쪽이 판단한다).
 */
export async function verifyAppleJws<T extends { signedDate?: number }>(
  jws: string,
  options: AppleJwsOptions = {},
): Promise<T> {
  const parts = jws.split(".");
  if (parts.length !== 3) throw new AppleJwsError("jws_malformed");
  const [h, p, s] = parts;

  let header: { alg?: string; x5c?: string[] };
  try {
    header = JSON.parse(new TextDecoder().decode(b64UrlToBytes(h)));
  } catch {
    throw new AppleJwsError("jws_bad_header");
  }
  if (header.alg !== "ES256") throw new AppleJwsError("jws_unexpected_alg");
  if (!Array.isArray(header.x5c) || header.x5c.length !== 3) throw new AppleJwsError("jws_bad_x5c");

  const [leaf, intermediate, root] = header.x5c.map((c) => parseCert(b64ToBytes(c)));

  const pinnedRoot = b64ToBytes(options.rootCertificateB64 ?? APPLE_ROOT_CA_G3_B64);
  if (!sameBytes(root.der, pinnedRoot)) throw new AppleJwsError("cert_root_not_apple");

  if (!intermediate.extensionOids.has(OID_APPLE_WWDR_INTERMEDIATE)) throw new AppleJwsError("cert_intermediate_not_apple");
  if (!leaf.extensionOids.has(OID_APPLE_RECEIPT_SIGNING_LEAF)) throw new AppleJwsError("cert_leaf_not_apple");

  await assertSignedBy(intermediate, root, "cert_intermediate_signature_invalid");
  await assertSignedBy(leaf, intermediate, "cert_leaf_signature_invalid");

  // JWS 서명은 이미 r||s(64바이트) 형식이다.
  const { key } = await importEcKey(leaf);
  const signed = new TextEncoder().encode(`${h}.${p}`);
  const ok = await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, b64UrlToBytes(s), signed);
  if (!ok) throw new AppleJwsError("jws_signature_invalid");

  const payload = JSON.parse(new TextDecoder().decode(b64UrlToBytes(p))) as T;

  const at = typeof payload.signedDate === "number" ? payload.signedDate : Date.now();
  for (const c of [leaf, intermediate, root]) {
    if (at < c.notBefore || at > c.notAfter) throw new AppleJwsError("cert_not_valid_at_signed_date");
  }

  return payload;
}
// ═══ END apple-jws ═══

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
  signedDate?: number;
  environment?: string;   // "Production" | "Sandbox" (TestFlight·앱 심사 포함) | "Xcode" 등
  type?: string;
  appAccountToken?: string;  // 결제 시점에 클라이언트가 SetAppAccountToken으로 심어둔 계정 id
};

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const publishableKeys     = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys          = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

const ALLOWED_BUNDLE_IDS = (Deno.env.get("APPLE_BUNDLE_ID") ?? "")
  .split(",").map((s) => s.trim()).filter((s) => s.length > 0);

// 앱 심사와 TestFlight 는 Sandbox 로 결제한다 — 막으면 심사가 결제를 못 해 거절된다.
// Xcode 로컬 StoreKit 시험 영수증은 애플 체인이 아니라 여기까지 오지도 못한다.
const ACCEPTED_ENVIRONMENTS = new Set(["Production", "Sandbox"]);

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

  const { jws_token, product_id } = body;

  if (ALLOWED_BUNDLE_IDS.length === 0) {
    console.error("[purchase-verify-apple] APPLE_BUNDLE_ID 환경 변수가 없습니다.");
    return json({ ok: false, reason: "server_config_error" } satisfies VerifyResponse, 500);
  }

  if (!product_id) {
    return json({ ok: false, reason: "missing_product_id" } satisfies VerifyResponse, 400);
  }

  if (!jws_token) {
    return json({ ok: false, reason: "missing_jws_token" } satisfies VerifyResponse, 400);
  }

  // ── StoreKit 2 JWS 검증 ───────────────────────────────────────────────────
  let jwsData: JWSTransactionPayload;
  try {
    jwsData = await verifyAppleJws<JWSTransactionPayload>(jws_token);
  } catch (e) {
    const code = e instanceof AppleJwsError ? e.message : String(e);
    console.warn(`[purchase-verify-apple] JWS 거절: ${code}, caller=${user.id}`);
    return json({ ok: false, reason: `jws_verify_failed: ${code}` } satisfies VerifyResponse, 400);
  }

  if (!jwsData.bundleId || !ALLOWED_BUNDLE_IDS.includes(jwsData.bundleId)) {
    console.warn(`[purchase-verify-apple] bundle_id_mismatch: jws=${jwsData.bundleId}, caller=${user.id}`);
    return json({ ok: false, reason: "bundle_id_mismatch" } satisfies VerifyResponse);
  }

  if (!jwsData.environment || !ACCEPTED_ENVIRONMENTS.has(jwsData.environment)) {
    return json({ ok: false, reason: `environment_not_accepted: ${jwsData.environment}` } satisfies VerifyResponse);
  }

  if (jwsData.productId !== product_id) {
    return json({ ok: false, reason: "product_id_mismatch" } satisfies VerifyResponse);
  }

  const transactionId = jwsData.transactionId ?? "";
  if (!transactionId) {
    return json({ ok: false, reason: "jws_missing_transaction_id" } satisfies VerifyResponse);
  }

  // 감사 모드 — 구글의 obfuscatedExternalAccountId와 같은 목적(결제 시점 계정과 검증 시점 계정 대조)
  // 이지만, StoreKit 2의 SetAppAccountToken이 구매마다 값을 제대로 갱신하는지 아직 확인 전이라
  // (새 구매마다 갱신 안 되고 첫 거래 값을 계속 돌려주는 사례가 보고돼 있음) 거부하지 않고 로그만
  // 남긴다. 샌드박스 테스트로 신뢰성이 확인되면 구글과 동일하게 거부로 전환한다.
  if (jwsData.appAccountToken && jwsData.appAccountToken !== user.id) {
    console.warn(`[purchase-verify-apple] app_account_token_mismatch: token=${jwsData.appAccountToken}, caller=${user.id}, transaction=${transactionId}`);
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
      package_name: jwsData.bundleId,
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
