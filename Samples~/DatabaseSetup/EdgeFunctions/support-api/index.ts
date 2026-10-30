import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";
import { createRemoteJWKSet, decodeJwt, jwtVerify } from "npm:jose@5";

// 고객센터 창(웹 페이지)이 부르는 함수. 게임 SDK 가 먼저 session.start 로 본인을 확인받아 12시간짜리 접속 표를
// 받고, 창은 그 표로 나머지 요청을 한다. DB 는 전부 service_role 로 install.sql 22절 함수를 부른다.
//
// 본인 확인 — 로그인 중이면 지금 접속 토큰. 차단된 계정은 로그인이 막혀 세션이 없으므로, Supabase 가 그 계정에
// 내준 증명으로 확인한다(셋 다 "차단 중"일 때만 받는다 — 차단이 아니면 정상 로그인으로 오면 된다):
//   ① 마지막 접속 토큰 — Supabase 가 비밀 키로 서명한 토큰. 기한이 지나도 서명은 공개 키로 확인된다.
//   ② 기기에 남은 갱신 토큰 — Supabase 인증 기록에 그 계정과 짝지어 있다.
//   ③ 차단으로 거절된 구글·애플 ID 토큰 — Supabase 인증 서버에 그대로 보내 "차단됨"을 받으면 그 계정을 알아본 것이다.
//
// 게임 밖(고객센터 주소를 브라우저로 연 것) — 창이 구글·애플 로그인으로 받은 ID 토큰을 web.login 으로 보내면 같은
// 접속 표를 준다. 게임에 연결된 구글·애플 계정만 받고(③과 같은 확인) 새 계정은 만들지 않는다. 게스트 계정은
// 구글·애플 증명이 없으므로 게임 안 고객센터만 쓴다. 창이 띄울 로그인 단추의 ID 는 look 이 서버 연결값
// (GOOGLE_WEB_CLIENT_ID·APPLE_SERVICES_ID)에서 준다.

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_PUBLISHABLE_KEY = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!).default;
const SUPABASE_SECRET_KEY = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default;

const BUCKET = "support-attachments";
const TOKEN_TTL_SECONDS = 12 * 60 * 60;
// 마지막 접속 토큰을 받는 기한. 오래된 기기에서 주운 토큰으로 문의를 열지 못하게.
const SAVED_TOKEN_MAX_AGE_SECONDS = 90 * 24 * 60 * 60;
const MAX_UPLOAD_BYTES = 5 * 1024 * 1024;
const UPLOADS_PER_HOUR = 30;
const MAX_FILES_PER_ACCOUNT = 100;
const IMAGE_EXT: Record<string, string> = { "image/png": "png", "image/jpeg": "jpg", "image/webp": "webp" };

function looksLike(type: string, b: Uint8Array): boolean {
  if (type === "image/png") return b.length > 8 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47;
  if (type === "image/jpeg") return b.length > 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff;
  if (type === "image/webp") {
    return b.length > 12 && String.fromCharCode(b[0]!, b[1]!, b[2]!, b[3]!) === "RIFF"
      && String.fromCharCode(b[8]!, b[9]!, b[10]!, b[11]!) === "WEBP";
  }
  return false;
}

const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
  auth: { autoRefreshToken: false, persistSession: false },
});
const jwks = createRemoteJWKSet(new URL(`${SUPABASE_URL}/auth/v1/.well-known/jwks.json`));

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

type Params = Record<string, unknown>;
type SupportToken = { sub: string; ban: boolean; lang: string; meta: Record<string, string>; exp: number };

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { ...CORS, "Content-Type": "application/json" } });
const fail = (reason: string, status = 400) => json({ ok: false, reason }, status);

class Reject extends Error {
  constructor(readonly reason: string, readonly status = 400) {
    super(reason);
  }
}

const str = (p: Params, k: string): string => (typeof p[k] === "string" ? (p[k] as string) : "");

// 형식이 틀린 번호를 DB 로 넘기면 서버 오류로 보이므로 여기서 "없는 문의"로 끊는다.
const ticketId = (p: Params): string => {
  const id = str(p, "ticket_id");
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) throw new Reject("support_ticket_not_found");
  return id;
};

