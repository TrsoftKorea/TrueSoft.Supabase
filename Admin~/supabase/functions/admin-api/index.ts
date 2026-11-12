// 운영 도구 백엔드.
//
// 운영자 인증은 게임 Auth 를 쓰지 않는다. 구글 ID 토큰이나, 로그인 때 발급한 자체 세션 토큰을
// 여기서 직접 검증한다. 그래서 운영자는 auth.users 에 존재하지 않는다 —
// 플레이어와 계정 풀이 겹치지 않고, 게임의 탈퇴·차단 흐름이 운영자 접근에 영향을 주지 않는다.
//
// 로그인 방법은 둘이다.
//   구글 로그인    — 구글 계정이 있는 운영자용. 매번 구글이 신원을 증명한다.
//   비밀번호 로그인 — 구글 계정이 없는 운영자용. 비밀번호는 프로젝트가 아니라 트루베이스 초대 저장소(/hub)
//                   한 곳에 있고 본인이 정한다(마스터는 "트루베이스 설정"에서 초기화만). 콘솔이 저장소에서 받은 표를
//                   auth.hubLogin 으로 보내면 이 프로젝트의 세션 토큰으로 바꿔 준다.
//
// 권한은 두 단계다.
//   마스터  — ADMIN_EMAILS 환경변수. 화면에서 지울 수 없어 잠기지 않는 비상구. 운영자 비활성화·삭제와
//             Supabase 관리 열쇠는 마스터만.
//   운영자  — ts_admin_operators 표. 서로 권한이 같다(처음 만든 사람도 따로 없다 — 퇴사자가 생겨도 남은 사람이 관리한다).
//             트루베이스에 초대된 사람(콘솔 주소의 초대 저장소 /hub)만 운영자로 더할 수 있다.
//
// 필요한 시크릿:
//   GOOGLE_CLIENT_ID, ADMIN_EMAILS(쉼표 구분) — 기존과 동일.
//   ADMIN_SESSION_SECRET — 세션 토큰 서명용(선택). 없으면 설치 때 Vault 에 만든 값을 쓴다.
//   SUPABASE_* 는 플랫폼이 자동 주입한다.
//
// verify_jwt 는 false 로 배포한다. 게이트웨이의 JWT 검증은 게임 Auth 기준이라 구글 토큰을
// 통과시키지 못하고, CORS 프리플라이트(Authorization 없음)까지 막는다. 검증은 아래가 직접 한다.
import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";
import { createRemoteJWKSet, importPKCS8, jwtVerify, SignJWT } from "npm:jose@5";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

const GOOGLE_CLIENT_ID = Deno.env.get("GOOGLE_CLIENT_ID") ?? "";
const GOOGLE_JWKS = createRemoteJWKSet(new URL("https://www.googleapis.com/oauth2/v3/certs"));

// 세션 토큰 서명 키. 구글 토큰과 형태(JWT)를 맞춰서 프런트의
// 만료 판단 로직을 그대로 재사용한다 — 다만 서명 알고리즘이 달라(HS256 vs RS256)
// verifyIdentity() 가 시도 순서로 둘을 구분한다.
// Secrets 에 넣어 둔 값이 먼저다(그 값으로 발급한 표가 계속 통하도록). 없으면 설치 때 Vault 에 만든 값을 쓴다
// (install.sql 16절 admin_session_secret) — 새 프로젝트는 Secrets 에 따로 넣지 않는다.
const SESSION_SECRET = Deno.env.get("ADMIN_SESSION_SECRET") ?? "";
let sessionKeyCache: Uint8Array | null = SESSION_SECRET ? new TextEncoder().encode(SESSION_SECRET) : null;

async function sessionKey(db: SupabaseClient): Promise<Uint8Array | null> {
  if (sessionKeyCache) return sessionKeyCache;
  const { data, error } = await db.rpc("ts_admin_session_secret");
  if (error || typeof data !== "string" || data === "") return null;
  sessionKeyCache = new TextEncoder().encode(data);
  return sessionKeyCache;
}
// 구글 로그인도 이 토큰으로 바꿔 준다(auth.googleLogin) — 구글 ID 토큰은 1시간이면 끝나
// 그대로 쓰면 운영자가 한 시간마다 다시 로그인해야 했다.
const SESSION_TTL = "7d";

// 비어 있으면 마스터가 없는 상태 — 표에 등록된 운영자만 들어온다.
const MASTER_EMAILS = new Set(
  (Deno.env.get("ADMIN_EMAILS") ?? "")
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean),
);

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });

const fail = (error: string, status: number) => json({ ok: false, error }, status);

type Params = Record<string, unknown>;
const str = (p: Params, k: string): string => {
  const v = p[k];
  if (typeof v !== "string" || v.trim() === "") throw new Error(`${k} 값이 필요합니다.`);
  return v;
};
const optStr = (p: Params, k: string): string => {
  const v = p[k];
  return typeof v === "string" ? v : "";
};
const bool = (p: Params, k: string): boolean => p[k] === true;

// 제한값 입력은 화면·엣지 함수·DB 세 곳에서 같은 범위를 봐야 한다. DB 만 막으면 운영자가
// 영문 제약조건 오류를 보고, 상한이 없으면 오타 하나가 기능을 통째로 멈춘다.
const intInRange = (p: Params, k: string, label: string, min: number, max: number): number => {
  const v = p[k];
  const n = typeof v === "number" ? v : Number(v);
  if (!Number.isInteger(n) || n < min || n > max) {
    throw new Error(`${label}은 ${min}에서 ${max} 사이의 정수여야 합니다.`);
  }
  return n;
};

// `,()` 는 PostgREST or() 필터 문법을 깨서 제거하고, `%`·`_`·`\` 는 ILIKE 와일드카드라
// 리터럴로 취급되도록 백슬래시로 이스케이프한다 — 안 하면 "order_123" 검색에 "orderX123"까지 걸린다.
const sanitizeSearchTerm = (s: string): string => s.replace(/[,()]/g, "").replace(/[\\%_]/g, (c) => `\\${c}`);

// 화면의 날짜('YYYY-MM-DD')는 게임 시간대(설정 › 시간대)의 날짜다. 시간대 없이 비교하면 DB(UTC) 자정으로 읽혀
// 그 나라 새벽 기록이 전날로 빠진다 — 경계를 그 시간대의 자정 시각으로 만들어 비교한다.
// deno-lint-ignore no-explicit-any
const gameTimeZoneOf = async (db: any): Promise<string> => {
  const { data, error } = await db.rpc("ts_admin_game_settings_get");
  if (error) throw new Error(error.message);
  return (data?.time_zone as string) || "Asia/Seoul";
};

/** tz 의 그 날짜 자정이 가리키는 instant. 서머타임 경계도 맞추려고 시차를 두 번 잰다. */
const zonedMidnight = (tz: string, y: number, m: number, d: number): string => {
  const target = Date.UTC(y, m - 1, d);
  let t = target;
  for (let i = 0; i < 2; i++) {
    const p = Object.fromEntries(
      new Intl.DateTimeFormat("en-US", {
        timeZone: tz, hour12: false, year: "numeric", month: "2-digit", day: "2-digit",
        hour: "2-digit", minute: "2-digit", second: "2-digit",
      }).formatToParts(new Date(t)).map((x) => [x.type, x.value]),
    );
    const wall = Date.UTC(Number(p.year), Number(p.month) - 1, Number(p.day), Number(p.hour) % 24, Number(p.minute), Number(p.second));
    t += target - wall;
  }
  return new Date(t).toISOString();
};

const dayStartIn = (tz: string, dateStr: string): string | null => {
  const [y, m, d] = dateStr.split("-").map(Number);
  if (!y || !m || !d) return null;
  return zonedMidnight(tz, y, m, d);
};

// endDate 는 그날 하루 전체를 포함해야 한다 — 다음날 자정 미만(<)으로 바꾼다.
const nextDayExclusiveIn = (tz: string, dateStr: string): string | null => {
  const [y, m, d] = dateStr.split("-").map(Number);
  if (!y || !m || !d) return null;
  const next = new Date(Date.UTC(y, m - 1, d + 1));
  return zonedMidnight(tz, next.getUTCFullYear(), next.getUTCMonth() + 1, next.getUTCDate());
};

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 귓속말은 scope_key 가 "계정A:계정B"(정렬된 쌍)이고 받는 사람이 행에 따로 없다. 보낸 사람을
// 빼면 남는 쪽이 받는 사람이다 — 이게 없으면 운영자가 신고받은 귓속말의 상대를 알 수 없다.
const directPartnerId = (scopeKey: unknown, senderId: unknown): string | null => {
  if (typeof scopeKey !== "string") return null;
  // 조각이 정확히 둘일 때만 본다 — group 채널의 scope_key 는 게임이 정하는 값이라
  // "uuidA:uuidB:태그" 같은 형태가 올 수 있고, 앞 둘만 보면 귓속말로 오인한다.
  const parts = scopeKey.split(":");
  if (parts.length !== 2) return null;
  const [a, b] = parts;
  if (!UUID_RE.test(a) || !UUID_RE.test(b)) return null;
  if (senderId === a) return b;
  if (senderId === b) return a;
  return null;
};

/** 구글 ID 토큰을 검증하고 확인된 이메일을 돌려준다. 실패하면 null. */
async function verifyGoogleIdToken(idToken: string): Promise<string | null> {
  if (!GOOGLE_CLIENT_ID) {
    console.error("[admin-api] GOOGLE_CLIENT_ID 가 설정되지 않았습니다.");
    return null;
  }
  try {
    const { payload } = await jwtVerify(idToken, GOOGLE_JWKS, {
      issuer: ["https://accounts.google.com", "accounts.google.com"],
      audience: GOOGLE_CLIENT_ID,
    });
    // 미인증 이메일은 소유가 증명되지 않았으므로 허용목록과 대조할 근거가 못 된다.
    if (payload["email_verified"] !== true) return null;
    const email = payload["email"];
    return typeof email === "string" ? email.toLowerCase() : null;
  } catch {
    // 실패 로그는 여기서 안 남긴다 — 세션 토큰(HS256)도 항상 이 경로를
    // 먼저 타서 매번 "실패"하므로, 여기서 찍으면 정상적인 세션 요청마다 가짜 에러가
    // 쌓인다. 진짜 실패(둘 다 아님)는 verifyIdentity 에서 한 번만 남긴다.
    return null;
  }
}

const OPERATOR_LOOKUP_FAILED = "운영자 목록을 읽지 못했습니다. 잠시 뒤 다시 시도해 주세요.";

/**
 * 표에 등록되고 비활성화되지 않은 운영자인지. 목록을 못 읽으면 던진다 — false 로 돌려주면
 * 서버 고장이 콘솔에 "권한 없음"으로 보여 운영자가 엉뚱한 곳을 찾는다.
 */
async function isEnabledOperator(db: SupabaseClient, email: string): Promise<boolean> {
  const { data, error } = await db.rpc("ts_admin_list_operators");
  if (error) {
    console.error(`[admin-api] 운영자 목록 조회 실패: ${error.message}`);
    throw new Error(OPERATOR_LOOKUP_FAILED);
  }
  const rows = (data ?? []) as Array<{ email?: unknown; disabled_at?: unknown }>;
  return rows.some((r) => r.email === email && r.disabled_at == null);
}

/** 행마다 account_id 의 닉네임을 display_name 으로 붙인다. 닉네임이 없는 계정은 null. */
async function withDisplayNames<T extends { account_id?: unknown }>(
  db: SupabaseClient,
  rows: T[],
): Promise<Array<T & { display_name: string | null }>> {
  const ids = [...new Set(rows.map((r) => r.account_id).filter((v): v is string => typeof v === "string"))];
  const names = new Map<string, string>();
  // 계정 ID 를 주소에 실어 보내므로 한 번에 묻는 수를 묶어 나눈다(내려받기는 수천 명일 수 있다).
  for (let i = 0; i < ids.length; i += 200) {
    const { data, error } = await db.from("display_names").select("account_id, display_name").in("account_id", ids.slice(i, i + 200));
    if (error) throw new Error(error.message);
    for (const n of data ?? []) names.set(n.account_id as string, (n.display_name as string) ?? "");
  }
  return rows.map((r) => ({ ...r, display_name: names.get(r.account_id as string) || null }));
}

const NAME_SEARCH_LIMIT = 100;

