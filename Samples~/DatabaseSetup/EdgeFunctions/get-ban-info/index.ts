import { createClient } from "npm:@supabase/supabase-js@2";

type BanInfoRequest = {
  account_id?: string;
};

type BanInfoResponse = {
  ok: boolean;
  is_banned: boolean;
  banned_until: string | null;   // ISO 8601, null = 영구 차단 또는 차단 아님
  ban_message: string | null;
  reason?: string;
};

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const secretKeys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

// UUID v4 형식 검증 (간단한 정규식)
function isValidUUID(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
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
  countCall("get-ban-info", req);
  const json = <T>(body: T, status = 200) =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });

  let body: BanInfoRequest | null = null;
  try {
    body = await req.json();
  } catch {
    body = null;
  }

  const accountId = (body?.account_id ?? "").trim();
  if (!accountId || !isValidUUID(accountId)) {
    return json<BanInfoResponse>({ ok: false, is_banned: false, banned_until: null, ban_message: null, reason: "invalid_account_id" }, 400);
  }

  const adminClient = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  // auth.users에서 banned_until 조회
  const { data: userData, error: userError } = await adminClient.auth.admin.getUserById(accountId);
  if (userError || !userData?.user) {
    return json<BanInfoResponse>({ ok: true, is_banned: false, banned_until: null, ban_message: null }, 200);
  }

  const user = userData.user;
  const bannedUntilRaw = (user as unknown as { banned_until?: string }).banned_until ?? null;

  // 차단 여부 판정: banned_until이 존재하고 미래 시간이면 차단 중
  const now = new Date();
  const bannedUntilDate = bannedUntilRaw ? new Date(bannedUntilRaw) : null;
  const isBanned = bannedUntilDate === null
    ? false
    : bannedUntilDate > now;

  // 차단이 아니면 상세 정보 없이 반환 (프라이버시 보호)
  if (!isBanned) {
    return json<BanInfoResponse>({ ok: true, is_banned: false, banned_until: null, ban_message: null }, 200);
  }

  // user_ban_messages에서 어드민 메시지 조회
  const { data: msgRow } = await adminClient
    .from("user_ban_messages")
    .select("ban_message")
    .eq("account_id", accountId)
    .maybeSingle();

  // 인증 서버는 기간 없는 차단을 두지 않아, 운영 콘솔의 영구 차단은 100년 뒤 해제로 들어간다.
  // 50년 넘게 남았으면 영구로 보고 null 로 돌려준다 — SDK 는 null 을 영구 차단(IsPermanentBan)으로 읽는다.
  // 콘솔 화면의 영구 표시(PlayerDetail isPermanent)와 같은 기준이다.
  const permanentAfter = new Date(now);
  permanentAfter.setFullYear(permanentAfter.getFullYear() + 50);
  const isPermanent = bannedUntilDate !== null && bannedUntilDate >= permanentAfter;

  return json<BanInfoResponse>({
    ok: true,
    is_banned: true,
    banned_until: isPermanent ? null : bannedUntilRaw ?? null,
    ban_message: msgRow?.ban_message ?? null,
  }, 200);
});