function toBase64Url(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function fromBase64Url(s: string): Uint8Array {
  const b64 = s.replaceAll("-", "+").replaceAll("_", "/") + "===".slice((s.length + 3) % 4);
  return Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
}

let secretCache: string | null = null;
async function tokenSecret(): Promise<string> {
  if (secretCache) return secretCache;
  const { data, error } = await db.rpc("ts_support_token_secret");
  if (error || typeof data !== "string" || !data) throw new Error(`support secret: ${error?.message ?? "missing"}`);
  return (secretCache = data);
}

async function hmac(message: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw", new TextEncoder().encode(await tokenSecret()), { name: "HMAC", hash: "SHA-256" }, false, ["sign"],
  );
  return toBase64Url(new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message))));
}

async function signToken(t: SupportToken): Promise<string> {
  const body = toBase64Url(new TextEncoder().encode(JSON.stringify(t)));
  return `${body}.${await hmac(body)}`;
}

async function readToken(req: Request): Promise<SupportToken> {
  const raw = (req.headers.get("Authorization") ?? "").replace(/^Support\s+/i, "").trim();
  const [body, sig] = raw.split(".");
  if (!body || !sig || (await hmac(body)) !== sig) throw new Reject("support_session_invalid", 401);
  const t = JSON.parse(new TextDecoder().decode(fromBase64Url(body))) as SupportToken;
  if (!t.sub || t.exp * 1000 < Date.now()) throw new Reject("support_session_expired", 401);
  return t;
}

async function bannedUntil(accountId: string): Promise<Date | null> {
  const { data, error } = await db.auth.admin.getUserById(accountId);
  if (error || !data?.user) throw new Reject("support_account_not_found", 404);
  const raw = (data.user as unknown as { banned_until?: string | null }).banned_until;
  const until = raw ? new Date(raw) : null;
  return until && until > new Date() ? until : null;
}

const isBanned = async (accountId: string) => (await bannedUntil(accountId)) !== null;

// 차단 정보 — 구글·애플 로그인이 차단으로 거절되면 SDK 가 계정을 몰라 정지 기한·사유를 못 채운다. 본인 확인을
// 마친 이 함수가 대신 알려 준다(get-ban-info 와 같은 기준: 50년 넘게 남으면 영구 = null).
async function banDetails(accountId: string): Promise<{ account_id: string; banned_until: string | null; message: string | null }> {
  const until = await bannedUntil(accountId);
  const permanentAfter = new Date();
  permanentAfter.setFullYear(permanentAfter.getFullYear() + 50);
  const { data } = await db.from("user_ban_messages").select("ban_message").eq("account_id", accountId).maybeSingle();
  return {
    account_id: accountId,
    banned_until: until && until < permanentAfter ? until.toISOString() : null,
    message: (data as { ban_message?: string | null } | null)?.ban_message ?? null,
  };
}

// ① 마지막 접속 토큰. 기한(exp)은 일부러 넘긴다 — 서명과 발급 시각(iat)만 본다.
async function accountFromSavedAccessToken(token: string): Promise<string | null> {
  try {
    const claims = decodeJwt(token);
    if (typeof claims.iat !== "number" || claims.iat * 1000 < Date.now() - SAVED_TOKEN_MAX_AGE_SECONDS * 1000) return null;
    const { payload } = await jwtVerify(token, jwks, {
      issuer: `${SUPABASE_URL}/auth/v1`,
      audience: "authenticated",
      currentDate: new Date(claims.iat * 1000 + 1000),
    });
    return typeof payload.sub === "string" ? payload.sub : null;
  } catch {
    return null;
  }
}

// ② 기기에 남은 갱신 토큰.
async function accountFromRefreshToken(token: string): Promise<string | null> {
  const { data, error } = await db.rpc("ts_support_account_from_refresh_token", { p_token: token });
  if (error) throw new Error(error.message);
  return typeof data === "string" ? data : null;
}

// ③ 구글·애플 ID 토큰. 판정은 Supabase 인증 서버가 한다 — 서명·발급 대상(앱·웹 클라이언트 ID)·nonce 를 거기서 본다.
// 연결된 계정이 없으면 인증 서버에 보내지 않는다(보내면 새 계정이 만들어진다).
// 차단이 아니면 인증 서버가 세션을 내주는데, 그 세션은 쓰지 않고 바로 닫는다 — 계정이 맞는지만 확인한 것이다.
type IdentityCheck = { account: string; banned: boolean } | "not_linked" | "invalid";

