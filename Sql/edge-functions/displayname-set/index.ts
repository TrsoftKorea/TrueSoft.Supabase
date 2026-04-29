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
const SUPABASE_ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;
// ✅ 변경: service_role → 새 Secret Key (SECRET_KEY)
// Dashboard → Project Settings → API → Secret Keys에서 발급 (이름: SECRET_KEY)
const SECRET_KEY = Deno.env.get("SECRET_KEY")!;

function normalize(name: string): string {
  return name.trim();
}

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

  const userClient = createClient(SUPABASE_URL, SUPABASE_ANON_KEY, {
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
    .limit(1)
    .maybeSingle();

  if (myProfile.error || !myProfile.data?.server_id) {
    return new Response(
      JSON.stringify({ ok: false, reason: myProfile.error?.message ?? "server_id_not_found" } satisfies SetResponse),
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
    const msg = upsert.error.message ?? "display_name_upsert_failed";
    const reason = msg.toLowerCase().includes("duplicate") || msg.toLowerCase().includes("unique")
      ? "display_name_taken"
      : msg;
    return new Response(
      JSON.stringify({ ok: false, reason } satisfies SetResponse),
      { status: reason === "display_name_taken" ? 409 : 500, headers: { "Content-Type": "application/json" } },
    );
  }

  // 2) auth user_metadata 업데이트 (admin API - Secret Key 사용)
  // ✅ 변경: service_role → SECRET_KEY
  const adminClient = createClient(SUPABASE_URL, SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
  const existing = await adminClient.auth.admin.getUserById(user.id);
  if (existing.error || !existing.data?.user) {
    return new Response(
      JSON.stringify({ ok: false, reason: existing.error?.message ?? "auth_admin_get_user_failed" } satisfies SetResponse),
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
      JSON.stringify({ ok: false, reason: upd.error.message } satisfies SetResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  return new Response(JSON.stringify({ ok: true } satisfies SetResponse), {
    headers: { "Content-Type": "application/json" } },
  );
});
