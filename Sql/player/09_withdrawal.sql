-- =============================================================================
-- 탈퇴 — account_closures + profiles_withdrawn_at 인덱스 + 탈퇴 취소 RPC
-- 선행: 02_profiles.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- account_closures — 탈퇴 이력 (클라이언트 직접 접근 없음)
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

-- ---------------------------------------------------------------------------
-- ts_withdrawal_cancel_redeem — 탈퇴 취소 RPC
-- ---------------------------------------------------------------------------
-- withdrawal-cancel-redeem Edge Function이 사용자 JWT로 이 RPC를 호출합니다.
-- Edge Function이 cancel_token을 검증한 뒤 이 RPC를 호출하므로
-- RPC 자체는 auth.uid() 기반으로 탈퇴 예약만 취소합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_withdrawal_cancel_redeem()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid          uuid;
  v_withdrawn_at timestamptz;
begin
  v_uid := auth.uid();
  if v_uid is null then
    return jsonb_build_object('ok', false, 'reason', 'not_authenticated');
  end if;

  select withdrawn_at into v_withdrawn_at
  from public.user_profiles
  where account_id = v_uid;

  if not found then
    return jsonb_build_object('ok', false, 'reason', 'profile_not_found');
  end if;

  -- 탈퇴 예약이 없거나 이미 만료된 경우
  if v_withdrawn_at is null or v_withdrawn_at <= now() then
    return jsonb_build_object('ok', false, 'reason', 'withdrawal_not_scheduled');
  end if;

  -- 탈퇴 예약 취소
  update public.user_profiles
  set withdrawn_at = null
  where account_id = v_uid;

  return jsonb_build_object('ok', true);
end;
$$;

comment on function public.ts_withdrawal_cancel_redeem() is
  '탈퇴 취소 RPC. withdrawal-cancel-redeem Edge Function에서 사용자 JWT로 호출. withdrawn_at을 초기화합니다.';

grant execute on function public.ts_withdrawal_cancel_redeem() to authenticated;
