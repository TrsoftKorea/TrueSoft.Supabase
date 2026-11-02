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
  countCall("admin-ban-user", req);
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
