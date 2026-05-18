-- 플레이어의 소속 서버를 변경하는 이주 기능을 제공합니다.
-- 클라이언트 자가 이주(ts_transfer_my_server)와
-- 운영 도구용 강제 이주(ts_admin_transfer_user_server)를 지원합니다.
--
-- =============================================================================
-- 서버 이주 — server_id 동기화 트리거 + 이주 RPC
-- 선행: 03_display_names.sql, 05_user_sessions.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_sync_server_id_by_account — server_id 자동 동기화 트리거 함수
-- ---------------------------------------------------------------------------
-- INSERT/UPDATE 시 account_id 기준으로 profiles.server_id를 파생 테이블에 강제 반영합니다.
-- display_names·user_sessions에 BEFORE 트리거로 등록됩니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_sync_server_id_by_account()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_server_id uuid;
begin
  if new.account_id is not null then
    select p.server_id into v_server_id
    from public.user_profiles p
    where p.account_id = new.account_id
    limit 1;
  end if;

  if v_server_id is null then
    v_server_id := public.ts_default_server_id();
  end if;

  new.server_id := v_server_id;
  return new;
end;
$$;

drop trigger if exists trg_display_names_sync_server_id on public.display_names;
create trigger trg_display_names_sync_server_id
before insert or update on public.display_names
for each row
execute function public.ts_sync_server_id_by_account();

-- user_saves 테이블이 제거되었으므로 해당 트리거 삭제됨.
-- 커스텀 세이브 테이블에는 프로젝트별로 ts_sync_server_id_by_account 트리거를 직접 추가하세요.

drop trigger if exists trg_user_sessions_sync_server_id on public.user_sessions;
create trigger trg_user_sessions_sync_server_id
before insert or update on public.user_sessions
for each row
execute function public.ts_sync_server_id_by_account();

-- ---------------------------------------------------------------------------
-- 이주 RPC
-- ---------------------------------------------------------------------------
-- _ts_transfer_user_server_core : 내부 코어 (PostgREST 미노출)
-- ts_transfer_my_server         : 클라이언트 자가 이주
-- ts_admin_transfer_user_server : 운영 전용 이주 (service_role 한정)
-- ---------------------------------------------------------------------------

-- 코어: 계정 UUID 기준 단일 트랜잭션 이주. PostgREST에 노출하지 않음(권한 회수).
create or replace function public._ts_transfer_user_server_core(
  p_account_id uuid,
  p_target_server_code text
)
returns table(ok boolean, reason text, target_server_id uuid)
language plpgsql
security definer
set search_path = public
as $$
declare
  v_current_server_id uuid;
  v_target_server_id uuid;
  v_target_allow_transfers boolean;
  v_target_allow_new_signups boolean;
begin
  if p_account_id is null then
    return query select false, 'account_id_required'::text, null::uuid;
    return;
  end if;

  if p_target_server_code is null or length(trim(p_target_server_code)) = 0 then
    return query select false, 'target_server_code_empty'::text, null::uuid;
    return;
  end if;

  select gs.id, gs.allow_transfers, gs.allow_new_signups
    into v_target_server_id, v_target_allow_transfers, v_target_allow_new_signups
  from public.game_servers gs
  where gs.server_code = trim(p_target_server_code)
  limit 1;

  if v_target_server_id is null then
    return query select false, 'target_server_not_found'::text, null::uuid;
    return;
  end if;

  if v_target_allow_transfers is false then
    return query select false, 'target_server_transfer_blocked'::text, null::uuid;
    return;
  end if;

  select p.server_id into v_current_server_id
  from public.user_profiles p
  where p.account_id = p_account_id
  limit 1;

  if v_current_server_id is null then
    return query select false, 'profile_not_found'::text, null::uuid;
    return;
  end if;

  if v_current_server_id = v_target_server_id then
    return query select true, null::text, v_target_server_id;
    return;
  end if;

  if exists (
    select 1
    from public.display_names d
    where d.account_id = p_account_id
      and d.server_id = coalesce(v_current_server_id, d.server_id)
      and exists (
        select 1
        from public.display_names x
        where x.server_id = v_target_server_id
          and lower(trim(x.display_name)) = lower(trim(d.display_name))
          and x.account_id <> p_account_id
      )
  ) then
    return query select false, 'display_name_taken_in_target_server'::text, null::uuid;
    return;
  end if;

  update public.user_profiles
  set server_id = v_target_server_id
  where account_id = p_account_id;

  update public.display_names
  set server_id = v_target_server_id
  where account_id = p_account_id;

  update public.user_sessions
  set server_id = v_target_server_id
  where account_id = p_account_id;

  update public.anonymous_recovery_tokens
  set server_id = v_target_server_id
  where account_id = p_account_id;

  return query select true, null::text, v_target_server_id;
end;
$$;

comment on function public._ts_transfer_user_server_core(uuid, text) is
  '내부용: profiles·display_names·user_sessions·anonymous_recovery_tokens 의 server_id 일괄 이주.';

revoke all on function public._ts_transfer_user_server_core(uuid, text) from public;
revoke all on function public._ts_transfer_user_server_core(uuid, text) from anon, authenticated;

-- 로그인 유저: auth.uid()만 이주 대상 (클라이언트 자가 이주)
create or replace function public.ts_transfer_my_server(
  p_target_server_code text,
  p_reason text default null
)
returns table(ok boolean, reason text, target_server_id uuid)
language plpgsql
security definer
set search_path = public
as $$
declare
  v_account_id uuid := auth.uid();
begin
  if v_account_id is null then
    return query select false, 'auth_required'::text, null::uuid;
    return;
  end if;

  return query
  select c.ok, c.reason, c.target_server_id
  from public._ts_transfer_user_server_core(v_account_id, p_target_server_code) as c;
end;
$$;

-- 운영 전용: service_role JWT만 허용. p_account_id = auth.users.id
create or replace function public.ts_admin_transfer_user_server(
  p_account_id uuid,
  p_target_server_code text,
  p_reason text default null
)
returns table(ok boolean, reason text, target_server_id uuid)
language plpgsql
security definer
set search_path = public
as $$
begin
  if coalesce(auth.jwt() ->> 'role', '') <> 'service_role' then
    return query select false, 'forbidden_not_service_role'::text, null::uuid;
    return;
  end if;

  return query
  select c.ok, c.reason, c.target_server_id
  from public._ts_transfer_user_server_core(p_account_id, p_target_server_code) as c;
end;
$$;

comment on function public.ts_admin_transfer_user_server(uuid, text, text) is
  '운영 전용: 임의 계정 서버 이주. PostgREST는 Secret 키로만 호출할 것.';

grant execute on function public.auth_user_server_id() to authenticated;
grant execute on function public.ts_my_server_id() to authenticated;
grant execute on function public.ts_transfer_my_server(text, text) to authenticated;

revoke all on function public.ts_admin_transfer_user_server(uuid, text, text) from public;
revoke all on function public.ts_admin_transfer_user_server(uuid, text, text) from anon, authenticated;
grant execute on function public.ts_admin_transfer_user_server(uuid, text, text) to service_role;
