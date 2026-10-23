// purchase-notify-apple
// App Store Server Notifications V2 수신 — 환불(REFUND)·환불 취소(REFUND_REVERSED)를 purchases 에 기록한다.
//
// App Store Connect > 앱 > 앱 정보 > App Store 서버 알림 에 이 함수 주소를 버전 2로 등록한다
// (운영·샌드박스 각각). 주소: https://<project-ref>.supabase.co/functions/v1/purchase-notify-apple
// 애플은 인증 헤더를 보내지 않으므로 verify_jwt 를 끄고 배포한다. 대신 알림 자체가 애플 서명이라
// purchase-verify-apple 과 같은 검증을 통과한 것만 믿는다.
//
// 필요한 값: APPLE_BUNDLE_ID (운영 콘솔 "설정" 또는 Edge Function Secrets, purchase-verify-apple 과 같은 값)
//
// 응답: 처리했거나 처리할 것이 없으면 200. DB 오류면 500 — 애플이 운영 환경에서는 1·12·24·48·72시간 뒤
// 다시 보낸다(샌드박스는 다시 보내지 않는다).

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

type NotificationPayload = {
  notificationType?: string;
  subtype?: string;
  notificationUUID?: string;
  signedDate?: number;
  data?: {
    bundleId?: string;
    environment?: string;
    signedTransactionInfo?: string;
  };
};

type TransactionPayload = {
  transactionId?: string;
  originalTransactionId?: string;
  productId?: string;
  bundleId?: string;
  signedDate?: number;
  revocationDate?: number;
  revocationReason?: number;   // 0 = 앱 문제로 환불, 1 = 그 밖의 사유
};

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const secretKeys          = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

const ACCEPTED_ENVIRONMENTS = new Set(["Production", "Sandbox"]);

const STORE = "apple_app_store";

Deno.serve(async (req) => {
  const reply = (status: number, body: Record<string, unknown>) =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

  if (req.method !== "POST") return reply(405, { ok: false, reason: "method_not_allowed" });

  let signedPayload: string | undefined;
  try {
    signedPayload = (await req.json())?.signedPayload;
  } catch {
    return reply(400, { ok: false, reason: "invalid_json" });
  }
  if (typeof signedPayload !== "string") return reply(400, { ok: false, reason: "missing_signed_payload" });

  let note: NotificationPayload;
  let tx: TransactionPayload;
  try {
    note = await verifyAppleJws<NotificationPayload>(signedPayload);
    const inner = note.data?.signedTransactionInfo;
    if (!inner) {
      // 환불과 무관한 알림(구독 상태 등)은 거래 정보가 없을 수 있다. 받았다고만 답한다.
      return reply(200, { ok: true, ignored: note.notificationType ?? "unknown" });
    }
    tx = await verifyAppleJws<TransactionPayload>(inner);
  } catch (e) {
    const code = e instanceof AppleJwsError ? e.message : String(e);
    console.warn(`[purchase-notify-apple] 서명 거절: ${code}`);
    return reply(400, { ok: false, reason: `jws_verify_failed: ${code}` });
  }

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  // 서명 검사를 먼저 하는 이유: 설정이 빠졌다는 기록은 진짜 애플 알림이 왔을 때만 남겨야
  // 아무나 보낸 요청이 운영 콘솔의 상태를 덮어쓰지 못한다.
  const ALLOWED_BUNDLE_IDS = await serverConfigList("APPLE_BUNDLE_ID");
  if (ALLOWED_BUNDLE_IDS.length === 0) {
    console.error("[purchase-notify-apple] APPLE_BUNDLE_ID 가 콘솔 설정에도 Secrets 에도 없습니다.");
    await db.rpc("ts_purchase_refund_sync_mark", {
      p_store: STORE, p_phase: "finish", p_ok: false, p_message: "APPLE_BUNDLE_ID 없음", p_marked: 0,
    });
    return reply(500, { ok: false, reason: "server_config_error" });
  }

  const bundleId = note.data?.bundleId ?? tx.bundleId;
  if (!bundleId || !ALLOWED_BUNDLE_IDS.includes(bundleId) || tx.bundleId !== bundleId) {
    console.warn(`[purchase-notify-apple] bundle_id_mismatch: note=${note.data?.bundleId}, tx=${tx.bundleId}`);
    return reply(200, { ok: true, ignored: "bundle_id_mismatch" });
  }
  if (!note.data?.environment || !ACCEPTED_ENVIRONMENTS.has(note.data.environment)) {
    return reply(200, { ok: true, ignored: `environment_${note.data?.environment}` });
  }

  const type = note.notificationType ?? "";
  if (type !== "REFUND" && type !== "REFUND_REVERSED") {
    return reply(200, { ok: true, ignored: type });
  }

  const transactionId = tx.transactionId ?? "";
  if (!transactionId) return reply(200, { ok: true, ignored: "missing_transaction_id" });

  // 받은 시각을 먼저 남긴다 — 이 뒤에서 실패해도 "알림이 오고 있다"는 것은 보이게.
  await db.rpc("ts_purchase_refund_sync_mark", { p_store: STORE, p_phase: "start" });

  const refundedAt = type === "REFUND"
    ? new Date(tx.revocationDate ?? note.signedDate ?? Date.now()).toISOString()
    : null;
  const reason = type === "REFUND" && typeof tx.revocationReason === "number" ? String(tx.revocationReason) : null;

  const { data: changed, error } = await db.rpc("ts_purchase_set_refund", {
    p_store: STORE,
    p_token: transactionId,
    p_refunded_at: refundedAt,
    p_reason: reason,
  });
  if (error) {
    console.error(`[purchase-notify-apple] 기록 실패: ${error.message}, transaction=${transactionId}`);
    await db.rpc("ts_purchase_refund_sync_mark", {
      p_store: STORE, p_phase: "finish", p_ok: false, p_message: error.message, p_marked: 0,
    });
    return reply(500, { ok: false, reason: "db_error" });
  }

  // 우리 기록에 없는 거래(다른 앱·검증 전 거래)는 changed=false 다. 실패가 아니다.
  const message = `${type} ${transactionId}${changed ? "" : " (기록에 없거나 이미 반영됨)"}`;
  await db.rpc("ts_purchase_refund_sync_mark", {
    p_store: STORE, p_phase: "finish", p_ok: true, p_message: message, p_marked: changed ? 1 : 0,
  });
  return reply(200, { ok: true, type, changed: !!changed });
});
