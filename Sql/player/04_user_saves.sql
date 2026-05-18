-- 유저 세이브 공통 인프라를 제공합니다.
-- ts_ensure_my_row RPC로 로그인 유저의 행 존재를 보장하고,
-- set_updated_at / ts_update_last_activity_at 트리거 함수를 정의합니다.
-- 실제 세이브 테이블(user_data)은 15_user_data.sql에서 생성합니다.
--
-- =============================================================================
-- 유저 세이브 공통 RPC — ts_ensure_my_row
-- 선행: 02_profiles.sql (auth_user_server_id, ts_default_server_id)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_ensure_my_row — 유저 세이브 행 보장 RPC
-- ---------------------------------------------------------------------------
-- 지정 테이블에 본인 행이 없으면 INSERT, 있으면 user_id·updated_at만 갱신합니다.
-- p_table 식별자는 format('%I') 로 이스케이프되어 SQL 인젝션을 차단합니다.
-- 대상 테이블은 반드시 (user_id, account_id, server_id, updated_at) 컬럼과
-- account_id unique 제약을 가져야 합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_ensure_my_row(
  p_table text,
  p_user_id text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid;
  v_stable text;
  v_server_id uuid;
begin
  v_uid := auth.uid();
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  if p_table is null or trim(p_table) = '' then
    raise exception 'table_name_empty';
  end if;

  v_stable := coalesce(nullif(trim(p_user_id), ''), v_uid::text);
  v_server_id := public.ts_default_server_id();

  -- %I 로 식별자 이스케이프 → SQL 인젝션 안전. 테이블 부재 시 Postgres 런타임 오류 반환.
  execute format(
    'insert into public.%I (user_id, account_id, server_id, updated_at)
     values ($1, $2, $3, now())
     on conflict (account_id) do update set
       user_id = excluded.user_id,
       updated_at = excluded.updated_at',
    trim(p_table)
  ) using v_stable, v_uid, v_server_id;
end;
$$;

comment on function public.ts_ensure_my_row(text, text) is
  'user_data 테이블에 본인 행 보장(upsert). p_table: 대상 테이블명, p_user_id: 플레이어 고유 id.';

grant execute on function public.ts_ensure_my_row(text, text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_update_last_activity_at — user_data 테이블 UPDATE 시 profiles.last_activity_at 갱신
-- ---------------------------------------------------------------------------
create or replace function public.ts_update_last_activity_at()
returns trigger
language plpgsql
security definer
set search_path = public, auth
as $$
begin
  update public.user_profiles
  set last_activity_at = now()
  where account_id = auth.uid();
  return new;
end;
$$;

comment on function public.ts_update_last_activity_at() is
  'user_data 테이블 UPDATE 시 user_profiles.last_activity_at을 갱신합니다. 15_user_data.sql이 자동 등록.';

-- ---------------------------------------------------------------------------
-- set_updated_at — updated_at 자동 갱신 트리거 함수
-- ---------------------------------------------------------------------------
create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

comment on function public.set_updated_at() is
  'BEFORE UPDATE 트리거: new.updated_at을 now()로 갱신합니다. 15_user_data.sql이 자동 등록.';