/** 계정 ID 앞부분(16진수 4자 이상)이면 그 범위. uuid 칸은 글자 검색이 안 되어 범위로 찾는다. */
function accountIdRange(term: string): { lo: string; hi: string } | null {
  if (!/^[0-9a-f-]+$/i.test(term)) return null;
  const hex = term.replace(/-/g, "").toLowerCase();
  if (hex.length < 4 || hex.length > 32) return null;
  const uuid = (h: string) => `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
  return { lo: uuid(hex.padEnd(32, "0")), hi: uuid(hex.padEnd(32, "f")) };
}

/** 검색어로 계정을 거르는 조건들(or 에 넣는 꼴) — 계정 ID 앞부분 또는 닉네임 일부. 닉네임 칸이 없는 표(구매·데이터 로그)가
 *  같은 기준으로 찾게 한다. 화면이 닉네임 없는 플레이어를 ID 앞 8자로 보이므로 앞부분도 찾아야 한다.
 *  닉네임은 계정 ID 를 주소에 실어 거르므로 앞 100명까지만 쓰고, 넘으면 truncated 로 화면에 알린다. term 은 sanitizeSearchTerm 을 거친 값. */
async function accountFilterForSearch(db: SupabaseClient, term: string): Promise<{ anyOf: string[]; truncated: boolean }> {
  const anyOf: string[] = [];
  const range = accountIdRange(term);
  if (range) anyOf.push(`and(account_id.gte.${range.lo},account_id.lte.${range.hi})`);
  if (UUID_RE.test(term)) return { anyOf, truncated: false };
  const { data, error } = await db.from("display_names").select("account_id").ilike("display_name", `%${term}%`).order("account_id").limit(NAME_SEARCH_LIMIT + 1);
  if (error) throw new Error(error.message);
  const ids = (data ?? []).map((n) => n.account_id as string);
  if (ids.length > 0) anyOf.push(`account_id.in.(${ids.slice(0, NAME_SEARCH_LIMIT).join(",")})`);
  return { anyOf, truncated: ids.length > NAME_SEARCH_LIMIT };
}

/** 마스터이거나, 표에 등록되고 비활성화되지 않은 운영자인지. 로그인 때 쓴다. */
async function isAllowedIdentity(db: SupabaseClient, email: string): Promise<boolean> {
  return MASTER_EMAILS.has(email) || (await isEnabledOperator(db, email));
}

/**
 * 로그인 세션 토큰을 발급한다. 구글 ID 토큰과 같은 JWT 형태라 프런트가 만료를 그대로 읽는다.
 * 트루베이스 저장소의 표(비밀번호 로그인)로 받은 로그인은 그 표를 함께 싣는다 — 저장소에서 비밀번호를 바꾸면
 * 그 전에 받은 표가 무효가 되고, 요청마다 그것을 확인해 이 로그인도 함께 끊는다(stillValidHubToken).
 */
async function issueSessionToken(db: SupabaseClient, email: string, hubToken?: string): Promise<string> {
  const key = await sessionKey(db);
  if (!key) throw new Error("로그인 서명 비밀값이 없습니다. 설치 SQL 을 다시 실행해 주세요.");
  return await new SignJWT(hubToken ? { email, hub: hubToken } : { email })
    .setProtectedHeader({ alg: "HS256" })
    .setIssuedAt()
    .setExpirationTime(SESSION_TTL)
    .sign(key);
}

type Identity = { email: string; hubToken: string | null };

/** 우리가 발급한 세션 토큰을 검증한다. 실패하면 null. */
async function verifySessionToken(db: SupabaseClient, token: string): Promise<Identity | null> {
  const key = await sessionKey(db);
  if (!key) return null;
  try {
    const { payload } = await jwtVerify(token, key);
    const email = payload["email"];
    if (typeof email !== "string") return null;
    const hub = payload["hub"];
    return { email: email.toLowerCase(), hubToken: typeof hub === "string" ? hub : null };
  } catch {
    return null;
  }
}

/** 구글 ID 토큰이거나 우리가 발급한 세션 토큰이거나 — 신원을 확인한다. */
async function verifyIdentity(db: SupabaseClient, token: string): Promise<Identity | null> {
  const google = await verifyGoogleIdToken(token);
  if (google) return { email: google, hubToken: null };
  const session = await verifySessionToken(db, token);
  if (session) return session;
  // 둘 다 아니면 그때만 로그를 남긴다 — 어느 한쪽만 실패하는 건 정상(로그인 방식이 둘이라
  // 그렇다) 이라 노이즈가 안 되고, 진짜 문제만 여기 걸린다.
  console.error("[admin-api] 토큰 검증 실패 — 구글 ID 토큰도 세션 토큰도 아닙니다.");
  return null;
}

// 저장소 확인 결과를 잠깐 기억한다 — 요청마다 묻지 않게. 비밀번호를 바꾼 뒤 옛 로그인이 끊기기까지 길어야 이만큼 걸린다.
const HUB_CHECK_TTL_MS = 5 * 60_000;
const hubChecked = new Map<string, number>();

/** 세션에 실린 저장소 표가 아직 유효한지. 저장소가 거절하거나 초대가 지워졌으면 false, 저장소에 닿지 못하면 예외. */
async function stillValidHubToken(hubToken: string): Promise<boolean> {
  const until = hubChecked.get(hubToken);
  if (until && until > Date.now()) return true;
  try {
    const who = await hubVerify(hubToken);
    if (!who.invited && !who.isMaster) {
      hubChecked.delete(hubToken);
      return false;
    }
  } catch (e) {
    if (e instanceof HubRequestError && e.status === 401) {
      hubChecked.delete(hubToken);
      return false;
    }
    throw e;
  }
  if (hubChecked.size > 1000) hubChecked.clear();
  hubChecked.set(hubToken, Date.now() + HUB_CHECK_TTL_MS);
  return true;
}

// ═══ BEGIN usage-count ═══ (Tools~/SdkAudit 가 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 운영 콘솔 "서버 상태"의 서버 함수 호출 수에 1 을 더한다. 응답을 기다리지 않고, 실패해도 이 함수의 동작에는 영향이 없다.
// 미리 거치는 OPTIONS 요청은 Supabase 요금도 세지 않아 빼고 센다.
function countCall(name: string, req: Request): void {
  if (req.method === "OPTIONS") return;
  try {
    const key = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS") ?? "{}").default ?? "";
    const done = fetch(`${Deno.env.get("SUPABASE_URL")}/rest/v1/rpc/ts_usage_count_call`, {
      method: "POST",
      headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
      body: JSON.stringify({ p_name: name }),
    }).then((r) => r.body?.cancel()).catch(() => {});
    // 응답을 돌려준 뒤에도 이 요청이 끝날 때까지 함수가 살아 있게 한다.
    (globalThis as { EdgeRuntime?: { waitUntil(p: Promise<unknown>): void } }).EdgeRuntime?.waitUntil(done);
  } catch {
    // 세기 실패는 무시한다.
  }
}
// ═══ END usage-count ═══

// ═══ BEGIN auth-sync ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 콘솔 "서비스 연결"의 로그인 값을 Supabase 로그인 설정(관리 API)에 넣는다. 관리 열쇠는 이 프로젝트 하나에
// Auth Config·Project Settings 읽기·쓰기만 준 열쇠다.
// 기존 설정에 더하기만 하고 지우지 않는다 — 대시보드에서 직접 넣어 둔 클라이언트 ID·돌아올 주소가 콘솔에 없다고
// 빠지면 그 로그인이 깨진다. 값이 없는 로그인은 건드리지 않는다.
// 애플 Secret Key 는 Sign in with Apple 키로 서버가 만든다. 애플이 6개월까지만 받아 150일로 만들고 매주 다시 만든다.
type AuthSyncResult = { ok: boolean; skipped?: boolean; message: string };

const AUTH_SYNC_APPLE_SECRET_DAYS = 150;

function authSyncList(v: string | null | undefined): string[] {
  return (v ?? "").split(",").map((s) => s.trim()).filter((s) => s.length > 0);
}

// 앞의 것을 앞에 두고 겹치는 것은 한 번만 — 애플은 Services ID, 구글은 웹 클라이언트 ID 가 맨 앞이어야 웹·Android 로그인이 된다.
// 맨 앞에 둘 값이 콘솔에 없으면 기존 목록을 앞에 두고 뒤에 더한다 — 대시보드에서 넣어 둔 맨 앞 값이 밀려나지 않게.
function authSyncMerge(first: string[], existing: string[]): string {
  const out: string[] = [];
  for (const s of [...first, ...existing]) if (!out.includes(s)) out.push(s);
  return out.join(",");
}

async function authSyncAppleSecret(teamId: string, keyId: string, privateKey: string, servicesId: string): Promise<string> {
  const key = await importPKCS8(privateKey.replace(/\\n/g, "\n"), "ES256");
  return await new SignJWT({})
    .setProtectedHeader({ alg: "ES256", kid: keyId })
    .setIssuer(teamId)
    .setIssuedAt()
    .setExpirationTime(`${AUTH_SYNC_APPLE_SECRET_DAYS}d`)
    .setAudience("https://appleid.apple.com")
    .setSubject(servicesId)
    .sign(key);
}

async function syncAuthConfig(values: Record<string, string>): Promise<AuthSyncResult> {
  const v = (k: string) => (values[k] ?? "").trim();
  const token = v("SUPABASE_MANAGEMENT_TOKEN");
  if (!token) return { ok: true, skipped: true, message: "no_token" };

  const ref = new URL(Deno.env.get("SUPABASE_URL")!).hostname.split(".")[0];
  const api = `https://api.supabase.com/v1/projects/${ref}/config/auth`;
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const failed = (status: number, what: string): AuthSyncResult => ({
    ok: false,
    message: status === 401 || status === 403
      ? "Supabase 관리 열쇠가 맞지 않거나 권한이 모자랍니다. 열쇠를 다시 넣어 주세요."
      : status === 429
      ? "Supabase 가 요청을 잠시 막았습니다. 잠시 뒤 다시 반영해 주세요."
      : `${what}에 실패했습니다(HTTP ${status}).`,
  });

  let current: Response;
  try {
    current = await fetch(api, { headers, signal: AbortSignal.timeout(15_000) });
  } catch {
    return { ok: false, message: "Supabase 에 연결하지 못했습니다. 잠시 뒤 다시 반영해 주세요." };
  }
  if (!current.ok) {
    await current.body?.cancel();
    return failed(current.status, "로그인 설정 읽기");
  }
  const cfg = (await current.json()) as {
    uri_allow_list?: string | null; external_google_client_id?: string | null; external_apple_client_id?: string | null;
  };

  // 이메일 가입은 끈다 — SDK 에 이메일 로그인이 없고, 켜 두면 남의 지메일 주소로 먼저 가입해 두었다가 그 사람이
  // 같은 주소의 구글 계정으로 처음 로그인할 때 계정을 가로채는 길이 생긴다.
  // 이메일 자동 확인(mailer_autoconfirm)도 끈다 — 이메일 가입을 꺼도 게스트가 자기 정보 수정으로 이메일을 넣을 수 있고,
  // 자동 확인이 켜져 있으면 그 이메일이 확인 메일 없이 확인된 것으로 적혀 같은 주소의 구글·애플 첫 로그인이 그 게스트 계정에 붙는다.
  // 구글·애플은 확인된 이메일만 주므로 꺼도 소셜 첫 가입은 막히지 않는다.
  const patch: Record<string, unknown> = {
    external_anonymous_users_enabled: true,
    external_email_enabled: false,
    mailer_autoconfirm: false,
    security_manual_linking_enabled: true,
  };
  const done: string[] = ["기본 설정"];
  let problem = "";

  const googleWeb = authSyncList(values["GOOGLE_WEB_CLIENT_ID"]);
  const googleIos = authSyncList(values["GOOGLE_IOS_CLIENT_ID"]);
  if (googleWeb.length + googleIos.length > 0) {
    const existing = authSyncList(cfg.external_google_client_id);
    patch.external_google_enabled = true;
    patch.external_google_client_id = googleWeb.length > 0
      ? authSyncMerge([...googleWeb, ...googleIos], existing)
      : authSyncMerge(existing, googleIos);
    if (v("GOOGLE_WEB_CLIENT_SECRET")) patch.external_google_secret = v("GOOGLE_WEB_CLIENT_SECRET");
    done.push("Google");
  }

  const bundles = authSyncList(values["APPLE_BUNDLE_ID"]);
  const servicesId = v("APPLE_SERVICES_ID");
  if (bundles.length > 0) {
    const existing = authSyncList(cfg.external_apple_client_id);
    patch.external_apple_enabled = true;
    patch.external_apple_client_id = servicesId ? authSyncMerge([servicesId, ...bundles], existing) : authSyncMerge(existing, bundles);
    if (servicesId && v("APPLE_TEAM_ID") && v("APPLE_KEY_ID") && v("APPLE_PRIVATE_KEY")) {
      // Secret Key 를 못 만들면 그것만 빼고 나머지는 반영한다 — 키 하나 때문에 구글·돌아올 주소까지 멈추지 않게.
      try {
        patch.external_apple_secret = await authSyncAppleSecret(v("APPLE_TEAM_ID"), v("APPLE_KEY_ID"), v("APPLE_PRIVATE_KEY"), servicesId);
        done.push("Apple · Secret Key");
      } catch {
        problem = "Sign in with Apple 키로 Secret Key 를 만들지 못해 그것만 빼고 반영했습니다. Private key 를 확인해 주세요.";
        done.push("Apple");
      }
    } else {
      done.push("Apple");
    }
  }

  // 안드로이드 애플 로그인이 브라우저에서 앱으로 돌아오는 주소.
  const packages = authSyncList(values["GOOGLE_PACKAGE_NAME"]);
  if (packages.length > 0) {
    patch.uri_allow_list = authSyncMerge(authSyncList(cfg.uri_allow_list), packages.map((p) => `${p}://login-callback`));
    done.push("돌아올 주소");
  }

  let res: Response;
  try {
    res = await fetch(api, { method: "PATCH", headers, body: JSON.stringify(patch), signal: AbortSignal.timeout(15_000) });
  } catch {
    return { ok: false, message: "Supabase 에 연결하지 못했습니다. 잠시 뒤 다시 반영해 주세요." };
  }
  await res.body?.cancel();
  if (!res.ok) return failed(res.status, "로그인 설정 저장");
  if (problem) return { ok: false, message: problem };
  return { ok: true, message: done.join(" · ") };
}

