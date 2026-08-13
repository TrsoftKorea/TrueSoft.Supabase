import { createClient } from "jsr:@supabase/supabase-js@2";

type Body = {
  user_id: string;
  duration_hours: number; // 예: 1, 24, 72
};

// legacy service_role JWT 키가 아니라 새 secret key를 쓴다(형제 함수들과 동일).
// 두 값 모두 플랫폼이 주입하므로 모듈 로드 시점에 읽어 빠르게 실패시킨다.
const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

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

  const supabase = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY);

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