async function checkIdToken(provider: string, idToken: string, nonce: string): Promise<IdentityCheck> {
  if (provider !== "google" && provider !== "apple") return "invalid";
  let sub: unknown;
  try {
    sub = decodeJwt(idToken).sub;
  } catch {
    return "invalid";
  }
  if (typeof sub !== "string" || !sub) return "invalid";
  const { data: linked, error } = await db.rpc("ts_support_account_from_identity", { p_provider: provider, p_sub: sub });
  if (error) throw new Error(error.message);
  if (typeof linked !== "string") return "not_linked";

  const res = await fetch(`${SUPABASE_URL}/auth/v1/token?grant_type=id_token`, {
    method: "POST",
    headers: { apikey: SUPABASE_PUBLISHABLE_KEY, "Content-Type": "application/json" },
    body: JSON.stringify(nonce ? { provider, id_token: idToken, nonce } : { provider, id_token: idToken }),
    signal: AbortSignal.timeout(10000),
  });
  const body = await res.json().catch(() => ({})) as Record<string, unknown>;
  if (res.ok) {
    const access = typeof body.access_token === "string" ? body.access_token : "";
    if (access) {
      await fetch(`${SUPABASE_URL}/auth/v1/logout?scope=local`, {
        method: "POST",
        headers: { apikey: SUPABASE_PUBLISHABLE_KEY, Authorization: `Bearer ${access}` },
      }).catch(() => {});
    }
    const user = body.user as { id?: unknown } | undefined;
    return user?.id === linked ? { account: linked, banned: false } : "invalid";
  }
  const code = String(body.error_code ?? body.code ?? "");
  const msg = String(body.msg ?? body.message ?? body.error_description ?? "");
  if (code === "user_banned" || /banned/i.test(msg)) return { account: linked, banned: true };
  // 흔한 원인 — 이 클라이언트 ID·Services ID 가 Supabase 구글·애플 로그인의 Client IDs 에 없음, nonce 불일치, 횟수 제한.
  console.warn(`[support-api] ${provider} id_token rejected: ${res.status} ${code} ${msg}`.slice(0, 300));
  return "invalid";
}

// 게임 안 창의 ③ — 로그인 없이 받는 증명은 차단된 계정만 쓰므로(startSession) 차단일 때만 계정을 돌려준다.
async function accountFromIdToken(provider: string, idToken: string, nonce: string): Promise<string | null> {
  const r = await checkIdToken(provider, idToken, nonce);
  return typeof r === "object" && r.banned ? r.account : null;
}

const META_KEYS = ["platform", "app_version", "os", "device", "language", "server_id", "sdk_version"];

function pickMeta(raw: unknown): Record<string, string> {
  const out: Record<string, string> = {};
  if (raw && typeof raw === "object") {
    for (const k of META_KEYS) {
      const v = (raw as Record<string, unknown>)[k];
      if (typeof v === "string" && v.trim()) out[k] = v.trim().slice(0, 80);
    }
  }
  return out;
}

// 언어 코드를 콘솔·우편과 같은 꼴로 — 중국어만 간체(zh-CN)·번체(zh-TW)를 가르고 나머지는 두 글자.
// 두 글자로만 자르면 번체 기기가 간체로 바뀐다.
function normLang(raw: string | null | undefined): string {
  const v = (raw ?? "").trim().toLowerCase();
  if (!v) return "en";
  if (v.startsWith("zh")) return !/hans/.test(v) && /tw|hk|mo|hant/.test(v) ? "zh-TW" : "zh-CN";
  const two = v.slice(0, 2);
  return SUPPORTED_LANGS.has(two) ? two : "en";
}
const SUPPORTED_LANGS = new Set(["en", "ja", "ko", "de", "fr", "es", "pt", "ru", "id", "th", "vi"]);

