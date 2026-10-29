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
const publishableKeys = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS")!);
const SUPABASE_PUBLISHABLE_KEY = publishableKeys.default;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

// ═══ BEGIN request-limit ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 콘솔 "설정 › 요청 제한" — 이 함수 호출도 게임의 데이터 요청과 같은 기준으로 센다. 빈 DB 함수(ts_request_limit_ping)를
// 사용자 로그인으로 불러 DB 의 사전 검사(ts_request_limit_check)를 태운다. 제한 중이면 그쪽이 429 로 거절한다.
// 검사 자체가 실패하면(네트워크 등) 막지 않는다 — 제한 기능이 고장 나도 게임이 멈추지 않게.
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
      JSON.stringify({ ok: false, reason: "missing_jwt" } satisfies GetResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }
  if (await requestLimited(authHeader)) {
    return new Response(
      JSON.stringify({ ok: false, reason: "request_limited" } satisfies GetResponse),
      { status: 429, headers: { "Content-Type": "application/json" } },
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

  const userClient = createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, {
    global: { headers: { Authorization: `Bearer ${jwt}` } },
  });
  const me = await userClient.auth.getUser(jwt);
  if (!me.data.user?.id) {
    return new Response(
      JSON.stringify({ ok: false, reason: "user_not_found" } satisfies GetResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }

  // display_name 만 노출: service_role로 조회해 display_names 직접 REST 노출을 막는다.
  // (전역 유니크 닉네임이므로 user_id로만 조회. RLS SELECT는 본인 행만 허용하므로 남의 닉네임은 이 경로로만 노출)
  const admin = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
  const res = await admin
    .from("display_names")
    .select("display_name")
    .eq("user_id", userId)
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
