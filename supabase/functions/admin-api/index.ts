// 운영 도구 백엔드.
//
// 운영자 인증은 게임 Auth 를 쓰지 않는다. 구글이 발급한 ID 토큰을 여기서 직접 검증한다.
// 그래서 운영자는 auth.users 에 존재하지 않는다 — 플레이어와 계정 풀이 겹치지 않고,
// 게임의 탈퇴·차단 흐름이 운영자 접근에 영향을 주지 않는다.
//
// 권한은 두 단계다.
//   마스터  — ADMIN_EMAILS 환경변수. 화면에서 지울 수 없어 잠기지 않는 비상구. 운영자 관리 가능.
//   운영자  — ts_admin_operators 표. 마스터가 화면에서 추가·비활성화한다.
//
// 필요한 시크릿: GOOGLE_CLIENT_ID, ADMIN_EMAILS(쉼표 구분). SUPABASE_* 는 플랫폼이 주입한다.
//
// verify_jwt 는 false 로 배포한다. 게이트웨이의 JWT 검증은 게임 Auth 기준이라 구글 토큰을
// 통과시키지 못하고, CORS 프리플라이트(Authorization 없음)까지 막는다. 검증은 아래가 직접 한다.
import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";
import { createRemoteJWKSet, jwtVerify } from "npm:jose@5";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

const GOOGLE_CLIENT_ID = Deno.env.get("GOOGLE_CLIENT_ID") ?? "";
const GOOGLE_JWKS = createRemoteJWKSet(new URL("https://www.googleapis.com/oauth2/v3/certs"));

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
  } catch (e) {
    console.error(`[admin-api] 구글 토큰 검증 실패: ${e}`);
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

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
  if (req.method !== "POST") return fail("method_not_allowed", 405);

  // ── 1. 신원 확인 (구글) ─────────────────────────────────────────────────────
  const authHeader = req.headers.get("Authorization") ?? "";
  const idToken = authHeader.startsWith("Bearer ") ? authHeader.slice("Bearer ".length) : "";
  if (!idToken) return fail("로그인이 필요합니다.", 401);

  const email = await verifyGoogleIdToken(idToken);
  if (!email) return fail("로그인이 필요합니다.", 401);

  // ── 2. 인가 ────────────────────────────────────────────────────────────────
  // 운영자 표를 읽으려면 secret key 가 필요해 여기서 클라이언트를 만든다.
  // 이 시점의 질의는 고정된 목록 조회 하나뿐이라 호출자가 조종할 수 있는 부분이 없다.
  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const isMaster = MASTER_EMAILS.has(email);
  if (!isMaster && !(await isEnabledOperator(db, email))) {
    return fail("운영자 권한이 없습니다.", 403);
  }

  // ── 3. 처리 ────────────────────────────────────────────────────────────────
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
        return json({ ok: true, data: data ?? [] });
      }

      case "operators.upsert": {
        const { error } = await db.rpc("ts_admin_upsert_operator", {
          p_email: str(params, "email"),
          p_display_name: optStr(params, "displayName"),
          p_by: email,
        });
        if (error) throw new Error(error.message);
        return json({ ok: true, data: null });
      }

      case "operators.setDisabled": {
        const targetEmail = str(params, "email").toLowerCase();
        // 마스터는 표에 없지만, 혹시 같은 이메일이 들어와도 자기 발등은 못 찍게 한다.
        if (targetEmail === email) throw new Error("자기 계정은 비활성화할 수 없습니다.");
        const { error } = await db.rpc("ts_admin_set_operator_disabled", {
          p_email: targetEmail,
          p_disabled: bool(params, "disabled"),
          p_by: email,
        });
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
