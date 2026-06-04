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

Deno.serve(async (req) => {
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

  return json<BanInfoResponse>({
    ok: true,
    is_banned: true,
    banned_until: bannedUntilRaw ?? null,
    ban_message: msgRow?.ban_message ?? null,
  }, 200);
});
