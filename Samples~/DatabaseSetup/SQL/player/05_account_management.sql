-- 서버 이주와 회원 탈퇴 예약·이력을 관리합니다.
-- 클라이언트 자가 이주(ts_transfer_my_server)와 운영 도구용 강제 이주(ts_admin_transfer_user_server),
-- 탈퇴 예약(ts_request_withdrawal)·취소(ts_withdrawal_cancel_redeem)·상태 조회(ts_my_withdrawal_status)를 제공합니다.
--
-- =============================================================================
-- 계정 관리 — 서버 이주 + 탈퇴
-- 선행: 02_profiles.sql, 03_anonymous_recovery.sql
-- =============================================================================


-- =============================================================================
-- 서버 이주 — server_id 동기화 트리거 + 이주 RPC
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


-- =============================================================================
-- 탈퇴 — account_closures + 탈퇴 예약·취소·상태 RPC
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
-- 탈퇴 취소는 로그인하지 않은 상태에서 이뤄집니다(게이트가 세션을 이미 정리함).
-- withdrawal-cancel-redeem Edge Function이 cancel_token의 HMAC 서명을 검증해
-- 본인 인증을 대신하고, 서명에서 얻은 account_id를 secret key로 이 RPC에 넘깁니다.
-- 따라서 auth.uid()에 의존하지 않고 파라미터로 대상 계정을 받습니다.
-- 서명 검증은 Edge Function이 이미 수행하므로 이 함수는 service_role에게만 노출합니다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_withdrawal_cancel_redeem();
create or replace function public.ts_withdrawal_cancel_redeem(p_account_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_withdrawn_at timestamptz;
  v_exists       boolean;
begin
  if p_account_id is null then
    return jsonb_build_object('ok', false, 'reason', 'account_id_null');
  end if;

  -- account_id 하나에 프로필 행이 여러 개(서버별 등)일 수 있으므로 집계로 단일 값을 구한다.
  select max(withdrawn_at), bool_or(true)
  into   v_withdrawn_at, v_exists
  from public.user_profiles
  where account_id = p_account_id;

  if not coalesce(v_exists, false) then
    return jsonb_build_object('ok', false, 'reason', 'profile_not_found');
  end if;

  -- 탈퇴 예약이 없거나 이미 만료된 경우
  if v_withdrawn_at is null or v_withdrawn_at <= now() then
    return jsonb_build_object('ok', false, 'reason', 'withdrawal_not_scheduled');
  end if;

  -- 탈퇴 예약 취소(해당 계정의 모든 프로필 행)
  update public.user_profiles
  set withdrawn_at = null
  where account_id = p_account_id;

  return jsonb_build_object('ok', true);
end;
$$;

comment on function public.ts_withdrawal_cancel_redeem(uuid) is
  '탈퇴 취소 RPC. withdrawal-cancel-redeem Edge Function이 cancel_token 서명 검증 후 account_id를 넘겨 secret key로 호출. withdrawn_at을 초기화합니다.';

-- 서명 검증을 Edge Function이 담당하므로 일반 사용자에게 노출하지 않는다.
-- (authenticated에게 열면 임의 account_id로 남의 탈퇴 예약을 취소할 수 있음)
revoke all on function public.ts_withdrawal_cancel_redeem(uuid) from public;
revoke all on function public.ts_withdrawal_cancel_redeem(uuid) from anon;
revoke all on function public.ts_withdrawal_cancel_redeem(uuid) from authenticated;
grant execute on function public.ts_withdrawal_cancel_redeem(uuid) to service_role;

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