// 로그인 설정 반영에 쓰는 키. 콘솔 값이 없으면 Edge Function Secrets 를 쓴다(server-config 구간과 같은 순서).
// 콘솔 서버는 이 키가 저장되면 바로 반영한다.
const AUTH_SYNC_KEYS = [
  "SUPABASE_MANAGEMENT_TOKEN", "GOOGLE_WEB_CLIENT_ID", "GOOGLE_WEB_CLIENT_SECRET", "GOOGLE_IOS_CLIENT_ID",
  "APPLE_BUNDLE_ID", "APPLE_SERVICES_ID", "APPLE_TEAM_ID", "APPLE_KEY_ID", "APPLE_PRIVATE_KEY", "GOOGLE_PACKAGE_NAME",
];
// ═══ END auth-sync ═══

/** 지금 값으로 로그인 설정을 반영하고 결과를 상태 표에 남긴다. 관리 열쇠가 없으면 아무것도 하지 않는다. */
async function runAuthSync(db: SupabaseClient): Promise<AuthSyncResult> {
  let result: AuthSyncResult;
  try {
    const { data, error } = await db.rpc("ts_server_config_get");
    if (error) throw new Error(error.message);
    const fromConsole = (data ?? {}) as Record<string, string>;
    const values: Record<string, string> = {};
    for (const k of AUTH_SYNC_KEYS) values[k] = (fromConsole[k] ?? "").trim() || (Deno.env.get(k) ?? "").trim();
    result = await syncAuthConfig(values);
  } catch (e) {
    result = { ok: false, message: `반영하지 못했습니다: ${e instanceof Error ? e.message : e}` };
  }
  if (result.skipped) return result;
  const { error } = await db.rpc("ts_purchase_refund_sync_mark", {
    p_store: "auth_sync", p_phase: "finish", p_ok: result.ok, p_message: result.message,
  });
  if (error) console.error(`[admin-api] 로그인 설정 반영 기록 실패: ${error.message}`);
  return result;
}

Deno.serve(async (req) => {
  countCall("admin-api", req);
  if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
  if (req.method !== "POST") return fail("method_not_allowed", 405);

  // ── 1. 요청 본문 ──────────────────────────────────────────────────────────────
  // 로그인 전(auth.*) 요청은 아직 신원이 없으므로, 신원 확인보다 먼저 action 을 읽어야 한다.
  let action = "";
  let params: Params = {};
  try {
    const body = (await req.json()) as { action?: unknown; params?: unknown };
    if (typeof body.action !== "string") throw new Error("action 이 필요합니다.");
    action = body.action;
    params = (body.params ?? {}) as Params;
  } catch (e) {
    return fail(e instanceof Error ? e.message : "요청 본문을 읽을 수 없습니다.", 400);
  }

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  // ── 2. 로그인 전 공개 액션(로그인·프로젝트 꺼내기) ────────────────────────────────
  // 아직 세션이 없는 상태에서만 의미가 있으므로 아래 신원 확인보다 먼저 처리하고 끝낸다.
  if (action.startsWith("auth.")) {
    try {
      switch (action) {
        // 비밀번호 로그인. 비밀번호는 트루베이스 저장소 한 곳에 있다 — 콘솔이 저장소에서 받은 표를 프로젝트마다 보내
        // 이 프로젝트의 로그인으로 바꾼다.
        case "auth.hubLogin": {
          const hubToken = str(params, "hubToken");
          const who = await hubVerify(hubToken);
          if (!who.invited && !who.isMaster) return fail("트루베이스에 초대되지 않은 계정입니다.", 403);
          if (!(await isAllowedIdentity(db, who.email))) {
            return fail("운영자 권한이 없습니다.", 403);
          }
          const token = await issueSessionToken(db, who.email, hubToken);
          return json({ ok: true, data: { token } });
        }

        case "auth.googleLogin": {
          const googleEmail = await verifyGoogleIdToken(str(params, "idToken"));
          if (!googleEmail) return fail("구글 로그인을 확인하지 못했습니다.", 401);
          if (!(await isAllowedIdentity(db, googleEmail))) {
            return fail("운영자 권한이 없습니다.", 403);
          }
          const token = await issueSessionToken(db, googleEmail);
          return json({ ok: true, data: { token } });
        }

        // 로그인 화면·게임 목록이 로그인 전에도 게임마다 색·이름·아이콘을 보여 줘야 해서 인증 없이 연다.
        // 누가 언제 바꿨는지(운영자 이메일)는 내보내지 않는다.
        case "auth.consoleLook":
        case "auth.consoleColor": { // 옛 이름 — 배포 전 콘솔 화면이 아직 부른다
          const { data, error } = await db.rpc("ts_admin_console_settings_get");
          if (error) throw new Error(error.message);
          const s = (data ?? {}) as {
            color?: string | null; display_name?: string | null; icon?: string | null; active?: boolean;
          };
          return json({
            ok: true,
            // active 가 false 면 대기 중인 프로젝트 — 콘솔이 목록에서 숨기고 "새 프로젝트 만들기"에서만 보인다.
            data: { color: s.color ?? null, display_name: s.display_name ?? null, icon: s.icon ?? null, active: s.active ?? true },
          });
        }

        // 대기 프로젝트 꺼내기. 대기 프로젝트에는 운영자가 없어 이 프로젝트의 로그인으로는 누구인지 알 수 없다 —
        // 트루베이스 초대 저장소가 준 표로 초대된 운영자(또는 마스터)인지 확인하고, 누른 사람을 첫 운영자로 넣은 뒤
        // 이 프로젝트의 로그인 표를 돌려준다(콘솔이 다시 로그인하지 않고 바로 들어간다).
        case "auth.activateProject": {
          const who = await hubVerify(str(params, "hubToken"));
          if (!who.invited && !who.isMaster) return fail("트루베이스에 초대되지 않은 계정입니다.", 403);
          const { data, error } = await db.rpc("ts_admin_project_activate", {
            p_display_name: optStr(params, "displayName"),
            p_color: optStr(params, "color"),
            p_by: who.email,
          });
          if (error) throw new Error(ACTIVATE_ERRORS[error.message] ?? error.message);
          // 이 프로젝트의 마스터(ADMIN_EMAILS)만 운영자 표에서 뺀다 — 저장소의 마스터라도 여기 마스터가 아니면
          // 운영자로 넣어야 방금 꺼낸 프로젝트에 들어온다.
          if (!MASTER_EMAILS.has(who.email)) {
            const { error: opError } = await db.rpc("ts_admin_upsert_operator", {
              p_email: who.email, p_display_name: "", p_by: who.email,
            });
            if (opError) throw new Error(opError.message);
          }
          const { error: auditError } = await db.rpc("ts_admin_audit_write", {
            p_email: who.email, p_action: "project.activate", p_params: redactParams(params),
          });
          if (auditError) console.error(`[admin-api] 운영 기록 실패 (project.activate, ${who.email}): ${auditError.message}`);
          return json({ ok: true, data: { look: data, token: await issueSessionToken(db, who.email, str(params, "hubToken")) } });
        }

        default:
          return fail(`알 수 없는 action: ${action}`, 400);
      }
    } catch (e) {
      console.error(`[admin-api] ${action} 실패: ${e}`);
      const message = e instanceof Error ? e.message : "처리에 실패했습니다.";
      if (e instanceof HubRequestError) return fail(message, e.status === 0 ? 503 : e.status === 401 ? 401 : 400);
      return fail(message, message === OPERATOR_LOOKUP_FAILED ? 503 : 400);
    }
  }

  // ── 3. 신원 확인(구글 또는 세션 토큰) ───────────────────────────────────
  const authHeader = req.headers.get("Authorization") ?? "";
  const idToken = authHeader.startsWith("Bearer ") ? authHeader.slice("Bearer ".length) : "";
  if (!idToken) return fail("로그인이 필요합니다.", 401);

  const identity = await verifyIdentity(db, idToken);
  if (!identity) return fail("로그인이 필요합니다.", 401);
  const email = identity.email;

  // 트루베이스 저장소의 표로 받은 로그인은 그 표가 아직 유효해야 한다(비밀번호를 바꾸면 무효).
  // 마스터도 거친다 — 마스터는 비활성화가 안 되므로 이것이 그 로그인을 끊는 수단이다.
  if (identity.hubToken) {
    try {
      if (!(await stillValidHubToken(identity.hubToken))) return fail("로그인이 필요합니다.", 401);
    } catch {
      return fail("트루베이스 초대 저장소에 연결하지 못했습니다. 잠시 뒤 다시 시도해 주세요.", 503);
    }
  }

  // ── 4. 인가 ────────────────────────────────────────────────────────────────
  const isMaster = MASTER_EMAILS.has(email);
  if (!isMaster) {
    let enabled: boolean;
    try {
      enabled = await isEnabledOperator(db, email);
    } catch {
      return fail(OPERATOR_LOOKUP_FAILED, 503);
    }
    if (!enabled) return fail("운영자 권한이 없습니다.", 403);
  }

  // ── 5. 처리 ────────────────────────────────────────────────────────────────
  // 운영자는 모두 같은 권한이라 누구나 초대된 사람을 운영자로 더할 수 있다. 빼기·관리 열쇠는 마스터만 —
  // 화면에서도 숨기지만 서버가 다시 막는다.
  if (MASTER_ONLY_ACTIONS.has(action) && !isMaster) {
    return fail("마스터만 할 수 있습니다.", 403);
  }
  if (action === "settings.setServerConfig" && params["key"] === "SUPABASE_MANAGEMENT_TOKEN" && !isMaster) {
    return fail("Supabase 관리 열쇠는 마스터만 넣을 수 있습니다.", 403);
  }

  const res = await handleAction(db, action, params, email, isMaster);

  // ── 6. 운영 기록 ─────────────────────────────────────────────────────────────
  // 성공한 변경만 남긴다. 기록이 실패해도 이미 끝난 변경을 되돌릴 수는 없으므로 응답은 그대로 둔다.
  if (res.ok && !READ_ACTION.test(action)) {
    const { error } = await db.rpc("ts_admin_audit_write", {
      p_email: email,
      p_action: action,
      p_params: redactParams(params),
    });
    if (error) console.error(`[admin-api] 운영 기록 실패 (${action}, ${email}): ${error.message}`);
  }
  return res;
});

const MASTER_ONLY_ACTIONS = new Set(["operators.setDisabled", "operators.delete"]);

const ACTIVATE_ERRORS: Record<string, string> = {
  console_name_required: "프로젝트 이름을 입력해 주세요.",
  console_name_too_long: "프로젝트 이름은 40자까지입니다.",
  console_color_invalid: "색은 #rrggbb 형식이어야 합니다.",
  project_already_active: "다른 사람이 방금 이 프로젝트를 만들었습니다. 목록을 새로 고친 뒤 다시 시도해 주세요.",
};

// ── 트루베이스 초대 저장소 ──────────────────────────────────────────────────────
// 콘솔과 같은 주소(Cloudflare Pages)의 /hub. 대기 프로젝트에는 운영자가 없어 "새 프로젝트를 만들 수 있는 사람"과
// "운영자로 더할 수 있는 사람"을 여기 묻는다. 운영자의 초대 표는 저장소만 확인할 수 있다(비밀값이 저장소에만 있다).
const HUB_URL = Deno.env.get("TRUEBASE_HUB_URL") ?? "https://truebase-admin.pages.dev/hub";

async function callHub<T>(action: string, params: Params): Promise<T> {
  let res: Response;
  try {
    res = await fetch(HUB_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action, params }),
      signal: AbortSignal.timeout(10_000),
    });
  } catch {
    throw new HubRequestError("트루베이스 초대 저장소에 연결하지 못했습니다. 잠시 뒤 다시 시도해 주세요.", 0);
  }
  const body = (await res.json().catch(() => null)) as { ok?: boolean; data?: T; error?: string } | null;
  if (!res.ok || !body?.ok) {
    throw new HubRequestError(body?.error ?? `초대 저장소가 요청을 처리하지 못했습니다(HTTP ${res.status}).`, res.status);
  }
  return body.data as T;
}

/** 저장소 요청 실패. status 는 저장소가 준 응답 코드이고, 닿지 못했으면 0 이다. */
class HubRequestError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

const hubVerify = (token: string) =>
  callHub<{ email: string; invited: boolean; isMaster: boolean }>("verify", { token });

// 조회 요청 이름. 여기 안 걸리는 action 은 전부 변경으로 보고 기록한다 — 새 조회를 추가하면서
// 이름을 이 꼴로 안 지으면 기록이 늘 뿐 빠지지는 않는 쪽으로 틀리게 해 둔다.
const READ_ACTION =
  /^session\.me$|\.(list|get[A-Z]\w*|columns|profile|banInfo|logs|channels|messages|mutes|overview|settingsGet|codes|scores|searchPlayers|stats|player|coupons|get)$/;

const SECRET_PARAM = /password|secret|token/i;

function redactParams(params: Params): Params {
  const out: Params = {};
  for (const [k, v] of Object.entries(params)) {
    if (SECRET_PARAM.test(k)) out[k] = "***";
    // 그림(data URL)은 기록을 수십 KB씩 불린다 — 바꿨다는 사실만 남긴다.
    else if (typeof v === "string" && v.startsWith("data:image/")) out[k] = "(그림)";
    else out[k] = v;
  }
  return out;
}

