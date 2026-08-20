// 운영 도구 백엔드.
//
// 운영자 인증은 게임 Auth 를 쓰지 않는다. 구글 ID 토큰이나, 아래 비밀번호 로그인으로 발급한
// 자체 세션 토큰을 여기서 직접 검증한다. 그래서 운영자는 auth.users 에 존재하지 않는다 —
// 플레이어와 계정 풀이 겹치지 않고, 게임의 탈퇴·차단 흐름이 운영자 접근에 영향을 주지 않는다.
//
// 로그인 방법은 둘이다.
//   구글 로그인    — 구글 계정이 있는 운영자용. 매번 구글이 신원을 증명한다.
//   비밀번호 로그인 — 구글 계정이 없어도 등록된 이메일이면 쓸 수 있다. 비밀번호는 이메일을
//                   거치지 않고 마스터가 "운영자 관리" 화면에서 직접 정해서 운영자에게 알려준다
//                   (이메일 발송 인프라가 따로 필요 없다).
//
// 권한은 두 단계다.
//   마스터  — ADMIN_EMAILS 환경변수. 화면에서 지울 수 없어 잠기지 않는 비상구. 운영자 관리 가능.
//   운영자  — ts_admin_operators 표. 마스터가 화면에서 추가·비활성화·비밀번호 설정한다.
//
// 필요한 시크릿:
//   GOOGLE_CLIENT_ID, ADMIN_EMAILS(쉼표 구분) — 기존과 동일.
//   ADMIN_SESSION_SECRET — 비밀번호 로그인 세션 토큰 서명용. 긴 무작위 문자열.
//   SUPABASE_* 는 플랫폼이 자동 주입한다.
//
// verify_jwt 는 false 로 배포한다. 게이트웨이의 JWT 검증은 게임 Auth 기준이라 구글 토큰을
// 통과시키지 못하고, CORS 프리플라이트(Authorization 없음)까지 막는다. 검증은 아래가 직접 한다.
import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";
import { createRemoteJWKSet, jwtVerify, SignJWT } from "npm:jose@5";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

const GOOGLE_CLIENT_ID = Deno.env.get("GOOGLE_CLIENT_ID") ?? "";
const GOOGLE_JWKS = createRemoteJWKSet(new URL("https://www.googleapis.com/oauth2/v3/certs"));

// 비밀번호 로그인 세션 토큰 서명 키. 구글 토큰과 형태(JWT)를 맞춰서 프런트의
// 만료 판단 로직을 그대로 재사용한다 — 다만 서명 알고리즘이 달라(HS256 vs RS256)
// verifyIdentity() 가 시도 순서로 둘을 구분한다.
const SESSION_SECRET = Deno.env.get("ADMIN_SESSION_SECRET") ?? "";
const SESSION_KEY = SESSION_SECRET ? new TextEncoder().encode(SESSION_SECRET) : null;
const SESSION_TTL = "12h";

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

// `,()` 는 PostgREST or() 필터 문법을 깨서 제거하고, `%`·`_`·`\` 는 ILIKE 와일드카드라
// 리터럴로 취급되도록 백슬래시로 이스케이프한다 — 안 하면 "order_123" 검색에 "orderX123"까지 걸린다.
const sanitizeSearchTerm = (s: string): string => s.replace(/[,()]/g, "").replace(/[\\%_]/g, (c) => `\\${c}`);

// endDate 는 'YYYY-MM-DD' 뿐이라 그대로 lte 하면 그날 00:00:00 까지만 걸려 당일 데이터가
// 거의 다 빠진다 — 다음날 자정 미만(<)으로 바꿔 그날 전체를 포함한다.
const nextDayExclusive = (dateStr: string): string | null => {
  const [y, m, d] = dateStr.split("-").map(Number);
  if (!y || !m || !d) return null;
  return new Date(Date.UTC(y, m - 1, d + 1)).toISOString().slice(0, 10);
};

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

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
    // 실패 로그는 여기서 안 남긴다 — 비밀번호 로그인 세션 토큰(HS256)도 항상 이 경로를
    // 먼저 타서 매번 "실패"하므로, 여기서 찍으면 정상적인 비밀번호 로그인마다 가짜 에러가
    // 쌓인다. 진짜 실패(둘 다 아님)는 verifyIdentity 에서 한 번만 남긴다.
    return null;
  }
}

