import { createClient } from "jsr:@supabase/supabase-js@2";

type Body = {
  user_id: string;
  duration_hours: number; // 예: 1, 24, 72
};

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response(JSON.stringify({ error: "method_not_allowed" }), { status: 405 });
  }

  // Retool에서만 호출되도록 간단한 API 키 체크(운영툴 MVP용)
  const provided = req.headers.get("x-admin-api-key");
  const expected = Deno.env.get("ADMIN_API_KEY");
  if (!expected || !provided || provided !== expected) {
    return new Response(JSON.stringify({ error: "unauthorized" }), { status: 401 });
  }

  const { user_id, duration_hours } = (await req.json()) as Body;

  if (!user_id || typeof duration_hours !== "number" || Number.isNaN(duration_hours) || duration_hours < 0) {
    return new Response(JSON.stringify({ error: "invalid_input" }), { status: 400 });
  }

  const supabaseUrl = Deno.env.get("SUPABASE_URL")!;
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const supabase = createClient(supabaseUrl, serviceRoleKey);

  // ban_duration 포맷: decimal + 단위 (예: "24h", unban은 "none")
  const banDuration = duration_hours === 0 ? "none" : `${duration_hours}h`;

  const { data, error } = await supabase.auth.admin.updateUserById(user_id, {
    ban_duration: banDuration,
  });

  if (error) {
    return new Response(JSON.stringify({ error: "ban_failed", details: error.message }), { status: 400 });
  }

  return new Response(
    JSON.stringify({
      ok: true,
      user_id,
      ban_duration: banDuration,
      banned_until: (data as any)?.banned_until ?? null,
    }),
    { status: 200, headers: { "Content-Type": "application/json" } }
  );
});
