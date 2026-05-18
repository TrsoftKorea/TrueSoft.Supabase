-- =============================================================================
-- 탈퇴 — account_closures + profiles_withdrawn_at 인덱스
-- 선행: 02_profiles.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- account_closures (탈퇴 이력 예시 — 클라이언트 직접 접근 없음 가정)
-- ---------------------------------------------------------------------------
create table if not exists public.account_closures (
  id bigint generated always as identity primary key,
  user_id text not null,
  account_id uuid null,
  closed_at timestamptz not null default now(),
  note text null
);

alter table public.account_closures add column if not exists user_id text;
alter table public.account_closures add column if not exists account_id uuid;
alter table public.account_closures add column if not exists closed_at timestamptz not null default now();
alter table public.account_closures add column if not exists note text;

do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'account_closures'
      and column_name = 'user_id' and udt_name = 'uuid'
  ) then
    alter table public.account_closures alter column user_id type text using user_id::text;
  end if;
end $$;

comment on table public.account_closures is '탈퇴 기록. PostgREST는 service role 등으로만 쓰는 것을 권장.';

create index if not exists account_closures_user_id_idx on public.account_closures (user_id);
create unique index if not exists account_closures_user_id_uq on public.account_closures (user_id);
create index if not exists account_closures_account_id_idx on public.account_closures (account_id);
create index if not exists account_closures_closed_at_idx on public.account_closures (closed_at desc);

-- 탈퇴 예약 만료 조회(정리 배치/로그인 가드) 성능용 인덱스
create index if not exists profiles_withdrawn_at_idx
  on public.user_profiles (withdrawn_at)
  where withdrawn_at is not null;

alter table public.account_closures enable row level security;

-- [의도적] 정책 없음 — RLS가 활성화된 상태에서 policy가 없으면 anon/authenticated 역할의
-- 모든 접근(SELECT/INSERT/UPDATE/DELETE)이 기본 차단됨. service_role만 RLS를 우회하여 접근 가능.
-- 일반 사용자에게 접근을 열어줄 경우에만 별도 policy 추가할 것.