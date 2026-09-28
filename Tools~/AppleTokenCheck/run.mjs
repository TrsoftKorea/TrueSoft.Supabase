// apple-token Edge Function 을 가짜 애플 서버·가짜 Supabase 로 통째로 돌려 본다.
//
//   node --import ./Tools~/AppleTokenCheck/register.mjs Tools~/AppleTokenCheck/run.mjs
//
// register.mjs 가 "npm:@supabase/supabase-js" 를 supabase-stub.mjs(메모리 테이블)로 바꿔 끼운다.
// 가짜 애플 서버는 client_secret 의 서명·클레임까지 검증한다 — 애플에 실제로 보내 보지 않고 형식을 확인하는 방법이다.
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { db } from "./supabase-stub.mjs";

const fnUrl = pathToFileURL(path.join(path.dirname(fileURLToPath(import.meta.url)),
  "..", "..", "Samples~", "DatabaseSetup", "EdgeFunctions", "apple-token", "index.ts")).href;

const kp = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
const pkcs8 = Buffer.from(await crypto.subtle.exportKey("pkcs8", kp.privateKey)).toString("base64");
const pem = `-----BEGIN PRIVATE KEY-----\n${pkcs8.match(/.{1,64}/g).join("\n")}\n-----END PRIVATE KEY-----\n`;

const env = {
  SUPABASE_URL: "http://sb", SUPABASE_PUBLISHABLE_KEYS: '{"default":"pk"}', SUPABASE_SECRET_KEYS: '{"default":"sk"}',
  APPLE_BUNDLE_ID: "com.example.game, com.example.game2", APPLE_TEAM_ID: "TEAM123456", APPLE_KEY_ID: "KEY1234567", APPLE_PRIVATE_KEY: pem,
};
let handler;
globalThis.Deno = { env: { get: (k) => env[k] }, serve: (h) => { handler = h; } };

const b64u = (s) => Buffer.from(s).toString("base64url");
const fakeIdToken = (sub) => `${b64u('{"alg":"none"}')}.${b64u(JSON.stringify({ sub }))}.x`;
const codeOwner = { c1: "A1", cOther: "B2" };
const calls = [];
const secretProblems = [];

globalThis.fetch = async (url, init) => {
  const form = Object.fromEntries(new URLSearchParams(init.body.toString()));
  calls.push({ url, form });
  // client_secret 가 애플 형식인지 서명까지 검증한다.
  const [h, p, s] = form.client_secret.split(".");
  const header = JSON.parse(Buffer.from(h, "base64url"));
  const claims = JSON.parse(Buffer.from(p, "base64url"));
  const sigOk = await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, kp.publicKey, Buffer.from(s, "base64url"), new TextEncoder().encode(`${h}.${p}`));
  const now = Math.floor(Date.now() / 1000);
  if (!sigOk) secretProblems.push("signature");
  if (header.alg !== "ES256" || header.kid !== "KEY1234567") secretProblems.push("header " + JSON.stringify(header));
  if (claims.iss !== "TEAM123456" || claims.aud !== "https://appleid.apple.com" || claims.sub !== form.client_id) secretProblems.push("claims " + JSON.stringify(claims));
  if (!(claims.exp > now && claims.exp - claims.iat <= 15777000)) secretProblems.push("exp");

  if (url.endsWith("/auth/token")) {
    if (form.grant_type !== "authorization_code") return new Response('{"error":"unsupported_grant_type"}', { status: 400 });
    const sub = codeOwner[form.code];
    if (!sub) return new Response('{"error":"invalid_grant"}', { status: 400 });
    return new Response(JSON.stringify({ refresh_token: "rt-" + form.code, id_token: fakeIdToken(sub) }), { status: 200 });
  }
  if (url.endsWith("/auth/revoke")) return new Response("", { status: 200 });
  return new Response("?", { status: 404 });
};

await import(fnUrl);

const call = async (body, { auth = true } = {}) => {
  const res = await handler(new Request("http://fn", {
    method: "POST", headers: auth ? { Authorization: "Bearer t" } : {}, body: JSON.stringify(body),
  }));
  return { status: res.status, body: await res.json() };
};

let failed = 0;
const check = (name, cond, got) => { if (!cond) failed++; console.log(`${cond ? "OK  " : "FAIL"} ${name}${cond ? "" : "  → " + JSON.stringify(got)}`); };

