-- 발송 폼에서 선택할 우편 분류(category) 사전 목록을 제공합니다.
-- mails.category 에는 여기 key 가 저장되며, 게임 클라이언트의 분류 필터 키와 일치해야 합니다.
-- 발송 시 목록에서만 선택하도록 어드민 UI에서 강제합니다(서버 검증은 두지 않음 — 기존 자유 문자열 호환).
--
-- =============================================================================
-- 우편 분류 사전 목록 — mail_categories
-- 선행: 06_mails.sql(mails.category), 12_admin_mail.sql(ts_admin_send_mail)
-- =============================================================================

create table if not exists public.mail_categories (
  key          text primary key,
  display_name text not null default '',
  sort_order   int  not null default 0,
  created_at   timestamptz not null default now()
);

comment on table public.mail_categories is
  '발송 폼에서 선택할 우편 분류(category) 사전 목록. mails.category에 key가 저장됨. 게임 필터 키와 일치해야 함.';

-- 기본 분류 시드(멱등)
insert into public.mail_categories (key, display_name, sort_order)
values ('default', '기본', 0)
on conflict (key) do nothing;

alter table public.mail_categories enable row level security;
revoke all on table public.mail_categories from anon, authenticated;

notify pgrst, 'reload schema';
