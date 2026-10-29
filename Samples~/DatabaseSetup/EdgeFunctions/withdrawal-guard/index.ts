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

function jwtSub(jwt: string): string | null {
  const part = jwt.split(".")[1];
  if (!part) return null;
  try {
    const b64 = part.replace(/-/g, "+").replace(/_/g, "/");
    const payload = JSON.parse(atob(b64 + "=".repeat((4 - (b64.length % 4)) % 4)));
    return typeof payload.sub === "string" ? payload.sub : null;
  } catch {
    return null;
  }
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
      JSON.stringify({ deleted: false, reason: "missing_jwt" } satisfies GuardResponse),
      { status: 401, headers: { "Content-Type": "application/json" } },
    );
  }
  if (await requestLimited(authHeader)) {
    return new Response(
      JSON.stringify({ deleted: false, reason: "request_limited" } satisfies GuardResponse),
      { status: 429, headers: { "Content-Type": "application/json" } },
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
    // 자정 정리(withdrawal-sweep)가 방금 지운 계정의 토큰일 수 있다 — 그러면 "지워짐"으로 답해야 SDK 가
    // 지워진 계정 세션으로 로그인을 이어 가지 않고 새 계정을 만든다. 아무것도 지우지 않는 답이라 토큰 sub 만 본다.
    const sub = jwtSub(jwt);
    if (sub) {
      const { data: found, error: lookupError } = await adminClient.auth.admin.getUserById(sub);
      const gone = (!lookupError && !found?.user) || (!!lookupError && /not.?found/i.test(lookupError.message));
      if (gone) {
        return new Response(JSON.stringify({ deleted: true } satisfies GuardResponse), {
          headers: { "Content-Type": "application/json" },
        });
      }
    }
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
  // 자정 정리와 겹쳐 그 사이에 지워졌으면 결과는 같다 — 실패로 돌리면 SDK 가 지워진 계정으로 로그인을 이어 간다.
  if (deleteRes.error && !/not.?found/i.test(deleteRes.error.message)) {
    return new Response(
      JSON.stringify({ deleted: false, reason: deleteRes.error.message } satisfies GuardResponse),
      { status: 500, headers: { "Content-Type": "application/json" } },
    );
  }

  return new Response(JSON.stringify({ deleted: true } satisfies GuardResponse), {
    headers: { "Content-Type": "application/json" } },
  );
});