async function startSession(p: Params): Promise<Response> {
  const meta = pickMeta(p.meta);
  const lang = normLang(meta.language);

  let account: string | null = null;
  let banned = false;

  const access = str(p, "access_token");
  if (access) {
    const { data, error } = await db.auth.getUser(access);
    if (!error && data?.user) {
      account = data.user.id;
      banned = await isBanned(account);
    }
  }

  if (!account) {
    const candidates: Array<() => Promise<string | null>> = [];
    const saved = str(p, "saved_access_token");
    const refresh = str(p, "refresh_token");
    const idToken = str(p, "id_token");
    if (saved) candidates.push(() => accountFromSavedAccessToken(saved));
    if (refresh) candidates.push(() => accountFromRefreshToken(refresh));
    if (idToken) candidates.push(() => accountFromIdToken(str(p, "provider"), idToken, str(p, "nonce")));
    if (candidates.length === 0) throw new Reject("support_login_required", 401);

    for (const next of candidates) {
      // 증명 하나가 실패해도(지워진 계정의 옛 토큰 등) 다음 증명을 본다 — 앞에서 끊기면 구글·애플 증명이 시도도 안 된다.
      try {
        const found = await next();
        if (!found) continue;
        // 로그인 없이 받는 증명은 차단된 계정만 — 차단이 아니면 정상 로그인으로 오면 되고, 주운 토큰으로 남의
        // 문의를 읽는 길을 좁힌다.
        if (await isBanned(found)) {
          account = found;
          banned = true;
          break;
        }
      } catch (e) {
        console.warn(`[support-api] proof skipped: ${e instanceof Error ? e.message : String(e)}`);
      }
    }
    if (!account) throw new Reject("support_login_required", 401);
  }

  const exp = Math.floor(Date.now() / 1000) + TOKEN_TTL_SECONDS;
  const token = await signToken({ sub: account, ban: banned, lang, meta, exp });
  const ban = banned ? await banDetails(account) : null;
  return json({ ok: true, data: { token, expires_at: new Date(exp * 1000).toISOString(), banned, lang, ban } });
}

async function signAttachments<T extends { attachments?: unknown }>(items: T[]): Promise<T[]> {
  const paths = items.flatMap((m) => (Array.isArray(m.attachments) ? (m.attachments as string[]) : []));
  if (paths.length === 0) return items;
  const { data } = await db.storage.from(BUCKET).createSignedUrls(paths, 3600);
  const url = new Map((data ?? []).map((d) => [d.path, d.signedUrl]));
  return items.map((m) => ({
    ...m,
    attachments: (Array.isArray(m.attachments) ? (m.attachments as string[]) : [])
      .map((path) => ({ path, url: url.get(path) ?? null })),
  }));
}

async function upload(account: string, p: Params): Promise<Response> {
  const folder = account;
  const limits = { perHour: UPLOADS_PER_HOUR, perMonth: MAX_FILES_PER_ACCOUNT };
  const type = str(p, "content_type");
  const ext = IMAGE_EXT[type];
  if (!ext) throw new Reject("support_attachment_type");
  let bytes: Uint8Array;
  try {
    bytes = Uint8Array.from(atob(str(p, "data")), (c) => c.charCodeAt(0));
  } catch {
    throw new Reject("support_attachment_invalid");
  }
  if (bytes.length === 0) throw new Reject("support_attachment_invalid");
  if (bytes.length > MAX_UPLOAD_BYTES) throw new Reject("support_attachment_too_large");
  // 올리는 쪽이 밝힌 종류가 아니라 내용의 첫 바이트로 진짜 그림인지 본다.
  if (!looksLike(type, bytes)) throw new Reject("support_attachment_type");

  // 올리기만 하고 안 붙인 지 하루 지난 그림은 지운다 — 따로 도는 정리 작업 없이 그 계정이 다시 올릴 때 치운다.
  const { data: files } = await db.storage.from(BUCKET).list(folder, { limit: 1000, sortBy: { column: "created_at", order: "desc" } });
  const listed = (files ?? []).filter((o) => o.name && o.created_at);
  const attached = new Set(await rpc<string[]>("ts_support_account_attachments", { p_account: account }) ?? []);
  const dayAgo = Date.now() - 24 * 3600_000;
  const stale = listed.filter((o) => !attached.has(`${folder}/${o.name}`) && Date.parse(o.created_at!) < dayAgo);
  if (stale.length > 0) await db.storage.from(BUCKET).remove(stale.map((o) => `${folder}/${o.name}`));

  // 도배 막기 — 시간당 개수와 계정당 총량.
  const hourAgo = Date.now() - 3600_000;
  if (listed.filter((o) => Date.parse(o.created_at!) > hourAgo).length >= limits.perHour) {
    throw new Reject("support_too_fast");
  }
  // 총량은 최근 30일만 센다 — 붙인 그림은 지우지 않으므로 평생 개수로 세면 문의를 많이 한 계정이 영영 못 붙인다.
  const monthAgo = Date.now() - 30 * 24 * 3600_000;
  const staleNames = new Set(stale.map((o) => o.name));
  if (listed.filter((o) => !staleNames.has(o.name) && Date.parse(o.created_at!) > monthAgo).length >= limits.perMonth) {
    throw new Reject("support_attachment_limit");
  }

  const path = `${folder}/${crypto.randomUUID()}.${ext}`;
  const { error } = await db.storage.from(BUCKET).upload(path, bytes, { contentType: type, upsert: false });
  if (error) throw new Error(`upload: ${error.message}`);
  const { data: signed } = await db.storage.from(BUCKET).createSignedUrl(path, 3600);
  return json({ ok: true, data: { path, url: signed?.signedUrl ?? null } });
}

