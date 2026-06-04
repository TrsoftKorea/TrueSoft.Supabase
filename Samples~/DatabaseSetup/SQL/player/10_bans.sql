-- =============================================================================
-- 10_bans.sql  — 차단(Ban) 어드민 메시지
-- 선행: 01_servers.sql ~ 09_cron_jobs.sql
-- =============================================================================
-- Supabase 네이티브 ban(auth.users.banned_until)을 사용하는 프로젝트에서
-- 어드민이 작성한 사유 메시지를 플레이어에게 전달하기 위한 테이블.
-- SDK의 get-ban-info Edge Function이 이 테이블을 읽어 SupabaseBanInfo.BanMessage로 반환합니다.
-- =============================================================================

-- 어드민 차단 메시지
create table if not exists public.user_ban_messages (
  account_id  uuid        primary key references auth.users(id) on delete cascade,
  ban_message text,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

-- RLS 활성화 — 아래에서 모든 public 접근을 거부
-- 읽기는 get-ban-info Edge Function(service_role 클라이언트)만 허용
alter table public.user_ban_messages enable row level security;

comment on table public.user_ban_messages is
  '차단된 플레이어에게 표시할 어드민 메시지. auth.users.banned_until과 함께 사용.
   쓰기: service_role (Retool 또는 admin Edge Function).
   읽기: get-ban-info Edge Function (service_role) 경유 — 게임 클라이언트는 직접 접근 불가.';

notify pgrst, 'reload schema';