/** 표에 등록되고 비활성화되지 않은 운영자인지. */
async function isEnabledOperator(db: SupabaseClient, email: string): Promise<boolean> {
  const { data, error } = await db.rpc("ts_admin_list_operators");
  if (error) {
    console.error(`[admin-api] 운영자 목록 조회 실패: ${error.message}`);
    return false;
  }
  const rows = (data ?? []) as Array<{ email?: unknown; disabled_at?: unknown }>;
  return rows.some((r) => r.email === email && r.disabled_at == null);
}

/** 마스터이거나, 표에 등록되고 비활성화되지 않은 운영자인지. 비밀번호 로그인·재설정에서 공용으로 쓴다. */
async function isAllowedIdentity(db: SupabaseClient, email: string): Promise<boolean> {
  return MASTER_EMAILS.has(email) || (await isEnabledOperator(db, email));
}

/** 비밀번호 로그인 세션 토큰을 발급한다. 구글 ID 토큰과 같은 JWT 형태라 프런트가 만료를 그대로 읽는다. */
async function issueSessionToken(email: string): Promise<string> {
  if (!SESSION_KEY) throw new Error("ADMIN_SESSION_SECRET 가 설정되지 않았습니다.");
  return await new SignJWT({ email })
    .setProtectedHeader({ alg: "HS256" })
    .setIssuedAt()
    .setExpirationTime(SESSION_TTL)
    .sign(SESSION_KEY);
}

/** 비밀번호 로그인 세션 토큰을 검증한다. 실패하면 null. */
async function verifySessionToken(token: string): Promise<string | null> {
  if (!SESSION_KEY) return null;
  try {
    const { payload } = await jwtVerify(token, SESSION_KEY);
    const email = payload["email"];
    return typeof email === "string" ? email.toLowerCase() : null;
  } catch {
    return null;
  }
}

/** 구글 ID 토큰이거나 우리가 발급한 세션 토큰이거나 — 신원을 확인하고 이메일을 돌려준다. */
async function verifyIdentity(token: string): Promise<string | null> {
  const google = await verifyGoogleIdToken(token);
  if (google) return google;
  const session = await verifySessionToken(token);
  if (session) return session;
  // 둘 다 아니면 그때만 로그를 남긴다 — 어느 한쪽만 실패하는 건 정상(로그인 방식이 둘이라
  // 그렇다) 이라 노이즈가 안 되고, 진짜 문제만 여기 걸린다.
  console.error("[admin-api] 토큰 검증 실패 — 구글 ID 토큰도 비밀번호 로그인 세션도 아닙니다.");
  return null;
}

// ── 비밀번호 해시 (Web Crypto PBKDF2-SHA256, 외부 의존성 없음) ──────────────────────
const PBKDF2_ITERATIONS = 100_000;

function toBase64(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes));
}
function fromBase64(b64: string): Uint8Array {
  return Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
}

async function pbkdf2(secret: string, salt: Uint8Array, iterations: number): Promise<Uint8Array> {
  const keyMaterial = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), "PBKDF2", false, [
    "deriveBits",
  ]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", salt, iterations, hash: "SHA-256" },
    keyMaterial,
    256,
  );
  return new Uint8Array(bits);
}

/** "pbkdf2$반복수$salt$hash" 형태로 저장한다. 원문은 어디에도 남지 않는다. */
async function hashSecret(secret: string): Promise<string> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const hash = await pbkdf2(secret, salt, PBKDF2_ITERATIONS);
  return `pbkdf2$${PBKDF2_ITERATIONS}$${toBase64(salt)}$${toBase64(hash)}`;
}

/** 저장된 해시와 대조한다. 형식이 깨졌으면 false(예외를 던지지 않는다). */
async function verifySecretHash(secret: string, stored: string): Promise<boolean> {
  const parts = stored.split("$");
  if (parts.length !== 4 || parts[0] !== "pbkdf2") return false;
  const iterations = Number(parts[1]);
  if (!Number.isFinite(iterations) || iterations <= 0) return false;
  try {
    const salt = fromBase64(parts[2]);
    const expected = fromBase64(parts[3]);
    const actual = await pbkdf2(secret, salt, iterations);
    if (actual.length !== expected.length) return false;
    // 타이밍 공격을 막으려 길이가 같을 때만 상수시간으로 비교한다.
    let diff = 0;
    for (let i = 0; i < actual.length; i++) diff |= actual[i] ^ expected[i];
    return diff === 0;
  } catch {
    return false;
  }
}

const MIN_PASSWORD_LENGTH = 8;

