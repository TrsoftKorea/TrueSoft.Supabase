-- =============================================================================
-- 내 탈퇴 예약 상태 조회 RPC (게이트 UI용)
-- 로그인 직후 본인(account_id=auth.uid())의 displayName/예약 시각/남은 시간을 한 번에 반환합니다.
-- 마지막 SELECT(Result 탭)로 함수 반영·GRANT 여부를 확인한다.
--
-- 재실행: CREATE OR REPLACE 로 정의가 항상 이 파일의 최종본으로 맞춰진다. GRANT·COMMENT 도 재실행해도 동일.
-- 반환 컬럼 이름·순서·타입이 바뀌면 CREATE OR REPLACE 만으로는 갱신되지 않는다(42P13).
-- 그 경우 아래 DROP 후 새 정의가 적용된다.
-- =============================================================================

drop function if exists public.ts_my_withdrawal_status();

create or replace function public.ts_my_withdrawal_status()
returns table(
  display_name text,
  withdrawn_at timestamptz,
  server_now timestamptz,
  is_scheduled boolean,
  seconds_remaining bigint
)
language sql
security invoker
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
    coalesce(me.display_name, '') as display_name,
    me.withdrawn_at as withdrawn_at,
    now_cte.n as server_now,
    (me.withdrawn_at is not null and me.withdrawn_at > now_cte.n) as is_scheduled,
    case
      when me.withdrawn_at is not null and me.withdrawn_at > now_cte.n
        then extract(epoch from (me.withdrawn_at - now_cte.n))::bigint
      else 0::bigint
    end as seconds_remaining
  from now_cte
  left join me on true;
$$;

comment on function public.ts_my_withdrawal_status() is
  '본인 탈퇴 예약 상태(displayName/예약 시각/서버 시각/예약 여부/남은 초)를 반환한다.';

grant execute on function public.ts_my_withdrawal_status() to authenticated;

-- -----------------------------------------------------------------------------
-- 실행 결과 (SQL Editor Result 탭에서 반영 여부 확인)
-- RETURNS TABLE 이면 identity 문자열이 빈 문자열이 아닐 수 있어, public 스키마·이름으로만 확인한다.
-- -----------------------------------------------------------------------------
select
  'ts_my_withdrawal_status'::text as object_name,
  case
    when exists (
      select 1
      from pg_proc p
      join pg_namespace n on n.oid = p.pronamespace
      where n.nspname = 'public'
        and p.proname = 'ts_my_withdrawal_status'
    ) then 'applied'
    else 'missing'
  end as status,
  (
    select pg_get_function_identity_arguments(p.oid)
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public'
      and p.proname = 'ts_my_withdrawal_status'
    order by p.oid
    limit 1
  ) as identity_arguments,
  exists (
    select 1
    from information_schema.routine_privileges rp
    where rp.routine_schema = 'public'
      and rp.routine_name = 'ts_my_withdrawal_status'
      and rp.grantee = 'authenticated'
      and rp.privilege_type = 'EXECUTE'
  ) as grant_authenticated_execute;

