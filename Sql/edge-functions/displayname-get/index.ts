import { createClient } from "npm:@supabase/supabase-js@2";

type GetRequest = {
  user_id?: string;
};

type GetResponse = {
  ok: boolean;
  display_name?: string;
  reason?: string;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;

Deno.serve(async (req) => {
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.startsWith("Bearer ")
    ? authHeader.slice("Bearer ".length)
    : "";
  if (!jwt) {
    return new Response(
      JSON.stringify({ ok: false, reason: "missing_jwt" } satisfies GetResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  let body: GetRequest | null = null;
  try {
    body = await req.json();
  } catch {
    body = null;
  }

  const userId = body?.user_id?.trim();
  if (!userId) {
    return new Response(
      JSON.stringify({ ok: false, reason: "user_id_empty" } satisfies GetResponse),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }

  const client = createClient(SUPABASE_URL, SUPABASE_ANON_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });

  const me = await client.auth.getUser(jwt);
  const accountId = me.data.user?.id;
  if (!accountId) {
    return new Response(
      JSON.stringify({ ok: false, reason: "user_not_found" } satisfies GetResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  const myProfile = await client
    .from("user_profiles")
    .select("server_id")
    .eq("account_id", accountId)
    .limit(1)
    .maybeSingle();

  if (myProfile.error || !myProfile.data?.server_id) {
    return new Response(
      JSON.stringify({ ok: false, reason: myProfile.error?.message ?? "server_id_not_found" } satisfies GetResponse),
      { status: 409, headers: { "Content-Type": "application/json" } },
    );
  }

  const res = await client
    .from("display_names")
    .select("display_name")
    .eq("user_id", userId)
    .eq("server_id", myProfile.data.server_id)
    .limit(1)
    .maybeSingle();

  if (res.error) {
    return new Response(
      JSON.stringify({ ok: false, reason: res.error.message } satisfies GetResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  return new Response(
    JSON.stringify({ ok: true, display_name: res.data?.display_name ?? "" } satisfies GetResponse),
    { headers: { "Content-Type": "application/json" } },
  );
});

