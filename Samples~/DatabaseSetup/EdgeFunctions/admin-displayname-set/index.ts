import { createClient } from "npm:@supabase/supabase-js@2";

type AdminSetRequest = {
  display_name?: string;
  account_id?: string;
  user_id?: string;
};

type AdminSetResponse = {
  ok: boolean;
  reason?: string;
  display_name?: string;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

function normalize(name: string): string {
  return name.trim();
}

function json(body: AdminSetResponse, status: number): Response {
  return new Response(JSON.stringify(body satisfies AdminSetResponse), {
    status,
    headers: { "Content-Type": "application/json" },
  });
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

Deno.serve(async (req) => {
  countCall("admin-displayname-set", req);
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ") ? authHeader.slice("Bearer ".length) : "";

  if (!jwt || jwt !== SUPABASE_SECRET_KEY) {
    return json({ ok: false, reason: "forbidden" }, 401);
  }

  let body: AdminSetRequest | null = null;
  try {
    body = await req.json();
  } catch {
    body = null;
  }

  const displayName = normalize(body?.display_name ?? "");
  if (!displayName) return json({ ok: false, reason: "display_name_empty" }, 400);
  if (displayName.length > 64) return json({ ok: false, reason: "display_name_too_long" }, 400);

  const rawAccountId = (body?.account_id ?? "").trim();
  const rawUserId = (body?.user_id ?? "").trim();
  if (!rawAccountId && !rawUserId) return json({ ok: false, reason: "both_identifiers_null" }, 400);

  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  // 대상 플레이어 프로필 조회 (service_role → RLS 우회)
  const profileQ = adminClient
    .from("user_profiles")
    .select("account_id, user_id, server_id")
    .limit(1);
  const { data: profile, error: profileError } = await (
    rawAccountId
      ? profileQ.eq("account_id", rawAccountId)
      : profileQ.eq("user_id", rawUserId)
  ).maybeSingle();

  if (profileError || !profile) {
    return json({ ok: false, reason: "profile_not_found" }, 404);
  }

  const { account_id: accountId, user_id: userId, server_id: serverId } = profile;

  // display_names upsert — 고유 제약이 중복을 처리함
  const { error: upsertError } = await adminClient
    .from("display_names")
    .upsert(
      {
        account_id: accountId,
        user_id: userId,
        server_id: serverId,
        display_name: displayName,
        updated_at: new Date().toISOString(),
      },
      { onConflict: "account_id" },
    );

  if (upsertError) {
    const msg = upsertError.message ?? "display_name_upsert_failed";
    const reason =
      msg.toLowerCase().includes("duplicate") || msg.toLowerCase().includes("unique")
        ? "display_name_taken"
        : msg;
    return json({ ok: false, reason }, reason === "display_name_taken" ? 409 : 500);
  }

  // auth.users.user_metadata 동기화
  const { data: existing, error: getUserError } = await adminClient.auth.admin.getUserById(accountId);
  if (getUserError || !existing?.user) {
    return json({ ok: false, reason: getUserError?.message ?? "auth_admin_get_user_failed" }, 500);
  }
  const prevMeta = (existing.user.user_metadata ?? {}) as Record<string, unknown>;
  const { error: updateError } = await adminClient.auth.admin.updateUserById(accountId, {
    user_metadata: { ...prevMeta, displayName, full_name: displayName, name: displayName },
  });
  if (updateError) {
    return json({ ok: false, reason: updateError.message }, 500);
  }

  return json({ ok: true, display_name: displayName }, 200);
});