// DB 함수가 raise 한 사유(support_*·coupon_*)는 화면이 문구로 바꿔 보이게 그대로 돌려준다. 직접 raise 한 것(P0001)만
// 본다 — "함수가 없다" 같은 글 속 함수 이름을 사유로 뽑으면 배포 누락이 서버 로그에 안 남는다.
function dbReason(error: { message: string; code?: string }): string | null {
  if (error.code !== "P0001") return null;
  const m = /^(?:support|coupon)_[a-z_]+$/.exec(error.message.trim());
  return m ? m[0] : null;
}

// ── 횟수 제한 ──────────────────────────────────────────────────────────────
// 시간당. 접속 주소는 그대로 남기지 않고 서명 비밀값으로 해시한 값만 쓴다.
const LIMITS = {
  couponPerAccount: 10,
  // 게임 밖 로그인 — ID 토큰마다 인증 서버를 한 번 부르므로 접속 주소별로 막는다.
  loginPerIp: 20,
} as const;

function clientIp(req: Request): string {
  const fwd = req.headers.get("x-forwarded-for") ?? "";
  return (req.headers.get("cf-connecting-ip") ?? fwd.split(",")[0] ?? "").trim() || "unknown";
}

async function webHit(key: string, max: number): Promise<void> {
  const ok = await rpc<boolean>("ts_support_web_hit", { p_key: key, p_window_seconds: 3600, p_max: max });
  if (!ok) throw new Reject("support_too_fast", 429);
}

// 게임 밖 창의 로그인. 연결된 구글·애플 계정이면 게임 안 창과 같은 접속 표를 준다 — 그 뒤로는 내 문의·덧붙이기까지
// 게임 안 창과 똑같이 쓴다. 차단된 계정도 받는다(문의는 되고 쿠폰은 redeemCoupon 이 막는다).
async function webLogin(req: Request, p: Params): Promise<Response> {
  await webHit(`login:ip:${await hmac(clientIp(req))}`, LIMITS.loginPerIp);
  const idToken = str(p, "id_token");
  if (!idToken || idToken.length > 8192) throw new Reject("support_web_login_failed", 401);
  const r = await checkIdToken(str(p, "provider"), idToken, str(p, "nonce"));
  if (r === "not_linked") throw new Reject("support_web_not_linked", 404);
  if (r === "invalid") throw new Reject("support_web_login_failed", 401);

  const lang = normLang(str(p, "lang"));
  const exp = Math.floor(Date.now() / 1000) + TOKEN_TTL_SECONDS;
  const token = await signToken({ sub: r.account, ban: r.banned, lang, meta: webMeta(req, p), exp });
  return json({ ok: true, data: { token, expires_at: new Date(exp * 1000).toISOString(), banned: r.banned, lang } });
}

// 차단된 계정은 쿠폰 보상을 받지 않는다(게임에서도 로그인이 막혀 못 쓴다).
async function redeemCoupon(account: string, banned: boolean, p: Params): Promise<Response> {
  if (banned) throw new Reject("support_account_restricted");
  const code = str(p, "code").trim();
  if (!code || code.length > 64) throw new Reject("coupon_not_found");
  await rpc("ts_coupon_redeem_as", { p_uid: account, p_code: code });
  return json({ ok: true, data: null });
}

function webMeta(req: Request, p: Params): Record<string, string> {
  const ua = (req.headers.get("user-agent") ?? "").slice(0, 80);
  return pickMeta({ platform: "web", language: str(p, "lang"), device: ua });
}