globalThis.__user = { id: "acc-1", identities: [{ provider: "apple", id: "A1", identity_data: { sub: "A1" } }] };

let r = await call({ action: "store", authorization_code: "c1" }, { auth: false });
check("JWT 없으면 401", r.status === 401, r);

r = await call({ action: "store", authorization_code: "c1" });
check("보관 성공", r.status === 200 && r.body.stored === true, r);
check("보관된 토큰·client_id", db.apple_auth_tokens.get("acc-1")?.refresh_token === "rt-c1" && db.apple_auth_tokens.get("acc-1")?.client_id === "com.example.game", [...db.apple_auth_tokens]);
check("교환 요청 형식", calls.at(-1).form.grant_type === "authorization_code" && calls.at(-1).form.code === "c1" && !("redirect_uri" in calls.at(-1).form), calls.at(-1).form);

calls.length = 0;
r = await call({ action: "store", authorization_code: "cOther" });
check("남의 애플 계정 코드 → 403", r.status === 403 && r.body.reason === "apple_identity_mismatch", r);
check("그 토큰은 바로 철회", calls.some((c) => c.url.endsWith("/auth/revoke") && c.form.token === "rt-cOther"), calls);
check("기존 보관분은 그대로", db.apple_auth_tokens.get("acc-1")?.refresh_token === "rt-c1", [...db.apple_auth_tokens]);

r = await call({ action: "store", authorization_code: "c1", client_id: "com.attacker.app" });
check("허용 안 된 client_id → 400", r.status === 400 && r.body.reason === "client_id_not_allowed", r);

r = await call({ action: "store", authorization_code: "c1", client_id: "com.example.game2" });
check("두 번째 번들 ID 도 허용", r.status === 200, r);
await call({ action: "store", authorization_code: "c1" });

calls.length = 0;
r = await call({ action: "revoke" });
check("철회 성공", r.status === 200 && r.body.revoked === true, r);
const rv = calls.find((c) => c.url.endsWith("/auth/revoke"));
check("철회 요청 형식", rv && rv.form.token === "rt-c1" && rv.form.token_type_hint === "refresh_token" && rv.form.client_id === "com.example.game", rv);
check("철회 뒤 행 삭제", !db.apple_auth_tokens.has("acc-1"), [...db.apple_auth_tokens]);

r = await call({ action: "revoke" });
check("보관분 없으면 no_token", r.status === 200 && r.body.revoked === false && r.body.reason === "no_token", r);

r = await call({ action: "store" });
check("코드 없으면 400", r.status === 400, r);
r = await call({ action: "nope" });
check("모르는 action 400", r.status === 400, r);

check("client_secret 형식·서명 문제 없음", secretProblems.length === 0, secretProblems);

// 키에 줄바꿈이 글자 "\n" 으로 들어간 경우 — 걷어 내고 정상 동작해야 한다.
env.APPLE_PRIVATE_KEY = pem.replace(/\n/g, "\\n");
await import(fnUrl + "?literalnewline");
r = await call({ action: "store", authorization_code: "c1" });
check("키 줄바꿈이 글자 \\n 이어도 보관 성공", r.status === 200 && r.body.stored === true, r);

// 키가 깨졌을 때 — 사유 없는 500 이 아니라 원인을 돌려준다.
env.APPLE_PRIVATE_KEY = "-----BEGIN PRIVATE KEY-----\n@@not-base64@@\n-----END PRIVATE KEY-----";
await import(fnUrl + "?badkey");
r = await call({ action: "store", authorization_code: "c1" });
check("깨진 키 → 500 apple_token_exception", r.status === 500 && String(r.body.reason).startsWith("apple_token_exception"), r);
env.APPLE_PRIVATE_KEY = pem;

// 설정이 비었을 때 — 모듈을 다시 읽어 상수를 새로 잡는다.
env.APPLE_KEY_ID = "";
await import(fnUrl + "?noconfig");
r = await call({ action: "store", authorization_code: "c1" });
check("설정 없으면 500 server_config_error", r.status === 500 && r.body.reason === "server_config_error", r);

console.log(failed ? `\n${failed}건 실패` : "\n전부 통과");
process.exit(failed ? 1 : 0);
