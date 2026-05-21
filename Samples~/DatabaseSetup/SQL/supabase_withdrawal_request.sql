-- =============================================================================
-- 탈퇴 요청 RPC (서버 시각 기준)
-- 클라이언트는 "유예 기간(일)"만 전달하고, 실제 withdrawn_at은 서버에서 계산합니다.
-- 마지막 SELECT(Result 탭)로 함수 반영·GRANT 여부를 확인한다.
--
-- 선행: anonymous_recovery_tokens 정리를 위해 Sql/player/06_anonymous_recovery_tokens.sql 의
--       public.ts_delete_my_anon_recovery_tokens() 가 정의되어 있어야 합니다(해당 파일을 먼저 실행).
--
-- 재실행: CREATE OR REPLACE 로 정의가 항상 이 파일의 최종본으로 맞춰진다. GRANT·COMMENT 도 재실행해도 동일.
-- 인자/반환 타입을 바꿀 때는 기존 함수와 충돌하면 드롭 후 재생성이 필요할 수 있다.
-- =============================================================================

create or replace function public.ts_request_withdrawal(p_delay_days integer)
returns table(scheduled_at timestamptz)
language plpgsql
security definer
set search_path = public
as $$
declare
  v_delay_days integer;
  v_scheduled_at timestamptz;
  v_account_id uuid;
  v_user_id text;
  scheduled_at timestamptz;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  v_delay_days := greatest(0, coalesce(p_delay_days, 0));

  v_account_id := auth.uid();
  v_user_id := auth.uid()::text;

  v_scheduled_at := case
    when v_delay_days <= 0 then now()
    else now() + make_interval(days => v_delay_days)
  end;

  insert into public.user_profiles (user_id, account_id, withdrawn_at)
  values (v_user_id, v_account_id, v_scheduled_at)
  on conflict (account_id)
  do update set
    withdrawn_at = excluded.withdrawn_at
  returning withdrawn_at into scheduled_at;

  if scheduled_at is null then
    return;
  end if;

  -- 익명 복구 토큰은 탈퇴 시 더 이상 유효하지 않음(RLS로 일반 DELETE 불가 → DEFINER 헬퍼)
  perform public.ts_delete_my_anon_recovery_tokens();

  return query select scheduled_at;
end;
$$;

comment on function public.ts_request_withdrawal(integer) is
  '탈퇴 요청 시 withdrawn_at을 서버 시각 기준으로 설정(0일이면 즉시 now, 그 외 now + delay_days). SECURITY DEFINER: profiles RLS(INSERT/UPDATE WITH CHECK)와 충돌하지 않도록. 본인 auth.uid()만 대상.';

grant execute on function public.ts_request_withdrawal(integer) to authenticated;

-- -----------------------------------------------------------------------------
-- 실행 결과 (SQL Editor Result 탭에서 반영 여부 확인)
-- pg_get_function_identity_arguments() 는 환경에 따라 타입만이 아니라
-- "p_delay_days integer" 처럼 매개변수 이름까지 포함할 수 있다. 그래서 'integer' 와 단순 비교하면 안 된다.
-- 입력 타입만 보려면 input_argtypes(oidvectortypes(proargtypes)) 를 본다.
-- -----------------------------------------------------------------------------
select
  'ts_request_withdrawal(integer)'::text as object_name,
  case
    when exists (
      select 1
      from pg_proc p
      join pg_namespace n on n.oid = p.pronamespace
      where n.nspname = 'public'
        and p.proname = 'ts_request_withdrawal'
        and (
          trim(coalesce(oidvectortypes(p.proargtypes), '')) in ('integer', 'int4')
          or pg_get_function_identity_arguments(p.oid) ~* '(^|[, ])integer([, ]|$)'
          or pg_get_function_identity_arguments(p.oid) ~* '(^|[, ])int4([, ]|$)'
        )
    ) then 'applied'
    else 'missing'
  end as status,
  (
    select oidvectortypes(p.proargtypes)
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public'
      and p.proname = 'ts_request_withdrawal'
    order by p.oid
    limit 1
  ) as input_argtypes,
  (
    select pg_get_function_identity_arguments(p.oid)
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public'
      and p.proname = 'ts_request_withdrawal'
    order by p.oid
    limit 1
  ) as identity_arguments,
  exists (
    select 1
    from information_schema.routine_privileges rp
    where rp.routine_schema = 'public'
      and rp.routine_name = 'ts_request_withdrawal'
      and rp.grantee = 'authenticated'
      and rp.privilege_type = 'EXECUTE'
  ) as grant_authenticated_execute;