async function rpc<T>(name: string, args: Params): Promise<T> {
  const { data, error } = await db.rpc(name, args);
  if (error) {
    const reason = dbReason(error);
    if (reason) throw new Reject(reason);
    throw new Error(`${name}: ${error.message}`);
  }
  return data as T;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });
  if (req.method !== "POST") return fail("method_not_allowed", 405);

  let p: Params;
  try {
    p = await req.json();
  } catch {
    return fail("invalid_json");
  }
  const action = str(p, "action");

  try {
    switch (action) {
      case "session.start":
        return await startSession(p);

      // 창 위쪽의 게임 이름·색·아이콘 — 운영 콘솔 "설정 › 프로젝트 정보" 값. 게임 밖 로그인 단추의 ID 는 "서비스 연결" 값이고,
      // 비어 있으면 그 단추를 보이지 않는다. 둘 다 웹 페이지에 그대로 드러나는 공개 값이다.
      case "look": {
        // 콘솔 "서비스 연결"에서 Google 로그인·Sign in with Apple 묶음을 꺼 두면 값이 있어도 그 단추를 감춘다
        // (켜고 끈 적이 없으면 값이 기준).
        const [s, services, google, apple] = await Promise.all([
          rpc<Record<string, string | null>>("ts_admin_console_settings_get", {}),
          rpc<Record<string, boolean>>("ts_admin_server_config_services", {}),
          serverConfig("GOOGLE_WEB_CLIENT_ID"),
          serverConfig("APPLE_SERVICES_ID"),
        ]);
        return json({
          ok: true,
          data: {
            name: s?.display_name ?? null, color: s?.color ?? null, icon: s?.icon ?? null,
            google_client_id: (services?.google !== false && google) || null,
            apple_services_id: (services?.signin !== false && apple) || null,
          },
        });
      }

      // 자주 묻는 질문 — 공개 정보라 접속 표 없이도 준다(본인 확인이 실패해도 창에서 볼 수 있게).
      case "faqs": {
        return json({ ok: true, data: await rpc("ts_support_faqs", { p_lang: normLang(str(p, "lang")) }) });
      }

      // 분류 이름도 공개 정보다 — 게임 밖에서 연 창의 문의하기·FAQ 묶음에 쓴다.
      case "categories.public":
        return json({ ok: true, data: await rpc("ts_support_categories", { p_lang: normLang(str(p, "lang")) }) });

      case "web.login":
        return await webLogin(req, p);
    }

    const t = await readToken(req);
    const lang = normLang(str(p, "lang") || t.lang);
    switch (action) {
      case "me":
        return json({ ok: true, data: { banned: t.ban, lang: t.lang } });

      case "categories":
        return json({ ok: true, data: await rpc("ts_support_categories", { p_lang: lang }) });

      case "tickets":
        return json({ ok: true, data: await rpc("ts_support_tickets", { p_account: t.sub, p_lang: lang }) });

      case "ticket": {
        const ticket = await rpc<{ messages: Array<{ attachments?: unknown }> }>(
          "ts_support_ticket", { p_account: t.sub, p_ticket: ticketId(p), p_lang: lang },
        );
        return json({ ok: true, data: { ...ticket, messages: await signAttachments(ticket.messages ?? []) } });
      }

      case "create": {
        const id = await rpc<string>("ts_support_create", {
          p_account: t.sub,
          p_category: Number(p.category_id),
          p_body: str(p, "body"),
          p_attachments: Array.isArray(p.attachments) ? p.attachments : [],
          p_meta: t.meta,
          p_was_banned: t.ban,
        });
        return json({ ok: true, data: { id } });
      }

      case "reply":
        await rpc("ts_support_reply", {
          p_account: t.sub,
          p_ticket: ticketId(p),
          p_body: str(p, "body"),
          p_attachments: Array.isArray(p.attachments) ? p.attachments : [],
        });
        return json({ ok: true, data: null });

      case "upload":
        return await upload(t.sub, p);

      // 서버 키로 부르므로 게임의 요청 제한(10절)을 안 탄다 — 번호 대입은 같은 횟수 제한으로 막는다.
      // 차단 여부는 접속 표에 적힌 값(최대 12시간 전)이 아니라 지금 상태로 본다.
      case "coupon":
        await webHit(`coupon:app:${t.sub}`, LIMITS.couponPerAccount);
        return await redeemCoupon(t.sub, await isBanned(t.sub), p);

      default:
        return fail("unknown_action");
    }
  } catch (e) {
    if (e instanceof Reject) return fail(e.reason, e.status);
    console.error(`[support-api] ${action}: ${e instanceof Error ? e.message : String(e)}`);
    return fail("server_error", 500);
  }
});
