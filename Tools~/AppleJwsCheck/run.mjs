// purchase-verify-apple 의 영수증(JWS) 검증을 Node 로 시험한다.
//
//   node Tools~/AppleJwsCheck/run.mjs
//
// index.ts 에서 "BEGIN apple-jws" ~ "END apple-jws" 구간만 떼어 내 그대로 돌린다 — 배포되는 코드와
// 시험하는 코드가 갈라지지 않게. 가짜 인증서 체인은 실행할 때마다 openssl 로 새로 만든다(개인 키를
// 저장소에 두지 않으려는 것). openssl 이 PATH 에 있어야 한다(Git for Windows 에 들어 있다).
//
// 진짜 애플 인증서 두 장을 fixtures 에 둔다(공개 인증서다).
//   AppleWWDRCAG6.b64            — 중간. https://www.apple.com/certificateauthority/AppleWWDRCAG6.cer
//   AppleReceiptSigningLeaf.b64  — 리프 "Prod ECC Mac App Store and iTunes Store Receipt Signing"
//                                  (애플 공식 app-store-server-library-node 시험 코드의 REAL_APPLE_SIGNING_CERTIFICATE,
//                                   유효 2025-09-19 ~ 2027-10-13 — 만료되면 새 것으로 바꾼다)
// 진짜 세 장으로 만든 체인은 인증서 검사를 모두 통과하고 JWS 서명에서만 막혀야 정상이다(그 리프의 개인 키는 없다).
// 가짜 체인은 운영과 같은 짝(P-384 중간이 SHA-384 로 리프에 서명)으로 만든다 — Deno 의 WebCrypto 는 곡선과
// 해시 짝이 안 맞으면 지원하지 않을 수 있어, 다른 짝으로 시험하면 운영 경로를 증명하지 못한다.

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const indexTs = path.join(here, "..", "..", "Samples~", "DatabaseSetup", "EdgeFunctions", "purchase-verify-apple", "index.ts");

const src = fs.readFileSync(indexTs, "utf8");
const begin = src.indexOf("BEGIN apple-jws");
const end = src.indexOf("END apple-jws");
if (begin < 0 || end < 0) throw new Error("index.ts 에서 apple-jws 구간 표시를 찾지 못했습니다.");
const block = src.slice(src.indexOf("\n", begin) + 1, src.lastIndexOf("\n", end));

const work = fs.mkdtempSync(path.join(os.tmpdir(), "apple-jws-check-"));
fs.writeFileSync(path.join(work, "apple-jws.mts"), block);

// ── 가짜 체인: 루트(P-384) → 중간(P-384, 애플 중간 표식) → 리프(P-256, 애플 리프 표식) ──────────
const ossl = (...args) => execFileSync("openssl", args, { cwd: work, stdio: ["ignore", "pipe", "pipe"] });
fs.writeFileSync(path.join(work, "ext.cnf"), [
  "[inter]",
  "basicConstraints = critical,CA:TRUE,pathlen:0",
  "keyUsage = critical,keyCertSign,cRLSign",
  "1.2.840.113635.100.6.2.1 = ASN1:NULL",
  "[leaf]",
  "basicConstraints = critical,CA:FALSE",
  "keyUsage = critical,digitalSignature",
  "1.2.840.113635.100.6.11.1 = ASN1:NULL",
  "[leafnooid]",
  "basicConstraints = critical,CA:FALSE",
  "keyUsage = critical,digitalSignature",
  "",
].join("\n"));

ossl("ecparam", "-name", "secp384r1", "-genkey", "-noout", "-out", "root.key");
ossl("req", "-new", "-x509", "-key", "root.key", "-subj", "/CN=Fake Root", "-days", "3650", "-sha384",
  "-addext", "basicConstraints=critical,CA:TRUE", "-out", "root.pem");
ossl("ecparam", "-name", "secp384r1", "-genkey", "-noout", "-out", "inter.key");
ossl("req", "-new", "-key", "inter.key", "-subj", "/CN=Fake WWDR", "-out", "inter.csr");
ossl("x509", "-req", "-in", "inter.csr", "-CA", "root.pem", "-CAkey", "root.key", "-CAcreateserial",
  "-days", "3000", "-sha384", "-extfile", "ext.cnf", "-extensions", "inter", "-out", "inter.pem");
ossl("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", "leaf.key");
ossl("pkcs8", "-topk8", "-nocrypt", "-in", "leaf.key", "-out", "leaf.p8");
ossl("req", "-new", "-key", "leaf.key", "-subj", "/CN=Fake StoreKit Leaf", "-out", "leaf.csr");
for (const ext of ["leaf", "leafnooid"]) {
  ossl("x509", "-req", "-in", "leaf.csr", "-CA", "inter.pem", "-CAkey", "inter.key", "-CAcreateserial",
    "-days", "2000", "-sha384", "-extfile", "ext.cnf", "-extensions", ext, "-out", `${ext}.pem`);
}
const derB64 = (pem) => ossl("x509", "-in", pem, "-outform", "DER").toString("base64");

const C = {
  root: derB64("root.pem"), inter: derB64("inter.pem"), leaf: derB64("leaf.pem"), leafnooid: derB64("leafnooid.pem"),
  appleInter: fs.readFileSync(path.join(here, "fixtures", "AppleWWDRCAG6.b64"), "utf8").trim(),
  appleLeaf: fs.readFileSync(path.join(here, "fixtures", "AppleReceiptSigningLeaf.b64"), "utf8").trim(),
};

