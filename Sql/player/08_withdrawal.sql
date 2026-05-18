-- 회원 탈퇴 예약·이력 관리와 탈퇴 취소 기능을 제공합니다.
-- 탈퇴 예약은 user_profiles.withdrawn_at으로 관리되며,
-- ts_withdrawal_cancel_redeem RPC(withdrawal-cancel-redeem Edge Function 경유)로 취소합니다.
--
-- =============================================================================
-- 탈퇴 — account_closures + profiles_withdrawn_at 인덱스 + 탈퇴 취소 RPC
-- 선행: 02_profiles.sql (user_profiles.withdrawn_at)
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

-- ---------------------------------------------------------------------------
-- ts_request_withdrawal — 탈퇴 예약
-- ---------------------------------------------------------------------------
-- p_delay_days 일 후 탈퇴가 실행되도록 user_profiles.withdrawn_at을 설정합니다.
-- 0이면 즉시 탈퇴 예약. 탈퇴 예약 시 익명 복구 토큰도 함께 삭제합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_request_withdrawal(p_delay_days integer)
returns table(scheduled_at timestamptz)
language plpgsql
security definer
set search_path = public
as $$
declare
  v_delay_days   integer;
  v_scheduled_at timestamptz;
  v_account_id   uuid;
  v_user_id      text;
  scheduled_at   timestamptz;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  v_delay_days  := greatest(0, coalesce(p_delay_days, 0));
  v_account_id  := auth.uid();
  v_user_id     := auth.uid()::text;

  v_scheduled_at := case
    when v_delay_days <= 0 then now()
    else now() + make_interval(days => v_delay_days)
  end;

  insert into public.user_profiles (user_id, account_id, withdrawn_at)
  values (v_user_id, v_account_id, v_scheduled_at)
  on conflict (account_id) do update set
    withdrawn_at = excluded.withdrawn_at
  returning withdrawn_at into scheduled_at;

  if scheduled_at is null then
    return;
  end if;

  -- 탈퇴 예약 시 익명 복구 토큰 삭제 (재설치 후 복구 차단)
  perform public.ts_delete_my_anon_recovery_tokens();

  return query select scheduled_at;
end;
$$;

comment on function public.ts_request_withdrawal(integer) is
  '탈퇴 예약. p_delay_days일 후 탈퇴. 0이면 즉시 예약. 익명 복구 토큰도 함께 삭제.';

grant execute on function public.ts_request_withdrawal(integer) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_my_withdrawal_status — 본인 탈퇴 예약 상태 조회
-- ---------------------------------------------------------------------------
-- 탈퇴 예약 여부·남은 시간·서버 시각을 한 번에 반환합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_my_withdrawal_status()
returns table(
  display_name      text,
  withdrawn_at      timestamptz,
  server_now        timestamptz,
  is_scheduled      boolean,
  seconds_remaining bigint
)
language sql
set search_path = public
as $$
  with now_cte as (
    select clock_timestamp() as n
  ),
  me as (
    select
      dn.display_name,
      p.withdrawn_at
    from public.user_profiles p
    left join public.display_names dn on dn.account_id = p.account_id
    where p.account_id = auth.uid()
    limit 1
  )
  select
    coalesce(me.display_name, '')                                        as display_name,
    me.withdrawn_at                                                      as withdrawn_at,
    now_cte.n                                                            as server_now,
    (me.withdrawn_at is not null and me.withdrawn_at > now_cte.n)       as is_scheduled,
    case
      when me.withdrawn_at is not null and me.withdrawn_at > now_cte.n
        then extract(epoch from (me.withdrawn_at - now_cte.n))::bigint
      else 0::bigint
    end                                                                  as seconds_remaining
  from now_cte
  left join me on true;
$$;

comment on function public.ts_my_withdrawal_status() is
  '본인 탈퇴 예약 상태 조회. 탈퇴 예약 여부·남은 초·서버 시각 반환.';

grant execute on function public.ts_my_withdrawal_status() to authenticated;
