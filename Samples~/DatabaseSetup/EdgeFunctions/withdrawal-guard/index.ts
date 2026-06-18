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
