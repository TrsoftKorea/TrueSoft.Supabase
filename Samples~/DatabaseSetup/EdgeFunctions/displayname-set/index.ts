import { createClient } from "npm:@supabase/supabase-js@2";

type SetRequest = {
  display_name?: string;
  user_id?: string;
};

type SetResponse = {
  ok: boolean;
  reason?: string;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

function normalize(name: string): string {
  return name.trim();
}

// ═══ BEGIN request-limit ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 콘솔 "설정 › 요청 제한" — 이 함수 호출도 게임의 데이터 요청과 같은 기준으로 센다. 빈 DB 함수(ts_request_limit_ping)를
// 사용자 로그인으로 불러 DB 의 사전 검사(ts_request_limit_check)를 태운다. 제한 중이면 그쪽이 429 로 거절한다.
// 검사 자체가 실패하거나 2초 안에 답이 없으면 막지 않는다 — 제한 기능이 고장 나도 게임이 멈추지 않게.
async function requestLimited(authHeader: string): Promise<boolean> {
  try {
    const res = await fetch(`${Deno.env.get("SUPABASE_URL")}/rest/v1/rpc/ts_request_limit_ping`, {
      method: "POST",
      headers: {
        Authorization: authHeader,
        apikey: JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!).default,
        "Content-Type": "application/json",
      },
      body: "{}",
      signal: AbortSignal.timeout(2000),
    });
    await res.body?.cancel();
    return res.status === 429;
  } catch {
    return false;
  }
}
// ═══ END request-limit ═══

Deno.serve(async (req) => {
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ")
    ? authHeader.slice("Bearer ".length)
    : "";

  if (!jwt) {
    return new Response(
      JSON.stringify({ ok: false, reason: "missing_jwt" } satisfies SetResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }
  if (await requestLimited(authHeader)) {
    return new Response(
      JSON.stringify({ ok: false, reason: "request_limited" } satisfies SetResponse),
      { status: 429, headers: { "Content-Type": "application/json" } },
    );
  }

  let body: SetRequest | null = null;
  try {
    body = await req.json();
  } catch {
    body = null;
  }

  const rawName = body?.display_name ?? "";
  const displayName = normalize(rawName);
  if (!displayName) {
    return new Response(
      JSON.stringify({ ok: false, reason: "display_name_empty" } satisfies SetResponse),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }
  if (displayName.length > 64) {
    return new Response(
      JSON.stringify({ ok: false, reason: "display_name_too_long" } satisfies SetResponse),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });

  const userRes = await userClient.auth.getUser(jwt);
  const user = userRes.data.user;
  if (!user) {
    return new Response(
      JSON.stringify({ ok: false, reason: "user_not_found" } satisfies SetResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  const playerUserId = (body?.user_id ?? "").trim() || user.id;

  const myProfile = await userClient
    .from("user_profiles")
    .select("server_id")
    .eq("account_id", user.id)
    .maybeSingle();

  if (myProfile.error || !myProfile.data?.server_id) {
    return new Response(
      JSON.stringify({ ok: false, reason: "server_id_not_found" } satisfies SetResponse),
      { status: 409, headers: { "Content-Type": "application/json" } },
    );
  }

  // 1) display_names upsert (userClient - RLS 적용)
  const upsert = await userClient
    .from("display_names")
    .upsert(
      {
        account_id: user.id,
        user_id: playerUserId,
        server_id: myProfile.data.server_id,
        display_name: displayName,
        updated_at: new Date().toISOString(),
      },
      { onConflict: "account_id" },
    );

  if (upsert.error) {
    const msg = upsert.error.message ?? "";
    // display_name_forbidden 은 display_names 트리거(금칙어·금지 닉네임)가 낸다.
    const reason = msg.includes("display_name_forbidden")
      ? "display_name_forbidden"
      : msg.toLowerCase().includes("duplicate") || msg.toLowerCase().includes("unique")
      ? "display_name_taken"
      : "display_name_upsert_failed";
    const status = reason === "display_name_taken" ? 409 : reason === "display_name_forbidden" ? 400 : 500;
    return new Response(
      JSON.stringify({ ok: false, reason } satisfies SetResponse),
      { status, headers: { "Content-Type": "application/json" } },
    );
  }

  // 2) auth user_metadata 업데이트 (admin API - Secret Key 사용)
  // ✅ 변경: service_role → SECRET_KEY
  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
  const existing = await adminClient.auth.admin.getUserById(user.id);
  if (existing.error || !existing.data?.user) {
    return new Response(
      JSON.stringify({ ok: false, reason: "auth_admin_get_user_failed" } satisfies SetResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }
  const prevMeta = (existing.data.user.user_metadata ?? {}) as Record<string, unknown>;
  const merged = {
    ...prevMeta,
    displayName,
    full_name: displayName,
    name: displayName,
  };
  const upd = await adminClient.auth.admin.updateUserById(user.id, { user_metadata: merged });
  if (upd.error) {
    return new Response(
      JSON.stringify({ ok: false, reason: "display_name_update_failed" } satisfies SetResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  return new Response(JSON.stringify({ ok: true } satisfies SetResponse), {
    headers: { "Content-Type": "application/json" } },
  );
});