Deno.serve(async (req) => {
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

  // ── 2. 로그인 전 공개 액션(비밀번호 로그인·재설정) ────────────────────────────────
  // 아직 세션이 없는 상태에서만 의미가 있으므로 아래 신원 확인보다 먼저 처리하고 끝낸다.
  if (action.startsWith("auth.")) {
    try {
      switch (action) {
        case "auth.passwordLogin": {
          const loginEmail = str(params, "email").toLowerCase();
          const password = str(params, "password");
          const { data: row, error } = await db
            .from("ts_admin_passwords")
            .select("password_hash")
            .eq("email", loginEmail)
            .maybeSingle();
          if (error) throw new Error(error.message);
          if (!row || !(await verifySecretHash(password, row.password_hash))) {
            return fail("이메일 또는 비밀번호가 올바르지 않습니다.", 401);
          }
          if (!(await isAllowedIdentity(db, loginEmail))) {
            return fail("운영자 권한이 없습니다.", 403);
          }
          const token = await issueSessionToken(loginEmail);
          return json({ ok: true, data: { token } });
        }

        default:
          return fail(`알 수 없는 action: ${action}`, 400);
      }
    } catch (e) {
      console.error(`[admin-api] ${action} 실패: ${e}`);
      return fail(e instanceof Error ? e.message : "처리에 실패했습니다.", 400);
    }
  }

  // ── 3. 신원 확인(구글 또는 비밀번호 로그인 세션) ───────────────────────────────────
  const authHeader = req.headers.get("Authorization") ?? "";
  const idToken = authHeader.startsWith("Bearer ") ? authHeader.slice("Bearer ".length) : "";
  if (!idToken) return fail("로그인이 필요합니다.", 401);

  const email = await verifyIdentity(idToken);
  if (!email) return fail("로그인이 필요합니다.", 401);

  // ── 4. 인가 ────────────────────────────────────────────────────────────────
  const isMaster = MASTER_EMAILS.has(email);
  if (!isMaster && !(await isEnabledOperator(db, email))) {
    return fail("운영자 권한이 없습니다.", 403);
  }

  // ── 5. 처리 ────────────────────────────────────────────────────────────────
  // 운영자 관리는 마스터만. 화면에서도 숨기지만 서버가 다시 막는다.
  if (action.startsWith("operators.") && !isMaster) {
    return fail("운영자 관리는 마스터만 할 수 있습니다.", 403);
  }

  try {
    switch (action) {
      case "session.me":
        return json({ ok: true, data: { email, isMaster } });

      case "operators.list": {
        const { data, error } = await db.rpc("ts_admin_list_operators");
        if (error) throw new Error(error.message);
        // 표 자체는 안 건드리고, 비밀번호 설정 여부만 덧붙인다 — 화면에서 상태 표시에 쓴다.
        const rows = (data ?? []) as Array<Record<string, unknown> & { email: string }>;
        const emails = rows.map((r) => r.email);
        const withPassword = new Set<string>();
        if (emails.length > 0) {
          const { data: pwRows, error: pwError } = await db
            .from("ts_admin_passwords")
            .select("email")
            .in("email", emails);
          if (pwError) throw new Error(pwError.message);
          for (const r of pwRows ?? []) withPassword.add(r.email as string);
        }
        return json({ ok: true, data: rows.map((r) => ({ ...r, hasPassword: withPassword.has(r.email) })) });
      }

      case "operators.upsert": {
        // 소문자로 저장해야 한다 — 로그인·비밀번호 설정·hasPassword 판정이 전부 소문자로
        // 비교한다(원본 대소문자를 그대로 넣으면 그 운영자는 영원히 인증에 실패한다).
        const { error } = await db.rpc("ts_admin_upsert_operator", {
          p_email: str(params, "email").toLowerCase(),
          p_display_name: optStr(params, "displayName"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "operators.setPassword": {
        // 이메일 발송을 안 거친다 — 마스터가 여기서 직접 정한 비밀번호를 운영자에게 따로 알려준다.
        const targetEmail = str(params, "email").toLowerCase();
        const newPassword = str(params, "newPassword");
        if (newPassword.length < MIN_PASSWORD_LENGTH) {
          return fail(`비밀번호는 ${MIN_PASSWORD_LENGTH}자 이상이어야 합니다.`, 400);
        }
        if (!(await isEnabledOperator(db, targetEmail))) {
          throw new Error("등록된 운영자가 아닙니다.");
        }
        const passwordHash = await hashSecret(newPassword);
        const { error } = await db
          .from("ts_admin_passwords")
          .upsert(
            { email: targetEmail, password_hash: passwordHash, updated_at: new Date().toISOString(), updated_by: email },
            { onConflict: "email" },
          );
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "operators.setDisabled": {
        const targetEmail = str(params, "email").toLowerCase();
        // 여기 오는 요청은 항상 마스터가 보낸 것이다(operators.* 는 위에서 이미 막았다) — 마스터
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
        if (error) throw new Error(error.message);
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
        const banDuration = seconds <= 0 ? "none" : `${seconds}s`;

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
          p_mode: optStr(params, "mode") === "id" ? "id" : "nickname",
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "purchases.list": {
        const page = typeof params["page"] === "number" ? params["page"] : 1;
        const pageSize = 20;
        const from = (page - 1) * pageSize;
        const to = from + pageSize - 1;

        let query = db
          .from("purchases")
          .select(
            "id, account_id, user_id, product_id, order_id, package_name, store, price_amount, price_currency, price_amount_krw, verified_at",
            { count: "exact" },
          )
          .order("verified_at", { ascending: false })
          .range(from, to);

        const search = sanitizeSearchTerm(optStr(params, "search").trim());
        if (search) {
          query = query.or(`product_id.ilike.%${search}%,user_id.ilike.%${search}%,order_id.ilike.%${search}%`);
        }
        const startDate = params["startDate"];
        const endDate = params["endDate"];
        if (typeof startDate === "string" && startDate) query = query.gte("verified_at", startDate);
        if (typeof endDate === "string" && endDate) {
          const next = nextDayExclusive(endDate);
          if (next) query = query.lt("verified_at", next);
        }

        const { data, error, count } = await query;
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [], total: count ?? 0, pageSize } });
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

        const source = sanitizeSearchTerm(optStr(params, "source").trim());
        if (source) query = query.ilike("source", `%${source}%`);

        const startDate = params["startDate"];
        if (typeof startDate === "string" && startDate) query = query.gte("created_at", startDate);
        const endDate = params["endDate"];
        if (typeof endDate === "string" && endDate) {
          const next = nextDayExclusive(endDate);
          if (next) query = query.lt("created_at", next);
        }

        const { data, error, count } = await query;
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [], total: count ?? 0, pageSize } });
      }

      case "dashboard.stats": {
        const { data, error } = await db.rpc("ts_admin_dashboard_stats");
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
          .select("id, channel_id, account_id, user_id, display_name, content, created_at, deleted_at, deleted_by", { count: "exact" })
          .order("id", { ascending: false })
          .range(from, to);

        const channelId = params["channelId"];
        if (typeof channelId === "string" && channelId) query = query.eq("channel_id", channelId);
        if (!bool(params, "includeDeleted")) query = query.is("deleted_at", null);

        const search = sanitizeSearchTerm(optStr(params, "search").trim());
        if (search) query = query.or(`display_name.ilike.%${search}%,content.ilike.%${search}%,user_id.ilike.%${search}%`);

        const { data, error, count } = await query;
        if (error) throw new Error(error.message);
        return json({ ok: true, data: { rows: data ?? [], total: count ?? 0, pageSize } });
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
        return json({ ok: true, data: { rows: data ?? [], total: count ?? 0, pageSize } });
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

      case "coupons.list": {
        const { data, error } = await db.rpc("ts_admin_coupon_list", { p_search: optStr(params, "search") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
      }

      case "coupons.codes": {
        const { data, error } = await db.rpc("ts_admin_coupon_codes", { p_coupon_id: str(params, "id") });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: data ?? [] });
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
        const { data, error } = await db.rpc("ts_admin_send_mail", {
          p_target_mode: str(params, "mode"),
          p_title: str(params, "title"),
          p_expires_at: str(params, "expiresAt"),
          p_account_ids: params["accountIds"] ?? null,
          p_server_id: params["serverId"] ?? null,
          p_content: optStr(params, "content"),
          p_items: params["items"] ?? null,
          p_created_by: params["createdBy"] ?? null,
          p_skip_item_validation: false,
          p_category: optStr(params, "category") || "default",
          p_localized: params["localized"] ?? null,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data });
      }

      case "mails.createSchedule": {
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
          p_created_by: params["createdBy"] ?? null,
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
    return fail(e instanceof Error ? e.message : "처리에 실패했습니다.", 400);
  }
});