// 스토어·애플 연동 값의 형식 검사. 키 목록은 install.sql 의 ts_server_config_keys() 와 같아야 한다 —
// 거기 없는 키는 ts_admin_server_config_set 이 거절한다.
const SERVER_CONFIG_VALID: Record<string, (v: string) => boolean> = {
  APPLE_BUNDLE_ID: (v) => v.split(",").every((s) => /^[A-Za-z0-9.-]+$/.test(s.trim())),
  APPLE_SHARED_SECRET: () => true,
  // 안드로이드 패키지 이름: 점으로 나뉜 두 마디 이상, 마디마다 영문으로 시작하고 영문·숫자·밑줄.
  GOOGLE_PACKAGE_NAME: (v) => v.split(",").every((s) => /^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$/.test(s.trim())),
  GOOGLE_SERVICE_ACCOUNT_JSON: (v) => {
    try {
      const sa = JSON.parse(v);
      return typeof sa.client_email === "string" && typeof sa.private_key === "string";
    } catch {
      return false;
    }
  },
  APPLE_TEAM_ID: (v) => /^[A-Z0-9]{10}$/.test(v),
  APPLE_KEY_ID: (v) => /^[A-Z0-9]{10}$/.test(v),
  APPLE_PRIVATE_KEY: (v) => v.includes("BEGIN PRIVATE KEY"),
  APPLE_IAP_ISSUER_ID: (v) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v),
  APPLE_IAP_KEY_ID: (v) => /^[A-Z0-9]{10}$/.test(v),
  APPLE_IAP_PRIVATE_KEY: (v) => v.includes("BEGIN PRIVATE KEY"),
  FCM_SERVICE_ACCOUNT_JSON: (v) => {
    try {
      const sa = JSON.parse(v);
      return typeof sa.client_email === "string" && typeof sa.private_key === "string" && typeof sa.project_id === "string";
    } catch {
      return false;
    }
  },
  APNS_KEY_ID: (v) => /^[A-Z0-9]{10}$/.test(v),
  APNS_PRIVATE_KEY: (v) => v.includes("BEGIN PRIVATE KEY"),
  GOOGLE_WEB_CLIENT_ID: (v) => /^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$/.test(v),
  APPLE_SERVICES_ID: (v) => /^[A-Za-z0-9.-]+$/.test(v),
  GOOGLE_WEB_CLIENT_SECRET: (v) => /^\S+$/.test(v),
  GOOGLE_IOS_CLIENT_ID: (v) => v.split(",").every((s) => /^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$/.test(s.trim())),
  SUPABASE_MANAGEMENT_TOKEN: (v) => /^\S{20,}$/.test(v),
};

const SERVER_CONFIG_FORMAT_HINT: Record<string, string> = {
  APPLE_BUNDLE_ID: "번들 ID 는 영문·숫자·점·하이픈만 씁니다. 여럿이면 쉼표로 나눕니다.",
  GOOGLE_PACKAGE_NAME: "패키지 이름은 com.회사.게임처럼 점으로 나뉜 영문·숫자·밑줄입니다. 여럿이면 쉼표로 나눕니다.",
  GOOGLE_SERVICE_ACCOUNT_JSON: "서비스 계정 키 JSON 파일 내용 전체를 붙여 넣으세요(client_email·private_key 가 있어야 합니다).",
  APPLE_TEAM_ID: "팀 ID 는 영문 대문자·숫자 10자입니다.",
  APPLE_KEY_ID: "키 ID 는 영문 대문자·숫자 10자입니다.",
  APPLE_PRIVATE_KEY: ".p8 파일 내용 전체(-----BEGIN PRIVATE KEY----- 줄 포함)를 붙여 넣으세요.",
  APPLE_IAP_ISSUER_ID: "발급자 ID 는 하이픈이 들어간 36자입니다(예: 57246542-96fe-1a63-e053-0824d011072a).",
  APPLE_IAP_KEY_ID: "키 ID 는 영문 대문자·숫자 10자입니다.",
  APPLE_IAP_PRIVATE_KEY: ".p8 파일 내용 전체(-----BEGIN PRIVATE KEY----- 줄 포함)를 붙여 넣으세요.",
  FCM_SERVICE_ACCOUNT_JSON: "Firebase 서비스 계정 키 JSON 파일 내용 전체를 붙여 넣으세요(project_id·client_email·private_key 가 있어야 합니다).",
  APNS_KEY_ID: "키 ID 는 영문 대문자·숫자 10자입니다.",
  APNS_PRIVATE_KEY: ".p8 파일 내용 전체(-----BEGIN PRIVATE KEY----- 줄 포함)를 붙여 넣으세요.",
  GOOGLE_WEB_CLIENT_ID: "클라이언트 ID 는 숫자-영문.apps.googleusercontent.com 꼴입니다.",
  APPLE_SERVICES_ID: "Services ID 는 영문·숫자·점·하이픈만 씁니다.",
  GOOGLE_WEB_CLIENT_SECRET: "클라이언트 보안 비밀번호는 띄어쓰기 없이 한 줄입니다.",
  GOOGLE_IOS_CLIENT_ID: "클라이언트 ID 는 숫자-영문.apps.googleusercontent.com 꼴입니다. 여럿이면 쉼표로 나눕니다.",
  SUPABASE_MANAGEMENT_TOKEN: "Supabase 에서 복사한 열쇠를 띄어쓰기 없이 그대로 붙여 넣으세요.",
};

