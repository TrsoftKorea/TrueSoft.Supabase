-- =============================================================================
-- 서버 기준 현재 시각 (비로그인 포함)
-- Supabase SQL Editor에서 실행. 클라이언트는 POST /rest/v1/rpc/ts_server_now (바디 {})
-- 마지막 SELECT(Result 탭)로 함수 존재·GRANT·샘플 시각을 확인한다.
--
-- 재실행: CREATE OR REPLACE 로 정의가 항상 이 파일의 최종본으로 맞춰진다. GRANT·COMMENT 도 재실행해도 동일.
-- =============================================================================

create or replace function public.ts_server_now()
returns table(server_time timestamptz)
language sql
stable
security definer
set search_path = public
as $$
  select clock_timestamp() as server_time;
$$;

comment on function public.ts_server_now() is 'DB 서버 시각(요청 시점). 클라이언트 시계 대신 이벤트·쿨다운 등에 사용.';

grant execute on function public.ts_server_now() to anon, authenticated;

-- -----------------------------------------------------------------------------
-- 실행 결과 (SQL Editor Result 탭에서 반영 여부 확인)
-- RETURNS TABLE 이면 identity 문자열이 빈 문자열이 아닐 수 있어, public 스키마·이름으로만 확인한다.
-- -----------------------------------------------------------------------------
select
  'ts_server_now'::text as object_name,
  case
    when exists (
      select 1
      from pg_proc p
      join pg_namespace n on n.oid = p.pronamespace
      where n.nspname = 'public'
        and p.proname = 'ts_server_now'
    ) then 'applied'
    else 'missing'
  end as status,
  (
    select pg_get_function_identity_arguments(p.oid)
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public'
      and p.proname = 'ts_server_now'
    order by p.oid
    limit 1
  ) as identity_arguments,
  (select server_time from public.ts_server_now() limit 1) as sample_value,
  exists (
    select 1
    from information_schema.routine_privileges rp
    where rp.routine_schema = 'public'
      and rp.routine_name = 'ts_server_now'
      and rp.grantee = 'anon'
      and rp.privilege_type = 'EXECUTE'
  ) as grant_anon_execute,
  exists (
    select 1
    from information_schema.routine_privileges rp
    where rp.routine_schema = 'public'
      and rp.routine_name = 'ts_server_now'
      and rp.grantee = 'authenticated'
      and rp.privilege_type = 'EXECUTE'
  ) as grant_authenticated_execute;