const { verifyAppleJws } = await import(pathToFileURL(path.join(work, "apple-jws.mts")).href);

// 루트 고정값은 구간 안에 있다 — "진짜 애플 루트"는 그 값을 그대로 꺼내 쓴다.
C.appleRoot = /APPLE_ROOT_CA_G3_B64 =\s*"([^"]+)"/.exec(block)[1];

const leafPem = fs.readFileSync(path.join(work, "leaf.p8"), "utf8");
const leafKey = await crypto.subtle.importKey(
  "pkcs8", Buffer.from(leafPem.replace(/-----[^-]+-----/g, "").replace(/\s+/g, ""), "base64"),
  { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);

const b64u = (buf) => Buffer.from(buf).toString("base64url");
async function makeJws(x5c, payload, { alg = "ES256" } = {}) {
  const h = b64u(JSON.stringify({ alg, x5c }));
  const p = b64u(JSON.stringify(payload));
  const sig = await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, leafKey, new TextEncoder().encode(`${h}.${p}`));
  return `${h}.${p}.${b64u(sig)}`;
}

const good = { productId: "gem_100", transactionId: "2000000123", bundleId: "com.example.game", environment: "Production", signedDate: Date.now() };
const fakeChain = [C.leaf, C.inter, C.root];
const fakeRoot = { rootCertificateB64: C.root };

let failed = 0;
async function expect(name, jwsPromise, want, opts) {
  let got;
  try { got = "통과:" + (await verifyAppleJws(await jwsPromise, opts)).transactionId; }
  catch (e) { got = e.message; }
  const ok = got === want;
  if (!ok) failed++;
  console.log(`${ok ? "OK  " : "FAIL"} ${name} → ${got}${ok ? "" : `  (기대: ${want})`}`);
}

await expect("정상 체인(가짜 루트를 시험용으로 고정)", makeJws(fakeChain, good), "통과:2000000123", fakeRoot);
await expect("같은 체인을 운영 기본값(애플 루트 고정)으로", makeJws(fakeChain, good), "cert_root_not_apple");
await expect("자기서명 인증서 한 장만", makeJws([C.leaf], good), "jws_bad_x5c");
await expect("자기 인증서 세 장으로 채움", makeJws([C.leaf, C.leaf, C.leaf], good), "cert_root_not_apple");
await expect("진짜 애플 루트·중간 + 가짜 리프", makeJws([C.leaf, C.appleInter, C.appleRoot], good), "cert_leaf_signature_invalid");
await expect("진짜 애플 루트 + 가짜 중간", makeJws([C.leaf, C.inter, C.appleRoot], good), "cert_intermediate_signature_invalid");
await expect("리프에 애플 표식 없음", makeJws([C.leafnooid, C.inter, C.root], good), "cert_leaf_not_apple", fakeRoot);
await expect("중간에 애플 표식 없음", makeJws([C.leaf, C.leaf, C.root], good), "cert_intermediate_not_apple", fakeRoot);
await expect("alg 가 ES256 이 아님", makeJws(fakeChain, good, { alg: "none" }), "jws_unexpected_alg", fakeRoot);
await expect("서명 뒤 페이로드 변조", (async () => {
  const [h, , s] = (await makeJws(fakeChain, good)).split(".");
  return `${h}.${b64u(JSON.stringify({ ...good, productId: "gem_9999" }))}.${s}`;
})(), "jws_signature_invalid", fakeRoot);
await expect("서명 시각이 인증서 유효기간 밖", makeJws(fakeChain, { ...good, signedDate: Date.UTC(2000, 0, 1) }), "cert_not_valid_at_signed_date", fakeRoot);
await expect("점 구분이 셋이 아님", Promise.resolve("abc.def"), "jws_malformed");

// 진짜 애플 체인 — 인증서 검사를 다 통과하고 서명에서만 막혀야 한다.
await expect("진짜 애플 루트·중간·리프 + 서명만 가짜", makeJws([C.appleLeaf, C.appleInter, C.appleRoot], good), "jws_signature_invalid");

// 악의적인 DER — 예전 파서는 음수 길이로 제자리를 돌다 메모리를 다 먹고 죽었다(리뷰에서 재현).
const rawB64 = (bytes) => Buffer.from(bytes).toString("base64");
await expect("길이 바이트 4개(음수가 되던 값)", makeJws([0, 1, 2].map(() => rawB64([0x30, 0x06, 0x02, 0x84, 0xff, 0xff, 0xff, 0xfa])), good), "der_bad_length");
await expect("부모 경계를 넘는 자식 길이", makeJws([0, 1, 2].map(() => rawB64([0x30, 0x05, 0x02, 0x83, 0xff, 0xff, 0xff])), good), "der_truncated");
await expect("헤더도 못 채우는 자투리", makeJws([0, 1, 2].map(() => rawB64([0x30, 0x01, 0x02])), good), "der_truncated");

fs.rmSync(work, { recursive: true, force: true });
console.log(failed ? `\n${failed}건 실패` : "\n전부 통과");
process.exit(failed ? 1 : 0);