async function handleAction(
  db: SupabaseClient,
  action: string,
  params: Params,
  email: string,
  isMaster: boolean,
): Promise<Response> {
  try {
    switch (action) {
      case "session.me":
        return json({ ok: true, data: { email, isMaster } });

      case "audit.list": {
        const { data, error } = await db.rpc("ts_admin_audit_list", {
          p_search: optStr(params, "search") || null,
          p_category: optStr(params, "category") || null,
          p_start_date: optStr(params, "startDate") || null,
          p_end_date: optStr(params, "endDate") || null,
          p_page: typeof params["page"] === "number" ? params["page"] : 1,
          p_page_size: typeof params["pageSize"] === "number" ? params["pageSize"] : 50,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "servers.list": {
        const { data, error } = await db.rpc("ts_admin_game_servers_list");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "servers.upsert": {
        const { data, error } = await db.rpc("ts_admin_game_server_upsert", {
          p_id: optStr(params, "id") || null,
          p_server_code: optStr(params, "serverCode"),
          p_display_name: str(params, "displayName"),
          p_allow_transfers: bool(params, "allowTransfers"),
        });
        if (error) throw new Error(SERVER_ERRORS[error.message] ?? error.message);
        return json({ ok: true, data: { id: data } });
      }

      case "bannedWords.list": {
        const { data, error } = await db.rpc("ts_admin_banned_words_list");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "bannedWords.add": {
        const kind = str(params, "kind");
        const words = Array.isArray(params["words"]) ? (params["words"] as unknown[]).map(String) : [];
        if (words.length === 0) throw new Error("추가할 단어를 입력하세요.");
        const { data, error } = await db.rpc("ts_admin_banned_words_add", { p_kind: kind, p_words: words, p_by: email });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { added: data } });
      }

      case "bannedWords.delete": {
        const kind = str(params, "kind");
        const words = Array.isArray(params["words"]) ? (params["words"] as unknown[]).map(String) : [];
        const { data, error } = await db.rpc("ts_admin_banned_words_delete", { p_kind: kind, p_words: words });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { deleted: data } });
      }

      case "players.coupons": {
        const { data, error } = await db.rpc("ts_admin_player_coupons", {
          p_account_id: str(params, "accountId"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "operators.list": {
        const { data, error } = await db.rpc("ts_admin_list_operators");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "operators.upsert": {
        // 소문자로 저장해야 한다 — 로그인 판정이 전부 소문자로 비교한다
        // (원본 대소문자를 그대로 넣으면 그 운영자는 영원히 인증에 실패한다).
        const targetEmail = str(params, "email").trim().toLowerCase();
        // 운영자로 더할 수 있는 사람은 트루베이스에 초대된 사람뿐이다. 묻는 표는 부른 사람의 초대 표다.
        const { invited } = await callHub<{ invited: boolean }>("check", { token: str(params, "hubToken"), email: targetEmail });
        if (!invited) throw new Error("트루베이스에 초대되지 않은 이메일입니다. 마스터에게 초대를 요청해 주세요.");
        // 다시 넣으면 비활성이 풀린다 — 마스터가 막은 사람을 다른 운영자가 되살리지 못하게 한다.
        if (!isMaster) {
          const { data: ops, error: listError } = await db.rpc("ts_admin_list_operators");
          if (listError) throw new Error(listError.message);
          const existing = ((ops ?? []) as Array<{ email: string; disabled_at: string | null }>).find((o) => o.email === targetEmail);
          if (existing?.disabled_at) throw new Error("비활성화된 운영자는 마스터만 되살릴 수 있습니다.");
        }
        const { error } = await db.rpc("ts_admin_upsert_operator", {
          p_email: targetEmail,
          p_display_name: optStr(params, "displayName"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "operators.setDisabled": {
        const targetEmail = str(params, "email").toLowerCase();
        // 여기 오는 요청은 항상 마스터가 보낸 것이다(MASTER_ONLY_ACTIONS 로 위에서 이미 막았다) — 마스터
        // 접근은 이 표가 아니라 ADMIN_EMAILS 로 정해지므로, 자기 이메일이 표에 우연히 들어 있어도
        // 그걸 비활성화한다고 본인이 잠기지 않는다. 막을 이유가 없다.
        const { error } = await db.rpc("ts_admin_set_operator_disabled", {
          p_email: targetEmail,
          p_disabled: bool(params, "disabled"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "operators.delete": {
        // 되돌릴 수 없다 — 화면에서도 비활성화된 운영자만 지울 수 있게 막지만, 서버가 다시 막는다.
        // (자기 이메일 여부는 안 막는다 — setDisabled 와 같은 이유.)
        const targetEmail = str(params, "email").toLowerCase();
        const { error } = await db.rpc("ts_admin_delete_operator", { p_email: targetEmail });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "gameItems.list": {
        const { data, error } = await db.rpc("ts_admin_list_game_items", {
          p_search: optStr(params, "search"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "gameItems.upsert": {
        const { error } = await db.rpc("ts_admin_upsert_game_item", {
          p_key: str(params, "key"),
          p_display_name: optStr(params, "displayName"),
        });
        // 동시에 두 창에서 같은 이름을 넣으면 함수 검사를 지나 색인(game_items_display_name_uniq)에서 걸린다.
        if (error) {
          throw new Error(error.message.includes("item_name_taken") || error.message.includes("game_items_display_name_uniq")
            ? "같은 이름의 아이템이 이미 있습니다."
            : error.message);
        }
        return json({ ok: true, data: null });
      }

      case "gameItems.delete": {
        const { error } = await db.rpc("ts_admin_delete_game_item", {
          p_key: str(params, "key"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "players.list": {
        const { data, error } = await db.rpc("ts_admin_search_players", {
          p_search: optStr(params, "search"),
          p_banned_only: bool(params, "bannedOnly"),
          p_page: typeof params["page"] === "number" ? params["page"] : 1,
          p_page_size: 20,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "players.profile": {
        const { data, error } = await db.rpc("ts_admin_player_profile", {
          p_account_id: str(params, "accountId"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "players.banInfo": {
        const { data, error } = await db.rpc("ts_admin_player_ban_info", {
          p_account_id: str(params, "accountId"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "players.ban": {
        const accountId = str(params, "accountId");
        const bannedUntil = str(params, "bannedUntil");
        const banMessage = optStr(params, "banMessage");

        const untilMs = Date.parse(bannedUntil);
        if (Number.isNaN(untilMs)) throw new Error("bannedUntil 형식이 올바르지 않습니다.");
        const seconds = Math.ceil((untilMs - Date.now()) / 1000);
        // 인증 서버는 기간을 Go duration 으로 읽어 약 292년을 넘기면 넘친다. 공식 문서의 "영구" 예시인
        // 876000h(100년)를 상한으로 둔다 — 콘솔의 영구 차단도 이 값으로 들어간다.
        const MAX_BAN_SECONDS = 876000 * 3600;
        const banDuration = seconds <= 0 ? "none" : `${Math.min(seconds, MAX_BAN_SECONDS)}s`;

        const { error: banError } = await db.auth.admin.updateUserById(accountId, { ban_duration: banDuration });
        if (banError) throw new Error(banError.message);

        const { error: msgError } = await db
          .from("user_ban_messages")
          .upsert(
            { account_id: accountId, ban_message: banMessage, updated_at: new Date().toISOString() },
            { onConflict: "account_id" },
          );
        if (msgError) throw new Error(msgError.message);

        return json({ ok: true, data: null });
      }

      case "players.unban": {
        const { error } = await db.auth.admin.updateUserById(str(params, "accountId"), { ban_duration: "none" });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "players.setDisplayName": {
        const accountId = str(params, "accountId");
        const displayName = str(params, "displayName").trim();
        if (displayName.length > 64) throw new Error("닉네임이 너무 깁니다.");

        const { data: profile, error: profileError } = await db
          .from("user_profiles")
          .select("account_id, user_id, server_id")
          .eq("account_id", accountId)
          .maybeSingle();
        if (profileError) throw new Error(profileError.message);
        if (!profile) throw new Error("플레이어를 찾을 수 없습니다.");

        const { error: upsertError } = await db
          .from("display_names")
          .upsert(
            {
              account_id: profile.account_id,
              user_id: profile.user_id,
              server_id: profile.server_id,
              display_name: displayName,
              updated_at: new Date().toISOString(),
            },
            { onConflict: "account_id" },
          );
        if (upsertError) {
          const msg = upsertError.message ?? "";
          throw new Error(msg.toLowerCase().includes("duplicate") || msg.toLowerCase().includes("unique")
            ? "이미 사용 중인 닉네임입니다."
            : msg);
        }

        // user_metadata 동기화 — 닉네임 3원(display_names·user_metadata·이 화면)을 맞춘다.
        const { data: existing, error: getUserError } = await db.auth.admin.getUserById(accountId);
        if (getUserError || !existing?.user) throw new Error(getUserError?.message ?? "계정 조회에 실패했습니다.");
        const prevMeta = (existing.user.user_metadata ?? {}) as Record<string, unknown>;
        const { error: metaError } = await db.auth.admin.updateUserById(accountId, {
          user_metadata: { ...prevMeta, displayName, full_name: displayName, name: displayName },
        });
        if (metaError) throw new Error(metaError.message);

        return json({ ok: true, data: null });
      }

      case "userData.columns": {
        const { data, error } = await db.rpc("ts_admin_user_data_columns");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "userData.get": {
        const { data, error } = await db.rpc("ts_admin_user_data_get", {
          p_account_id: str(params, "accountId"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "userData.update": {
        const { data, error } = await db.rpc("ts_admin_user_data_update", {
          p_account_id: str(params, "accountId"),
          p_patch: params["patch"] ?? {},
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "userData.getRestorePreview": {
        const { data, error } = await db.rpc("ts_admin_user_data_restore_preview", {
          p_account_id: str(params, "accountId"),
          p_log_id: params["logId"],
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "userData.restore": {
        const columns = Array.isArray(params["columns"]) ? params["columns"].filter((c) => typeof c === "string") : [];
        const { data, error } = await db.rpc("ts_admin_user_data_restore", {
          p_account_id: str(params, "accountId"),
          p_log_id: params["logId"],
          p_columns: columns,
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "userData.reset": {
        const { data, error } = await db.rpc("ts_admin_user_data_reset", {
          p_account_id: str(params, "accountId"),
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "jobs.list": {
        const { data, error } = await db.rpc("ts_admin_cron_jobs");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "userData.logs": {
        const { data, error } = await db.rpc("ts_admin_user_data_logs", {
          p_account_id: str(params, "accountId"),
          p_limit: typeof params["limit"] === "number" ? params["limit"] : 20,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "dataManagement.searchPlayers": {
        const { data, error } = await db.rpc("ts_admin_search_user_data_players", {
          p_search: optStr(params, "search"),
          p_page: typeof params["page"] === "number" ? params["page"] : 1,
          p_page_size: 20,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "purchases.list": {
        const search = sanitizeSearchTerm(optStr(params, "search").trim());
        const accountId = optStr(params, "accountId").trim();
        if (accountId && !UUID_RE.test(accountId)) return json({ ok: true, data: { rows: [], total: 0, pageSize: 20 } });
        const byAccount = search ? await accountFilterForSearch(db, search) : { anyOf: [], truncated: false };
        const startDate = params["startDate"];
        const endDate = params["endDate"];
        const tz = startDate || endDate ? await gameTimeZoneOf(db) : "";
        const filtered = (from: number, to: number) => {
          let query = db
            .from("purchases")
            .select(
              "id, account_id, user_id, product_id, order_id, package_name, store, price_amount, price_currency, price_amount_krw, verified_at, refunded_at, refund_reason, is_test, is_free",
              { count: "exact" },
            )
            .order("verified_at", { ascending: false })
            .order("id", { ascending: false })
            .range(from, to);
          if (bool(params, "refundedOnly")) query = query.not("refunded_at", "is", null);
          // 시험 결제: only = 시험만, exclude = 시험 빼고(모름은 실제로 친다), 그 밖 = 전부.
          const test = optStr(params, "test");
          if (test === "only") query = query.eq("is_test", true);
          else if (test === "exclude") query = query.not("is_test", "is", true);
          if (accountId) query = query.eq("account_id", accountId);
          if (search) {
            query = query.or([`product_id.ilike.%${search}%`, `user_id.ilike.%${search}%`, `order_id.ilike.%${search}%`, ...byAccount.anyOf].join(","));
          }
          const start = typeof startDate === "string" ? dayStartIn(tz, startDate) : null;
          if (start) query = query.gte("verified_at", start);
          if (typeof endDate === "string" && endDate) {
            const next = nextDayExclusiveIn(tz, endDate);
            if (next) query = query.lt("verified_at", next);
          }
          return query;
        };

        // all: CSV 내려받기 — 같은 조건 전체. PostgREST 는 한 번에 1000행까지만 주므로 나눠 받는다.
        if (bool(params, "all")) {
          const CHUNK = 1000;
          const MAX = 100_000;
          const rows: unknown[] = [];
          for (let from = 0; from < MAX; from += CHUNK) {
            const { data, error } = await filtered(from, from + CHUNK - 1);
            if (error) throw new Error(error.message);
            rows.push(...(data ?? []));
            if (!data || data.length < CHUNK) break;
          }
          return json({ ok: true, data: { rows: await withDisplayNames(db, rows as Array<{ account_id?: unknown }>), total: rows.length, truncated: rows.length >= MAX } });
        }

        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 20;
        const from = (page - 1) * pageSize;
        const { data, error, count } = await filtered(from, from + pageSize - 1);
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: await withDisplayNames(db, data ?? []), total: count ?? 0, pageSize, nameSearchTruncated: byAccount.truncated } });
      }

      case "purchases.getRefundStatus": {
        const { data, error } = await db
          .from("purchase_refund_sync")
          .select("store, last_started_at, last_finished_at, last_ok, last_message, last_marked");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      // 구글 환불을 지금 확인한다. 함수가 10분 안의 재호출을 건너뛰므로 연타해도 구글에 한 번만 묻는다.
      case "purchases.syncRefunds": {
        const res = await fetch(`${SUPABASE_URL}/functions/v1/purchase-refund-sync-google`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: "{}",
        });
        const body = await res.json().catch(() => null);
        if (res.status === 404) throw new Error("이 프로젝트에는 구글 환불 확인 기능이 설치되어 있지 않습니다.");
        return json({ ok: true, data: body ?? { ok: false, message: `HTTP ${res.status}` } });
      }

      case "remoteConfig.list": {
        const { data, error } = await db
          .from("remote_config")
          .select("key, value_json, enabled, requires_auth, description, version, updated_at")
          .order("key");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "remoteConfig.stageNew": {
        const { data, error } = await db.rpc("ts_admin_schema_stage_config_new", {
          p_key: str(params, "key"),
          p_enabled: bool(params, "enabled"),
          p_requires_auth: bool(params, "requiresAuth"),
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "remoteConfig.stageMeta": {
        const { data, error } = await db.rpc("ts_admin_schema_stage_config_meta", {
          p_key: str(params, "key"),
          p_enabled: bool(params, "enabled"),
          p_requires_auth: bool(params, "requiresAuth"),
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "remoteConfig.stageItem": {
        const { data, error } = await db.rpc("ts_admin_schema_stage_config_item", {
          p_key: str(params, "key"),
          p_item_key: str(params, "itemKey"),
          p_item_value: params["itemValue"] ?? null,
          p_meta_type: null,
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "remoteConfig.stageItemDelete": {
        const { data, error } = await db.rpc("ts_admin_schema_stage_config_item_delete", {
          p_key: str(params, "key"),
          p_item_key: str(params, "itemKey"),
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "remoteConfig.stageDelete": {
        const { data, error } = await db.rpc("ts_admin_schema_stage_config_delete", {
          p_key: str(params, "key"),
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "dataLogs.list": {
        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 30;
        const from = (page - 1) * pageSize;
        const to = from + pageSize - 1;

        let query = db
          .from("user_data_logs")
          .select("id, account_id, diff, source, created_at", { count: "exact" })
          .order("id", { ascending: false })
          .range(from, to);

        const accountId = optStr(params, "accountId").trim();
        if (accountId) {
          // account_id 는 uuid 컬럼이다 — 형식이 안 맞으면 Postgres 가 원본 에러를 던지므로,
          // 다른 검색 필드처럼 "결과 없음"으로 조용히 처리되게 여기서 먼저 걸러낸다.
          if (!UUID_RE.test(accountId)) return json({ ok: true, data: { rows: [], total: 0, pageSize } });
          query = query.eq("account_id", accountId);
        }

        const player = sanitizeSearchTerm(optStr(params, "search").trim());
        const byAccount = player ? await accountFilterForSearch(db, player) : { anyOf: [], truncated: false };
        if (player) {
          if (byAccount.anyOf.length === 0) return json({ ok: true, data: { rows: [], total: 0, pageSize } });
          query = query.or(byAccount.anyOf.join(","));
        }

        const source = sanitizeSearchTerm(optStr(params, "source").trim());
        if (source) query = query.ilike("source", `%${source}%`);

        const startDate = params["startDate"];
        const endDate = params["endDate"];
        const tz = startDate || endDate ? await gameTimeZoneOf(db) : "";
        const start = typeof startDate === "string" ? dayStartIn(tz, startDate) : null;
        if (start) query = query.gte("created_at", start);
        if (typeof endDate === "string" && endDate) {
          const next = nextDayExclusiveIn(tz, endDate);
          if (next) query = query.lt("created_at", next);
        }

        const { data, error, count } = await query;
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: await withDisplayNames(db, data ?? []), total: count ?? 0, pageSize, nameSearchTruncated: byAccount.truncated } });
      }

      case "dashboard.stats": {
        const { data, error } = await db.rpc("ts_admin_dashboard_stats");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "chat.stats": {
        const { data, error } = await db.rpc("ts_admin_chat_stats", { p_days: intInRange(params, "days", "기간", 1, 365) });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "revenue.stats": {
        const { data, error } = await db.rpc("ts_admin_revenue_by_country", { p_days: intInRange(params, "days", "기간", 1, 365) });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "activity.stats": {
        // 가입 플랫폼 거르기 — 비우면 전체, 'unknown' 이면 가입 플랫폼을 모르는 계정.
        const signupPlatform = optStr(params, "signupPlatform").trim();
        if (signupPlatform !== "" && !/^[a-z0-9_]{1,20}$/.test(signupPlatform)) throw new Error("가입 플랫폼 값이 올바르지 않습니다.");
        const { data, error } = await db.rpc("ts_admin_activity_stats", {
          p_days: intInRange(params, "days", "기간", 7, 365),
          p_signup_platform: signupPlatform || null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "ccu.stats": {
        const { data, error } = await db.rpc("ts_admin_ccu", { p_hours: intInRange(params, "hours", "기간", 1, 24 * 3650) });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "usage.stats": {
        const { data, error } = await db.rpc("ts_admin_usage", { p_hours: intInRange(params, "hours", "기간", 1, 24 * 3650) });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "chat.channels": {
        const { data, error } = await db
          .from("chat_channels")
          .select("id, kind, code, display_name, is_active, slow_mode_seconds, max_length, retention_days")
          .order("kind")
          .order("code");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "chat.messages": {
        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 30;
        const from = (page - 1) * pageSize;
        const to = from + pageSize - 1;

        let query = db
          .from("chat_messages")
          .select("id, channel_id, scope_key, account_id, user_id, display_name, content, created_at, deleted_at, deleted_by, is_notice", { count: "exact" })
          .order("id", { ascending: false })
          .range(from, to);

        const channelId = params["channelId"];
        if (typeof channelId === "string" && channelId) query = query.eq("channel_id", channelId);
        if (!bool(params, "includeDeleted")) query = query.is("deleted_at", null);

        const accountId = optStr(params, "accountId").trim();
        if (accountId) {
          if (!UUID_RE.test(accountId)) return json({ ok: true, data: { rows: [], total: 0, pageSize } });
          query = query.eq("account_id", accountId);
        }

        const search = sanitizeSearchTerm(optStr(params, "search").trim());
        if (search) query = query.or(`display_name.ilike.%${search}%,content.ilike.%${search}%,user_id.ilike.%${search}%`);

        const { data, error, count } = await query;
        if (error) throw new Error(error.message);

        // 귓속말 행에는 받는 사람이 없다 — scope_key 에서 상대를 뽑고 닉네임까지 붙여 준다.
        // kind='direct' 채널의 행만 본다. 문자열 모양만 보고 판단하면 게임이 정한 group
        // 채널 키가 우연히 같은 형태일 때 귓속말이 아닌 메시지에 상대가 찍힌다.
        const rows = (data ?? []) as Array<Record<string, unknown>>;
        const directIds = new Set<string>();
        if (rows.length > 0) {
          const { data: directChannels, error: channelError } = await db
            .from("chat_channels")
            .select("id")
            .eq("kind", "direct");
          if (channelError) throw new Error(channelError.message);
          for (const c of directChannels ?? []) directIds.add(c.id as string);
        }
        const partnerOf = (r: Record<string, unknown>): string | null =>
          directIds.has(r["channel_id"] as string) ? directPartnerId(r["scope_key"], r["account_id"]) : null;

        const partnerIds = new Set<string>();
        for (const r of rows) {
          const other = partnerOf(r);
          if (other) partnerIds.add(other);
        }

        const partnerNames = new Map<string, string>();
        if (partnerIds.size > 0) {
          const { data: names, error: nameError } = await db
            .from("display_names")
            .select("account_id, display_name")
            .in("account_id", [...partnerIds]);
          if (nameError) throw new Error(nameError.message);
          for (const n of names ?? []) partnerNames.set(n.account_id as string, (n.display_name as string) ?? "");
        }

        const withPartner = rows.map((r) => {
          const other = partnerOf(r);
          return { ...r, to_account_id: other, to_display_name: other ? partnerNames.get(other) ?? "" : null };
        });

        return json({ ok: true, data: { rows: withPartner, total: count ?? 0, pageSize } });
      }

      case "chat.deleteMessage": {
        const { error } = await db.rpc("ts_admin_chat_delete_message", {
          p_id: typeof params["id"] === "number" ? params["id"] : Number(str(params, "id")),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "chat.mutes": {
        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 30;
        const from = (page - 1) * pageSize;
        const to = from + pageSize - 1;

        const { data, error, count } = await db
          .from("chat_mutes")
          .select("id, account_id, channel_id, until, reason, created_by, created_at", { count: "exact" })
          .order("created_at", { ascending: false })
          .range(from, to);
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: await withDisplayNames(db, data ?? []), total: count ?? 0, pageSize } });
      }

      case "chat.notice": {
        const { data, error } = await db.rpc("ts_admin_chat_notice", {
          p_channel_id: str(params, "channelId"),
          p_content: str(params, "content"),
          p_server_id: optStr(params, "serverId") || null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { count: data } });
      }

      case "chat.mute": {
        const { error } = await db.rpc("ts_admin_chat_mute", {
          p_account_id: str(params, "accountId"),
          p_channel_id: optStr(params, "channelId") || null,
          p_minutes: typeof params["minutes"] === "number" ? params["minutes"] : 60,
          p_reason: optStr(params, "reason"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "chat.unmute": {
        const { error } = await db.rpc("ts_admin_chat_unmute", {
          p_id: typeof params["id"] === "number" ? params["id"] : Number(str(params, "id")),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "friends.overview": {
        const { data, error } = await db.rpc("ts_admin_friend_overview", {
          p_account_id: str(params, "accountId"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      // 스토어·애플 연동 값이 어디에 등록돼 있고 형식이 맞는지만 돌려준다. 값은 절대 내보내지 않는다.
      // 엣지 함수와 같은 순서로 고른다 — 콘솔에서 넣은 값(Vault)이 먼저, 없으면 Edge Function Secrets.
      case "settings.getStoreStatus": {
        const [{ data: values, error: valuesError }, { data: meta, error: metaError }, { data: services, error: servicesError }] = await Promise.all([
          db.rpc("ts_server_config_get"),
          db.rpc("ts_admin_server_config_status"),
          db.rpc("ts_admin_server_config_services"),
        ]);
        if (valuesError) throw new Error(valuesError.message);
        if (metaError) throw new Error(metaError.message);
        // 켜기/끄기는 화면 표시용이라, 이것 하나 때문에 키 상태 표 전체가 안 뜨면 안 된다 — 비우면 화면이 값 유무로 짐작한다.
        if (servicesError) console.warn(`[admin-api] 서비스 켜기/끄기 상태를 읽지 못했습니다: ${servicesError.message}`);
        const { data: authSync, error: authSyncError } = await db
          .from("purchase_refund_sync")
          .select("last_finished_at, last_ok, last_message")
          .eq("store", "auth_sync")
          .maybeSingle();
        if (authSyncError) console.warn(`[admin-api] 로그인 설정 반영 상태를 읽지 못했습니다: ${authSyncError.message}`);
        const fromConsole = (values ?? {}) as Record<string, string>;
        const metaByKey = new Map(((meta ?? []) as Array<{ key: string; updated_at: string; updated_by: string }>).map((m) => [m.key, m]));
        const rows = Object.entries(SERVER_CONFIG_VALID).map(([key, valid]) => {
          const consoleValue = (fromConsole[key] ?? "").trim();
          const envValue = (Deno.env.get(key) ?? "").trim();
          const value = consoleValue || envValue;
          const m = consoleValue ? metaByKey.get(key) : undefined;
          return {
            key,
            source: consoleValue ? "console" : envValue ? "supabase" : null,
            status: value === "" ? "missing" : valid(value) ? "ok" : "invalid",
            updatedAt: m?.updated_at ?? null,
            updatedBy: m?.updated_by || null,
          };
        });
        return json({
          ok: true,
          data: {
            rows,
            services: servicesError ? {} : services ?? {},
            authSync: authSync
              ? { finishedAt: authSync.last_finished_at, ok: authSync.last_ok, message: authSync.last_message }
              : null,
          },
        });
      }

      // Supabase 로그인 설정에 지금 값을 다시 넣는다(서비스 연결 › Supabase 의 "다시 반영").
      case "settings.syncAuth": {
        const result = await runAuthSync(db);
        if (result.skipped) throw new Error("Supabase 관리 열쇠가 없습니다.");
        if (!result.ok) throw new Error(result.message);
        return json({ ok: true, data: { message: result.message } });
      }

      // 화면 표시만 바꾼다 — 엣지 함수는 이 값을 보지 않는다.
      case "settings.setServiceEnabled": {
        const service = str(params, "service");
        if (!["appstore", "googleplay", "signin", "fcm", "apns", "google", "supabase"].includes(service)) throw new Error("모르는 서비스입니다.");
        if (typeof params.enabled !== "boolean") throw new Error("켜짐 여부가 없습니다.");
        const { data, error } = await db.rpc("ts_admin_server_config_service_set", { p_service: service, p_enabled: params.enabled, p_by: email });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { services: data ?? {} } });
      }

      // 값은 secretValue 로 받는다 — 운영 기록에서 가려지는 이름이다(SECRET_PARAM). 빈 값이면 콘솔 값을 지운다.
      case "settings.setServerConfig": {
        const key = str(params, "key");
        if (!Object.hasOwn(SERVER_CONFIG_VALID, key)) throw new Error("모르는 항목입니다.");
        const valid = SERVER_CONFIG_VALID[key];
        const value = optStr(params, "secretValue").trim();
        if (value !== "" && !valid(value)) throw new Error(SERVER_CONFIG_FORMAT_HINT[key] ?? "형식이 맞지 않습니다.");
        const { error } = await db.rpc("ts_admin_server_config_set", { p_key: key, p_value: value, p_by: email });
        if (error) throw new Error(error.message);
        // 로그인 값이면 바로 Supabase 로그인 설정에 넣는다. 반영이 실패해도 저장은 된 것이라 성공으로 답하고 결과를 함께 준다.
        // 지운 값은 Supabase 쪽에서 빼지 않는다(반영은 더하기만 한다).
        const authSync = AUTH_SYNC_KEYS.includes(key) ? await runAuthSync(db) : null;
        return json({
          ok: true,
          data: authSync && !authSync.skipped ? { authSync: { ok: authSync.ok, message: authSync.message } } : null,
        });
      }

      case "settings.getConsole": {
        const { data, error } = await db.rpc("ts_admin_console_settings_get");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "settings.setConsoleColor": {
        const { data, error } = await db.rpc("ts_admin_console_settings_update", {
          p_color: optStr(params, "color"),
          p_by: email,
        });
        if (error) throw new Error(error.message === "console_color_invalid" ? "색은 #rrggbb 형식이어야 합니다." : error.message);
        return json({ ok: true, data });
      }

      // 게임 개발자에게 넘길 연결 정보. 공개 키는 게임 빌드에 그대로 들어가는 값이라 운영자 누구에게나 보인다.
      case "project.getConnection": {
        const publishable = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS") ?? "{}") as Record<string, string>;
        return json({ ok: true, data: { url: SUPABASE_URL, publishableKey: publishable["default"] ?? null } });
      }

      case "settings.setConsoleIdentity": {
        const { data, error } = await db.rpc("ts_admin_console_identity_update", {
          p_display_name: optStr(params, "displayName"),
          p_icon: optStr(params, "icon"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.settingsGet": {
        const { data, error } = await db.rpc("ts_admin_mail_settings_get");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.settingsUpdate": {
        // 범위는 ts_admin_mail_settings_update 와 같게 둔다.
        const { data, error } = await db.rpc("ts_admin_mail_settings_update", {
          p_expired_retention_days: intInRange(params, "expiredRetentionDays", "만료 우편 보관 일수", 0, 30),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "game.settingsGet": {
        const { data, error } = await db.rpc("ts_admin_game_settings_get");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "game.settingsUpdate": {
        const { data, error } = await db.rpc("ts_admin_game_settings_update", {
          p_time_zone: str(params, "timeZone"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "requestLimit.settingsGet": {
        const { data, error } = await db.rpc("ts_admin_request_limit_settings_get");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "requestLimit.settingsUpdate": {
        // 범위는 install.sql request_limit_settings 의 check 와 같게 둔다.
        if (typeof params.enabled !== "boolean") throw new Error("사용 여부가 없습니다.");
        const { data, error } = await db.rpc("ts_admin_request_limit_settings_update", {
          p_enabled: params.enabled,
          p_window_seconds: intInRange(params, "windowSeconds", "기준 시간", 1, 600),
          p_max_requests: intInRange(params, "maxRequests", "최대 요청 수", 10, 10000),
          p_block_minutes: intInRange(params, "blockMinutes", "제한 시간", 1, 1440),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      // 이름이 list 로 끝나야 조회로 보고 운영 기록에 남기지 않는다(READ_ACTION).
      case "requestLimit.list": {
        const { data, error } = await db.rpc("ts_admin_request_limit_blocks");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "requestLimit.unblock": {
        const { error } = await db.rpc("ts_admin_request_limit_unblock", { p_key: str(params, "key") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      // 고객센터. 첨부 그림은 비공개 저장소라 볼 때마다 1시간짜리 서명 URL 을 만든다.
      case "support.list": {
        const status = optStr(params, "status");
        if (status && !["open", "answered", "closed"].includes(status)) throw new Error("상태 값이 올바르지 않습니다.");
        const categoryId = params.categoryId == null ? null : intInRange(params, "categoryId", "분류", 1, 2147483647);
        // 플레이어로 거를 때는 전체 ID 만 받는다 — 검색 함수는 일부도 찾아서 다른 사람 문의가 섞인다.
        const accountId = optStr(params, "accountId").trim();
        if (accountId && !UUID_RE.test(accountId)) return json({ ok: true, data: { rows: [], total: 0 } });
        const { data, error } = await db.rpc("ts_admin_support_tickets", {
          p_status: status || null,
          p_category: categoryId,
          p_search: accountId || optStr(params, "search") || null,
          p_limit: intInRange(params, "limit", "개수", 1, 200),
          p_offset: intInRange(params, "offset", "시작 위치", 0, 1_000_000),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "support.get": {
        const { data, error } = await db.rpc("ts_admin_support_ticket", { p_ticket: str(params, "ticketId") });
        if (error) throw new Error(error.message);
        if (!data) throw new Error("문의를 찾을 수 없습니다.");
        const ticket = data as { messages?: Array<{ attachments?: string[] }> };
        const paths = (ticket.messages ?? []).flatMap((m) => m.attachments ?? []);
        const urls = new Map<string, string>();
        if (paths.length > 0) {
          const { data: signed } = await db.storage.from("support-attachments").createSignedUrls(paths, 3600);
          for (const s of signed ?? []) if (s.path && s.signedUrl) urls.set(s.path, s.signedUrl);
        }
        return json({
          ok: true,
          data: {
            ...ticket,
            messages: (ticket.messages ?? []).map((m) => ({
              ...m,
              attachments: (m.attachments ?? []).map((path) => ({ path, url: urls.get(path) ?? null })),
            })),
          },
        });
      }

      case "support.reply": {
        const body = str(params, "body").trim();
        if (!body) throw new Error("답변을 입력해 주세요.");
        if (body.length > 2000) throw new Error("답변은 2000자까지 쓸 수 있습니다.");
        const { error } = await db.rpc("ts_admin_support_reply", { p_ticket: str(params, "ticketId"), p_body: body, p_by: email });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "support.setStatus": {
        const status = str(params, "status");
        if (!["open", "answered", "closed"].includes(status)) throw new Error("상태 값이 올바르지 않습니다.");
        const { error } = await db.rpc("ts_admin_support_set_status", { p_ticket: str(params, "ticketId"), p_status: status });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "supportFaqs.list": {
        const { data, error } = await db.rpc("ts_admin_support_faqs");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "supportFaqs.upsert": {
        const question = str(params, "question").trim();
        const answer = str(params, "answer").trim();
        if (question.length < 1 || question.length > 200) throw new Error("질문은 1~200자로 넣어 주세요.");
        if (answer.length < 1 || answer.length > 4000) throw new Error("답은 1~4000자로 넣어 주세요.");
        // 언어별 문구 {"en": {"question", "answer"}} — 둘 다 비면 그 언어를 뺀다.
        const localized: Record<string, { question: string; answer: string }> = {};
        const raw = params.localized;
        if (raw && typeof raw === "object" && !Array.isArray(raw)) {
          for (const [k, v] of Object.entries(raw as Record<string, unknown>)) {
            if (!/^[a-z]{2}$/.test(k)) throw new Error("언어 코드는 영문 소문자 두 글자로 넣어 주세요.");
            const q = typeof (v as Record<string, unknown>)?.question === "string" ? ((v as Record<string, string>).question).trim().slice(0, 200) : "";
            const a = typeof (v as Record<string, unknown>)?.answer === "string" ? ((v as Record<string, string>).answer).trim().slice(0, 4000) : "";
            // 한쪽만 있으면 그 언어 질문에 한국어 답이 붙는다 — 둘 다 넣거나 둘 다 비운다.
            if (!!q !== !!a) throw new Error("영어 질문과 영어 답은 함께 넣거나 함께 비워 주세요.");
            if (q && a) localized[k] = { question: q, answer: a };
          }
        }
        const { error } = await db.rpc("ts_admin_support_faq_upsert", {
          p_id: params.id == null ? null : intInRange(params, "id", "질문", 1, 2147483647),
          p_category: params.categoryId == null ? null : intInRange(params, "categoryId", "분류", 1, 2147483647),
          p_question: question,
          p_answer: answer,
          p_localized: localized,
          p_sort_order: params.sortOrder == null ? null : intInRange(params, "sortOrder", "순서", 0, 10000),
          p_is_active: typeof params.isActive === "boolean" ? params.isActive : null,
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      // 켜기·끄기만 바꾼다 — 화면이 불러 둔 옛 질문·답을 함께 보내면 다른 운영자의 수정을 덮는다.
      case "supportFaqs.setActive": {
        if (typeof params.isActive !== "boolean") throw new Error("사용 여부가 없습니다.");
        const { error } = await db.rpc("ts_admin_support_faq_set_active", {
          p_id: intInRange(params, "id", "질문", 1, 2147483647), p_is_active: params.isActive, p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "supportCategories.setActive": {
        if (typeof params.isActive !== "boolean") throw new Error("사용 여부가 없습니다.");
        const { error } = await db.rpc("ts_admin_support_category_set_active", {
          p_id: intInRange(params, "id", "분류", 1, 2147483647), p_is_active: params.isActive,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "supportFaqs.reorder": {
        const ids = params.ids;
        if (!Array.isArray(ids) || ids.length === 0 || ids.length > 500 || !ids.every((v) => Number.isInteger(v) && (v as number) > 0)) {
          throw new Error("순서 목록이 올바르지 않습니다.");
        }
        const { error } = await db.rpc("ts_admin_support_faq_reorder", { p_ids: ids, p_by: email });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "supportFaqs.delete": {
        const { error } = await db.rpc("ts_admin_support_faq_delete", { p_id: intInRange(params, "id", "질문", 1, 2147483647) });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "supportCategories.list": {
        const { data, error } = await db.rpc("ts_admin_support_categories");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "supportCategories.upsert": {
        const name = str(params, "name").trim();
        if (name.length < 1 || name.length > 40) throw new Error("분류 이름은 1~40자로 넣어 주세요.");
        const localized: Record<string, string> = {};
        const raw = params.localized;
        if (raw && typeof raw === "object" && !Array.isArray(raw)) {
          for (const [k, v] of Object.entries(raw as Record<string, unknown>)) {
            if (!/^[a-z]{2}$/.test(k)) throw new Error("언어 코드는 영문 소문자 두 글자로 넣어 주세요.");
            if (typeof v === "string" && v.trim()) localized[k] = v.trim().slice(0, 40);
          }
        }
        const { error } = await db.rpc("ts_admin_support_category_upsert", {
          p_id: params.id == null ? null : intInRange(params, "id", "분류", 1, 2147483647),
          p_name: name,
          p_localized: localized,
          p_sort_order: params.sortOrder == null ? null : intInRange(params, "sortOrder", "순서", 0, 10000),
          p_is_active: typeof params.isActive === "boolean" ? params.isActive : null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "friends.settingsGet": {
        const { data, error } = await db.rpc("ts_admin_friend_settings_get");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "friends.settingsUpdate": {
        // 범위는 ts_admin_friend_settings_update 와 같게 둔다 — 간격에 3600 을 넣으면
        // 최근 1시간 안에 요청한 유저가 전부 차단된다.
        const { data, error } = await db.rpc("ts_admin_friend_settings_update", {
          p_max_friends: intInRange(params, "maxFriends", "친구 수 상한", 1, 1000),
          p_max_pending_sent: intInRange(params, "maxPendingSent", "보낸 요청 대기 상한", 1, 500),
          p_request_cooldown_seconds: intInRange(params, "requestCooldownSeconds", "연속 요청 최소 간격", 0, 300),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "matchLobby.settingsGet": {
        const { data, error } = await db.rpc("ts_admin_match_lobby_settings_get");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "matchLobby.settingsUpdate": {
        // 범위는 ts_admin_match_lobby_settings_update 와 같게 둔다.
        const { data, error } = await db.rpc("ts_admin_match_lobby_settings_update", {
          p_max_pending_invites_received:
            intInRange(params, "maxPendingInvitesReceived", "받는 쪽 대기 초대 상한", 1, 200),
          p_invite_cooldown_seconds:
            intInRange(params, "inviteCooldownSeconds", "연속 초대 최소 간격", 0, 300),
          p_lobby_expire_minutes:
            intInRange(params, "lobbyExpireMinutes", "방이 닫히기까지의 시간", 1, 1440),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "matchLobby.list": {
        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 20;
        const status = optStr(params, "status").trim();

        const { data, error } = await db.rpc("ts_admin_match_lobby_list", {
          p_status: status === "" ? null : status,
          p_limit: pageSize,
          p_offset: (page - 1) * pageSize,
        });
        if (error) throw new Error(error.message);

        const result = (data ?? {}) as { rows?: unknown[]; total?: number };
        return json({ ok: true, data: { rows: result.rows ?? [], total: result.total ?? 0, pageSize } });
      }

      case "matchRewards.list": {
        const { data, error } = await db.rpc("ts_admin_match_reward_overview");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "matchRewards.upsert": {
        // 매치는 기록 전용이다 — 게임 코드와 결과 확인 방식만 받는다. 보상 칸은 비워 저장한다(서버가 보상을 보내지 않는다).
        const gameCode = str(params, "gameCode").trim();
        if (!/^[a-z0-9_]{1,64}$/.test(gameCode)) {
          throw new Error("게임 코드는 영문 소문자·숫자·밑줄로 64자 이내여야 합니다.");
        }
        const mode = str(params, "mode");
        if (mode !== "competitive" && mode !== "coop") throw new Error("판정 방식이 올바르지 않습니다.");
        const { error } = await db.rpc("ts_admin_match_reward_config_upsert", {
          p_game_code: gameCode,
          p_mode: mode,
          p_win_items: [],
          p_create_only: params["createOnly"] === true,
          p_by: email,
        });
        if (error) {
          if (error.message.startsWith("match_reward_config_exists")) {
            throw new Error("이미 등록된 게임 코드입니다. 목록에서 그 코드를 수정하세요.");
          }
          throw new Error(error.message);
        }
        return json({ ok: true, data: null });
      }

      case "matchRewards.delete": {
        const { data, error } = await db.rpc("ts_admin_match_reward_config_delete", {
          p_game_code: str(params, "gameCode"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "matchRecords.player": {
        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 20;
        const gameCode = optStr(params, "gameCode").trim();

        const { data, error } = await db.rpc("ts_admin_match_player_records", {
          p_account_id: str(params, "accountId"),
          p_game_code: gameCode === "" ? null : gameCode,
          p_limit: pageSize,
          p_offset: (page - 1) * pageSize,
        });
        if (error) throw new Error(error.message);

        const result = (data ?? {}) as { summary?: unknown[]; rows?: unknown[]; total?: number };
        return json({
          ok: true,
          data: { summary: result.summary ?? [], rows: result.rows ?? [], total: result.total ?? 0, pageSize },
        });
      }

      case "coupons.list": {
        const { data, error } = await db.rpc("ts_admin_coupon_list", { p_search: optStr(params, "search") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "coupons.codes": {
        // all: CSV 내려받기 — 화면 목록(500개)과 달리 전부 준다. 상한은 일반 쿠폰 1회 발급량을 넉넉히 넘게 잡았다.
        const all = bool(params, "all");
        const { data, error } = await db.rpc("ts_admin_coupon_codes", {
          p_coupon_id: str(params, "id"),
          p_limit: all ? 200_000 : 500,
        });
        if (error) throw new Error(error.message);
        // 내려받기는 쓴 계정이 수십만일 수 있어 닉네임을 붙이지 않는다(그 파일은 ID 칸으로 본다).
        return json({ ok: true, data: all ? data ?? [] : await withDisplayNames(db, (data ?? []) as Array<{ account_id?: unknown }>) });
      }

      case "coupons.create": {
        const { data, error } = await db.rpc("ts_admin_coupon_create", {
          p_kind: str(params, "kind"),
          p_title: str(params, "title"),
          p_content: optStr(params, "content"),
          p_category: optStr(params, "category") || "default",
          p_items: params["items"] ?? null,
          p_localized: null,
          p_expires_at: params["expiresAt"] ?? null,
          p_mail_expires_days: typeof params["mailExpiresDays"] === "number" ? params["mailExpiresDays"] : 7,
          p_is_active: bool(params, "isActive"),
          p_code: optStr(params, "code") || null,
          p_max_uses: typeof params["maxUses"] === "number" ? params["maxUses"] : null,
          p_prefix: optStr(params, "prefix"),
          p_random_len: typeof params["randomLen"] === "number" ? params["randomLen"] : 6,
          p_quantity: typeof params["quantity"] === "number" ? params["quantity"] : 1,
          p_created_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "coupons.update": {
        const { error } = await db.rpc("ts_admin_coupon_update", {
          p_id: str(params, "id"),
          p_title: str(params, "title"),
          p_content: optStr(params, "content"),
          p_category: optStr(params, "category") || "default",
          p_items: params["items"] ?? null,
          p_localized: null,
          p_expires_at: params["expiresAt"] ?? null,
          p_mail_expires_days: typeof params["mailExpiresDays"] === "number" ? params["mailExpiresDays"] : 7,
          p_max_uses: typeof params["maxUses"] === "number" ? params["maxUses"] : null,
          p_is_active: bool(params, "isActive"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "coupons.delete": {
        const { error } = await db.rpc("ts_admin_coupon_delete", { p_id: str(params, "id") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "mails.getServers": {
        const { data, error } = await db.from("game_servers").select("id, server_code, display_name").order("display_name");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "mails.getCategories": {
        const { data, error } = await db.from("mail_categories").select("key, display_name, created_at").order("sort_order");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "mails.upsertCategory": {
        const { error } = await db
          .from("mail_categories")
          .upsert({ key: str(params, "key"), display_name: optStr(params, "displayName") }, { onConflict: "key" });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "mails.deleteCategory": {
        const { error } = await db.from("mail_categories").delete().eq("key", str(params, "key"));
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "mails.send": {
        if (optStr(params, "platform")) {
          throw new Error("우편 플랫폼 지정이 없어졌습니다. 화면을 새로고침한 뒤 다시 보내 주세요.");
        }
        const { data, error } = await db.rpc("ts_admin_send_mail", {
          p_target_mode: str(params, "mode"),
          p_title: str(params, "title"),
          p_expires_at: str(params, "expiresAt"),
          p_account_ids: params["accountIds"] ?? null,
          p_server_id: params["serverId"] ?? null,
          p_content: optStr(params, "content"),
          p_items: params["items"] ?? null,
          // 보낸 사람은 화면이 넘기는 값이 아니라 로그인한 운영자로 남긴다 — 다른 변경 기록과 같은 기준.
          p_created_by: email,
          p_skip_item_validation: false,
          p_category: optStr(params, "category") || "default",
          p_localized: params["localized"] ?? null,
          p_open_for_new: params["openForNew"] === true,
        });
        if (error) throw new Error(error.message);
        // "푸시도 함께" — 우편은 이미 나갔다. 푸시를 못 넣어도 발송을 실패로 돌리지 않고 결과에 넣은 수만 붙인다.
        if (params["push"] === true && data?.batch_id) {
          const { data: pushed } = await db.rpc("ts_push_enqueue_mail_batch", {
            p_batch_id: data.batch_id,
            p_is_ad: params["pushIsAd"] === true,
          });
          return json({ ok: true, data: { ...data, push_count: typeof pushed === "number" ? pushed : -1 } });
        }
        return json({ ok: true, data });
      }

      case "mails.createSchedule": {
        if (optStr(params, "platform")) {
          throw new Error("우편 플랫폼 지정이 없어졌습니다. 화면을 새로고침한 뒤 다시 보내 주세요.");
        }
        const { data, error } = await db.rpc("ts_admin_create_mail_schedule", {
          p_schedule_type: str(params, "scheduleType"),
          p_target_mode: str(params, "mode"),
          p_title: str(params, "title"),
          p_expires_days: typeof params["expiresDays"] === "number" ? params["expiresDays"] : 7,
          p_account_ids: params["accountIds"] ?? null,
          p_server_id: params["serverId"] ?? null,
          p_content: optStr(params, "content"),
          p_items: params["items"] ?? null,
          p_localized: params["localized"] ?? null,
          p_category: optStr(params, "category") || "default",
          p_scheduled_at: params["scheduledAt"] ?? null,
          p_repeat_time: params["repeatTime"] ?? null,
          p_repeat_unit: optStr(params, "repeatUnit") || "day",
          p_repeat_dow: params["repeatDow"] ?? null,
          p_repeat_dom: params["repeatDom"] ?? null,
          p_created_by: email,
          p_open_for_new: params["openForNew"] === true,
        });
        if (error) throw new Error(error.message);
        if (params["push"] === true && typeof data === "string") {
          const { error: pushError } = await db.rpc("ts_admin_mail_schedule_set_push", {
            p_id: data,
            p_push: true,
            p_is_ad: params["pushIsAd"] === true,
          });
          if (pushError) throw new Error(`예약은 만들었지만 푸시를 붙이지 못했습니다: ${pushError.message}`);
        }
        return json({ ok: true, data });
      }

      case "push.get": {
        const { data, error } = await db.rpc("ts_admin_push_settings");
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "push.updateTrigger": {
        const localized = params["localized"];
        if (localized !== undefined && localized !== null && (typeof localized !== "object" || Array.isArray(localized))) {
          throw new Error("언어별 문구 형식이 맞지 않습니다.");
        }
        const { error } = await db.rpc("ts_admin_push_trigger_update", {
          p_code: str(params, "code"),
          p_title: typeof params["title"] === "string" ? params["title"] : null,
          p_body: typeof params["body"] === "string" ? params["body"] : null,
          p_localized: localized ?? null,
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "push.updateAd": {
        const localized = params["localized"];
        if (localized !== undefined && localized !== null && (typeof localized !== "object" || Array.isArray(localized))) {
          throw new Error("언어별 문구 형식이 맞지 않습니다.");
        }
        const { error } = await db.rpc("ts_admin_push_ad_settings_update", {
          p_title_prefix: typeof params["titlePrefix"] === "string" ? params["titlePrefix"] : null,
          p_footer: typeof params["footer"] === "string" ? params["footer"] : null,
          p_localized: localized ?? null,
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "push.stats": {
        const { data, error } = await db.rpc("ts_admin_push_stats", {
          p_days: typeof params["days"] === "number" ? params["days"] : 7,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.getBatches": {
        const { data, error } = await db.rpc("ts_admin_list_mail_batches", {
          p_search: optStr(params, "search"),
          p_category: optStr(params, "category"),
          p_page: typeof params["page"] === "number" ? params["page"] : 1,
          p_page_size: 20,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.getBatchDetail": {
        const { data, error } = await db.rpc("ts_admin_mail_batch_detail", {
          p_batch_id: str(params, "batchId"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.recallBatch": {
        const { data, error } = await db.rpc("ts_admin_mail_batch_recall", { p_batch_id: str(params, "batchId") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { count: data ?? 0 } });
      }

      case "mails.closeBatch": {
        const { error } = await db.rpc("ts_admin_mail_batch_close", { p_batch_id: str(params, "batchId") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "mails.getRecords": {
        const { data, error } = await db.rpc("ts_admin_search_mails", {
          p_search: optStr(params, "search"),
          p_status: optStr(params, "status") || null,
          p_category: optStr(params, "category"),
          p_start_date: params["startDate"] ?? null,
          p_end_date: params["endDate"] ?? null,
          p_batch_id: params["batchId"] ?? null,
          p_page: typeof params["page"] === "number" ? params["page"] : 1,
          p_page_size: 20,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.getSchedules": {
        const { data, error } = await db.rpc("ts_admin_list_mail_schedules");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "mails.setScheduleActive": {
        const { error } = await db.rpc("ts_admin_set_mail_schedule_active", {
          p_id: str(params, "id"),
          p_is_active: bool(params, "isActive"),
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "mails.deleteSchedule": {
        const { error } = await db.rpc("ts_admin_delete_mail_schedule", { p_id: str(params, "id") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "schema.getDraft": {
        const { data, error } = await db
          .from("ts_schema_draft")
          .select("id, created_at, operator, feature, action, object_name, params, sort_order")
          .eq("status", "pending")
          .order("sort_order");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "schema.getVersions": {
        const { data, error } = await db
          .from("ts_schema_version")
          .select("id, published_at, operator, label, ops, reversible, status, reverted_at, reverted_by")
          .order("id", { ascending: false });
        if (error) throw new Error(error.message);
        const rows = (data ?? []).map((v) => ({ ...v, op_count: Array.isArray(v.ops) ? v.ops.length : 0 }));
        return json({ ok: true, data: { rows } });
      }

      case "schema.discardDraft": {
        const id = params["id"];
        const query = db.from("ts_schema_draft").delete();
        const { error } = typeof id === "number" ? await query.eq("id", id) : await query.eq("status", "pending");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "schema.publish": {
        const { data, error } = await db.rpc("ts_admin_schema_publish", {
          p_operator: email,
          p_label: params["label"] ?? null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "schema.revertVersion": {
        const { data, error } = await db.rpc("ts_admin_schema_revert", {
          p_version_id: params["versionId"],
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "schema.stage": {
        const { data, error } = await db.rpc("ts_admin_schema_stage", {
          p_feature: str(params, "feature"),
          p_action: str(params, "action"),
          p_object_name: str(params, "objectName"),
          p_params: params["params"] ?? {},
          p_operator: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "leaderboard.list": {
        const { data, error } = await db.rpc("ts_admin_list_leaderboards");
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [] } });
      }

      case "leaderboard.columns": {
        const { data, error } = await db.rpc("ts_admin_leaderboard_columns", { p_code: str(params, "code") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "leaderboard.scores": {
        const { data, error } = await db.rpc("ts_admin_leaderboard_scores", {
          p_code: str(params, "code"),
          p_rotation_count: params["rotationCount"] ?? null,
          p_server_id: params["serverId"] ?? null,
          p_search: optStr(params, "search"),
          p_page: typeof params["page"] === "number" ? params["page"] : 1,
          p_page_size: 20,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "leaderboard.rotate": {
        const { data, error } = await db.rpc("ts_admin_leaderboard_rotate", { p_code: str(params, "code") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "leaderboard.setScore": {
        const { error } = await db.rpc("ts_admin_leaderboard_set_score", {
          p_code: str(params, "code"),
          p_account_id: str(params, "accountId"),
          p_score: params["score"],
          p_rotation_count: params["rotationCount"] ?? null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "leaderboard.deleteScore": {
        const { error } = await db.rpc("ts_admin_leaderboard_delete_score", {
          p_code: str(params, "code"),
          p_account_id: str(params, "accountId"),
          p_rotation_count: params["rotationCount"] ?? null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "leaderboard.setPlayerData": {
        const { error } = await db.rpc("ts_admin_leaderboard_set_player_data", {
          p_code: str(params, "code"),
          p_account_id: str(params, "accountId"),
          p_data: params["data"] ?? null,
          p_rotation_count: params["rotationCount"] ?? null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      default:
        return fail(`알 수 없는 action: ${action}`, 400);
    }
  } catch (e) {
    // 운영자에게는 사유를 그대로 보여준다 — 이 함수에 닿은 시점에 이미 인가된 사람이다.
    console.error(`[admin-api] ${action} 실패 (${email}): ${e}`);
    return fail(e instanceof Error ? friendlyError(e.message) : "처리에 실패했습니다.", 400);
  }
}

// DB 함수가 raise 하는 사유 코드를 운영자 문구로. 표에 없는 사유는 원문 그대로 둔다 — 지우면 원인을 잃는다.
const SERVER_ERRORS: Record<string, string> = {
  support_body_empty: "답변을 입력하세요.",
  support_body_too_long: "답변은 2,000자까지 쓸 수 있습니다.",
  support_ticket_not_found: "문의를 찾을 수 없습니다.",
  support_ticket_closed: "종료된 문의입니다. 다시 연 뒤 답하세요.",
  support_status_invalid: "상태 값이 올바르지 않습니다.",
  support_category_name_invalid: "분류 이름은 1~40자로 넣어 주세요.",
  support_category_name_taken: "같은 이름의 분류가 이미 있습니다.",
  support_category_not_found: "분류를 찾을 수 없습니다.",
  support_faq_question_invalid: "질문은 1~200자로 넣어 주세요.",
  support_faq_answer_invalid: "답은 1~4000자로 넣어 주세요.",
  support_faq_not_found: "질문을 찾을 수 없습니다.",
  server_display_name_empty: "서버 이름을 입력하세요.",
  server_code_invalid: "서버 코드는 영문·숫자·_·- 로 32자 이내여야 합니다.",
  server_code_taken: "이미 있는 서버 코드입니다.",
  server_not_found: "서버를 찾을 수 없습니다.",
  chat_empty: "보낼 내용을 입력하세요.",
  chat_too_long: "공지는 1,000자까지 보낼 수 있습니다.",
  chat_channel_not_found: "채널을 찾을 수 없습니다.",
  chat_kind_unsupported: "공지는 전체·서버 채널에만 보낼 수 있습니다.",
  coupon_code_taken: "이미 있는 쿠폰 코드입니다.",
  coupon_code_required: "쿠폰 코드를 입력하세요.",
  invalid_coupon_code: "쿠폰 코드는 영문 대문자·숫자·_·- 만 쓸 수 있습니다.",
  invalid_coupon_prefix: "접두어는 영문 대문자·숫자·_·- 만 쓸 수 있습니다.",
  coupon_code_space_exhausted: "이 길이로는 더 만들 수 있는 코드가 없습니다. 코드 길이를 늘리세요.",
  coupon_not_found: "쿠폰을 찾을 수 없습니다. 이미 삭제됐을 수 있습니다.",
  unknown_item_key: "아이템 카탈로그에 없는 아이템이 들어 있습니다",
  reserved_item_key: "이 아이템 코드는 쓸 수 없습니다",
  item_key_empty: "아이템을 고르세요.",
  item_count_invalid: "아이템 수량이 올바르지 않습니다.",
  invalid_mail_expires_days: "수령 기한(일)이 올바르지 않습니다.",
  mail_batch_not_found: "발송 내역을 찾을 수 없습니다.",
  leaderboard_score_not_found: "기록을 찾을 수 없습니다. 이미 지워졌을 수 있습니다.",
  leaderboard_column_not_allowed: "이 리더보드에서 쓰지 않는 필드입니다",
  leaderboard_invalid_code: "리더보드 코드는 영문 소문자로 시작하고 영문 소문자·숫자·_ 만 쓸 수 있습니다.",
  leaderboard_code_required: "리더보드 코드를 입력하세요.",
  leaderboard_rotation_period_required: "초기화 주기를 입력하세요.",
  leaderboard_invalid_rotation: "초기화 설정이 올바르지 않습니다.",
  leaderboard_rotation_not_found: "해당 회차를 찾을 수 없습니다.",
  leaderboard_rotation_closed: "이미 끝난 회차입니다.",
  leaderboard_ended: "이미 종료된 리더보드입니다.",
  remote_config_key_not_found: "원격 설정을 찾을 수 없습니다.",
  remote_config_key_exists: "이미 있는 키입니다. 목록에서 그 설정을 골라 필드를 추가하세요.",
  leaderboard_code_exists: "이미 있는 리더보드 코드입니다.",
  field_name_exists: "이미 있거나 추가 대기 중인 필드명입니다.",
  field_name_reserved: "기본으로 쓰는 이름이라 필드명으로 쓸 수 없습니다.",
  field_name_invalid: "필드명은 영문자나 _ 로 시작하고 영문·숫자·_ 만 쓸 수 있습니다.",
  chat_mute_not_found: "이미 해제된 채팅 차단입니다.",
  user_data_not_found: "이 플레이어는 저장된 데이터가 없습니다.",
  user_data_log_not_found: "이 변경 기록을 찾을 수 없습니다. 7일이 지나 지워졌을 수 있습니다.",
  patch_required: "바꿀 값이 없습니다.",
  schema_draft_empty: "게시할 변경이 없습니다.",
  schema_version_not_found: "해당 버전을 찾을 수 없습니다.",
  schema_version_already_reverted: "이미 되돌린 버전입니다.",
  schema_version_not_reversible: "이 버전은 되돌릴 수 없습니다.",
  match_reward_config_exists: "이미 등록된 게임 코드입니다.",
  match_reward_config_not_found: "매치 보상 설정을 찾을 수 없습니다.",
  match_game_code_invalid: "게임 코드 형식이 올바르지 않습니다.",
  operator_email_invalid: "이메일 형식이 올바르지 않습니다.",
  operator_not_found: "운영자를 찾을 수 없습니다.",
  operator_not_disabled: "먼저 비활성화한 운영자만 삭제할 수 있습니다.",
  game_time_zone_invalid: "알 수 없는 시간대입니다.",
  mail_settings_out_of_range: "허용 범위를 벗어난 값이 있습니다.",
  friend_settings_out_of_range: "허용 범위를 벗어난 값이 있습니다.",
  match_lobby_settings_out_of_range: "허용 범위를 벗어난 값이 있습니다.",
  console_name_required: "프로젝트 이름을 적어 주세요.",
  console_name_too_long: "프로젝트 이름은 40자 이내여야 합니다.",
  console_icon_invalid: "아이콘은 PNG·JPG·WebP 그림이어야 하고 너무 크면 안 됩니다.",
};

/** "code" 또는 "code: 상세" 꼴의 DB 사유를 운영자 문구로. 상세(대개 문제된 값)는 괄호로 붙인다. */
function friendlyError(message: string): string {
  const m = /^([a-z][a-z0-9_]*)(?::\s*(.+))?$/s.exec(message.trim());
  if (!m) return message;
  const text = SERVER_ERRORS[m[1]];
  if (!text) return message;
  // "abc (must match ^…$)" 처럼 개발자용 설명이 붙은 상세는 값만 남긴다.
  const detail = m[2]?.replace(/\s*\(must match .*\)$/, "").trim();
  return detail ? `${text.replace(/[.]$/, "")} (${detail})` : text;
}
