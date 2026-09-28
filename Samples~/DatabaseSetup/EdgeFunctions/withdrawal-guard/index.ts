import { createClient } from "npm:@supabase/supabase-js@2";

type GuardResponse = {
  deleted: boolean;
  reason?: string;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

Deno.serve(async (req) => {
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ")
    ? authHeader.slice("Bearer ".length)
    : "";

  if (!jwt) {
    return new Response(
      JSON.stringify({ deleted: false, reason: "missing_jwt" } satisfies GuardResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });

  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const userRes = await userClient.auth.getUser();
  const user = userRes.data.user;
  if (!user) {
    return new Response(
      JSON.stringify({ deleted: false, reason: "user_not_found" } satisfies GuardResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  // user_profiles는 SELECT가 공개(프로필 표시용)이므로 account_id로 본인 행을 명시 필터해야 함
  const profileRes = await userClient
    .from("user_profiles")
    .select("withdrawn_at")
    .eq("account_id", user.id)
    .maybeSingle();

  if (profileRes.error) {
    return new Response(
      JSON.stringify({ deleted: false, reason: profileRes.error.message } satisfies GuardResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  const withdrawnAt = profileRes.data?.withdrawn_at
    ? new Date(profileRes.data.withdrawn_at as string)
    : null;
  if (!withdrawnAt || withdrawnAt.getTime() > Date.now()) {
    return new Response(JSON.stringify({ deleted: false } satisfies GuardResponse), {
      headers: { "Content-Type": "application/json" } },
    );
  }

  await adminClient.from("account_closures").upsert(
    {
      user_id: user.id,
      account_id: user.id,
      closed_at: new Date().toISOString(),
      note: "withdrawal_guard",
    },
    { onConflict: "user_id" },
  );

  // Sign in with Apple 연결을 끊는다(애플 심사 기준). 보통은 탈퇴 신청 때 이미 끊었지만 그때 놓쳤을 수
  // 있다. 계정이 지워지면 보관한 토큰도 함께 지워져 다시는 끊을 수 없으므로 여기가 마지막 기회다.
  // 실패해도 삭제는 진행한다 — 삭제는 이용자가 요청한 일이라 이걸로 막지 않는다.
  try {
    const revokeRes = await fetch(`${SUPABASE_URL}/functions/v1/apple-token`, {
      method: "POST",
      headers: { "Authorization": `Bearer ${jwt}`, "apikey": SUPABASE_PUBLISHABLE_KEY, "Content-Type": "application/json" },
      body: JSON.stringify({ action: "revoke" }),
      // 오래 걸리면 SDK 의 가드 호출이 먼저 시간 초과로 끝나 "삭제 안 함"으로 읽고 로그인을 이어 간다.
      // 그 뒤에 서버가 계정을 지우면 지워진 계정의 세션으로 게임에 들어가게 된다.
      signal: AbortSignal.timeout(10000),
    });
    if (!revokeRes.ok) console.warn(`[withdrawal-guard] 애플 토큰 철회 실패: ${revokeRes.status} ${await revokeRes.text()}`);
  } catch (e) {
    console.warn(`[withdrawal-guard] 애플 토큰 철회 호출 실패: ${e}`);
  }

  const deleteRes = await adminClient.auth.admin.deleteUser(user.id, false);
  if (deleteRes.error) {
    return new Response(
      JSON.stringify({ deleted: false, reason: deleteRes.error.message } satisfies GuardResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  return new Response(JSON.stringify({ deleted: true } satisfies GuardResponse), {
    headers: { "Content-Type": "application/json" } },
  );
});
