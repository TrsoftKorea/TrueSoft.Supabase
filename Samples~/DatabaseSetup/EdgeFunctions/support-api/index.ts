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

// ③ 구글·애플 ID 토큰. 판정은 Supabase 인증 서버가 한다 — 받은 답이 "차단됨"이어야만 그 계정으로 본다.
// 연결된 계정이 없으면 인증 서버에 보내지 않는다(보내면 새 계정이 만들어진다).
async function accountFromIdToken(provider: string, idToken: string, nonce: string): Promise<string | null> {
  if (provider !== "google" && provider !== "apple") return null;
  let sub: unknown;
  try {
    sub = decodeJwt(idToken).sub;
  } catch {
    return null;
  }
  if (typeof sub !== "string" || !sub) return null;
  const { data: linked, error } = await db.rpc("ts_support_account_from_identity", { p_provider: provider, p_sub: sub });
  if (error) throw new Error(error.message);
  if (typeof linked !== "string") return null;

  const res = await fetch(`${SUPABASE_URL}/auth/v1/token?grant_type=id_token`, {
    method: "POST",
    headers: { apikey: SUPABASE_PUBLISHABLE_KEY, "Content-Type": "application/json" },
    body: JSON.stringify(nonce ? { provider, id_token: idToken, nonce } : { provider, id_token: idToken }),
    signal: AbortSignal.timeout(10000),
  });
  const body = await res.json().catch(() => ({})) as Record<string, unknown>;
  if (res.ok) {
    // 차단이 아니었다 — 방금 생긴 세션은 쓰지 않으니 닫는다. 이 경로는 차단된 계정만 받는다.
    const access = typeof body.access_token === "string" ? body.access_token : "";
    if (access) {
      await fetch(`${SUPABASE_URL}/auth/v1/logout?scope=local`, {
        method: "POST",
        headers: { apikey: SUPABASE_PUBLISHABLE_KEY, Authorization: `Bearer ${access}` },
      }).catch(() => {});
    }
    return null;
  }
  const code = String(body.error_code ?? body.code ?? "");
  const msg = String(body.msg ?? body.message ?? body.error_description ?? "");
  return code === "user_banned" || /banned/i.test(msg) ? linked : null;
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

async function startSession(p: Params): Promise<Response> {
  const meta = pickMeta(p.meta);
  const lang = (meta.language ?? "").slice(0, 2).toLowerCase() || "en";

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

async function upload(t: SupportToken, p: Params): Promise<Response> {
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
  const { data: files } = await db.storage.from(BUCKET).list(t.sub, { limit: 1000, sortBy: { column: "created_at", order: "desc" } });
  const listed = (files ?? []).filter((o) => o.name && o.created_at);
  const attached = new Set(await rpc<string[]>("ts_support_account_attachments", { p_account: t.sub }) ?? []);
  const dayAgo = Date.now() - 24 * 3600_000;
  const stale = listed.filter((o) => !attached.has(`${t.sub}/${o.name}`) && Date.parse(o.created_at!) < dayAgo);
  if (stale.length > 0) await db.storage.from(BUCKET).remove(stale.map((o) => `${t.sub}/${o.name}`));

  // 도배 막기 — 시간당 개수와 계정당 총량.
  const hourAgo = Date.now() - 3600_000;
  if (listed.filter((o) => Date.parse(o.created_at!) > hourAgo).length >= UPLOADS_PER_HOUR) {
    throw new Reject("support_too_fast");
  }
  // 총량은 최근 30일만 센다 — 붙인 그림은 지우지 않으므로 평생 개수로 세면 문의를 많이 한 계정이 영영 못 붙인다.
  const monthAgo = Date.now() - 30 * 24 * 3600_000;
  const staleNames = new Set(stale.map((o) => o.name));
  if (listed.filter((o) => !staleNames.has(o.name) && Date.parse(o.created_at!) > monthAgo).length >= MAX_FILES_PER_ACCOUNT) {
    throw new Reject("support_attachment_limit");
  }

  const path = `${t.sub}/${crypto.randomUUID()}.${ext}`;
  const { error } = await db.storage.from(BUCKET).upload(path, bytes, { contentType: type, upsert: false });
  if (error) throw new Error(`upload: ${error.message}`);
  const { data: signed } = await db.storage.from(BUCKET).createSignedUrl(path, 3600);
  return json({ ok: true, data: { path, url: signed?.signedUrl ?? null } });
}

// DB 함수가 raise 한 사유(support_*)는 화면이 문구로 바꿔 보이게 그대로 돌려준다.
function dbReason(message: string): string | null {
  const m = /support_[a-z_]+/.exec(message);
  return m ? m[0] : null;
}

async function rpc<T>(name: string, args: Params): Promise<T> {
  const { data, error } = await db.rpc(name, args);
  if (error) {
    const reason = dbReason(error.message);
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

      // 창 위쪽의 게임 이름·색·아이콘 — 운영 콘솔 "설정 › 콘솔 표시" 값.
      case "look": {
        const s = (await rpc<Record<string, string | null>>("ts_admin_console_settings_get", {})) ?? {};
        return json({ ok: true, data: { name: s.display_name ?? null, color: s.color ?? null, icon: s.icon ?? null } });
      }

      // 자주 묻는 질문 — 공개 정보라 접속 표 없이도 준다(본인 확인이 실패해도 창에서 볼 수 있게).
      case "faqs": {
        const lang = (str(p, "lang") || "en").slice(0, 2).toLowerCase();
        return json({ ok: true, data: await rpc("ts_support_faqs", { p_lang: lang }) });
      }
    }

    const t = await readToken(req);
    const lang = str(p, "lang") || t.lang;
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
        return await upload(t, p);

      default:
        return fail("unknown_action");
    }
  } catch (e) {
    if (e instanceof Reject) return fail(e.reason, e.status);
    console.error(`[support-api] ${action}: ${e instanceof Error ? e.message : String(e)}`);
    return fail("server_error", 500);
  }
});
