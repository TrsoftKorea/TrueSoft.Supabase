-- 프로젝트별 커스텀 세이브 테이블을 위한 공통 인프라를 제공합니다.
-- admin_create_user_table RPC로 표준 구조(RLS·트리거 포함)의 세이브 테이블을 생성하고,
-- ts_ensure_my_row RPC로 로그인 유저의 행 존재를 보장합니다.
--
-- =============================================================================
-- 유저 세이브 공통 RPC — ts_ensure_my_row, admin_create_user_table
-- 선행: 02_profiles.sql (auth_user_server_id, ts_default_server_id)
--
-- SDK는 단일 user_saves 테이블 대신 프로젝트별 커스텀 테이블을 직접 정의합니다.
-- 이 파일은 모든 커스텀 세이브 테이블에서 공유하는 범용 RPC만 포함합니다.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_ensure_my_row — 범용 유저 세이브 행 보장 RPC
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
  '커스텀 세이브 테이블에 본인 행 보장(upsert). p_table: 대상 테이블명, p_user_id: 플레이어 고유 id.';

grant execute on function public.ts_ensure_my_row(text, text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_update_last_activity_at — data_ 테이블 UPDATE 시 profiles.last_activity_at 갱신
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
  'data_ 테이블 UPDATE 시 user_profiles.last_activity_at을 갱신합니다. admin_create_user_table이 자동 등록.';

-- 기존에 생성된 data_ 테이블에 트리거 일괄 적용
do $$
declare
  t text;
begin
  for t in
    select table_name from information_schema.tables
    where table_schema = 'public' and table_name like 'data_%'
  loop
    execute format($f$
      drop trigger if exists update_last_activity_at on public.%I;
      create trigger update_last_activity_at
      after update on public.%I
      for each row execute function public.ts_update_last_activity_at();
    $f$, t, t);
  end loop;
end;
$$;

-- ---------------------------------------------------------------------------
-- set_updated_at — updated_at 자동 갱신 트리거 함수
-- ---------------------------------------------------------------------------
-- admin_create_user_table이 생성하는 커스텀 세이브 테이블의 BEFORE UPDATE 트리거에 사용합니다.
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
  'BEFORE UPDATE 트리거: new.updated_at을 now()로 갱신합니다. admin_create_user_table이 자동 등록.';

-- ---------------------------------------------------------------------------
-- user_table_metadata — 커스텀 세이브 테이블 레지스트리
-- ---------------------------------------------------------------------------
-- admin_create_user_table 호출 시 테이블 정보를 기록합니다.
-- 클라이언트 직접 접근 없음 — RLS 활성화 + policy 없음으로 service_role 전용.
-- ---------------------------------------------------------------------------
create table if not exists public.user_table_metadata (
  table_name  text        primary key,
  description text,
  enabled     boolean     not null default true,
  updated_at  timestamptz not null default now()
);

comment on table public.user_table_metadata is
  '커스텀 세이브 테이블 레지스트리. admin_create_user_table이 자동 기록. service_role 전용.';

alter table public.user_table_metadata enable row level security;
-- [의도적] policy 없음 — service_role 전용. authenticated 접근 불필요.

-- ---------------------------------------------------------------------------
-- admin_create_user_table — 유저 세이브 테이블 생성 헬퍼 (어드민 전용)
-- ---------------------------------------------------------------------------
-- 커스텀 세이브 테이블을 표준 구조로 생성합니다.
-- 생성된 테이블은 ts_ensure_my_row와 함께 동작하도록 account_id UNIQUE 제약을 포함합니다.
--
-- 파라미터:
--   p_access      — 'public'(전체 조회) | 'private'(본인만 조회)
--   p_table_name  — 테이블명 (^data_[a-z0-9_]+$ 패턴, 예: "data_basic")
--   p_description — 테이블 설명 (코멘트 및 메타데이터에 저장)
--   p_enabled     — 메타데이터 활성화 여부
--   p_columns_sql — 추가 컬럼 정의 SQL (세미콜론 금지, 예: "level int not null default 1, coins int not null default 0")
-- ---------------------------------------------------------------------------
create or replace function public.admin_create_user_table(
  p_access text,
  p_table_name text,
  p_description text,
  p_enabled boolean,
  p_columns_sql text
)
returns void
language plpgsql
security definer
set search_path to 'public', 'auth'
as $function$declare
  v_table_ident text;
  v_sql         text;
  v_pf_rec      record;
begin
  if p_access not in ('public', 'private') then
    raise exception 'Invalid access: % (expected public/private)', p_access;
  end if;

  if p_table_name !~ '^data_[a-z0-9_]+$' then
    raise exception 'Invalid table_name: % (must match ^data_[a-z0-9_]+$)', p_table_name;
  end if;

  if p_table_name = 'user_table_metadata' then
    raise exception 'Reserved table_name: %', p_table_name;
  end if;

  if position(';' in p_columns_sql) > 0 then
    raise exception 'Invalid columns_sql: semicolon is not allowed';
  end if;

  if coalesce(trim(p_columns_sql), '') = '' then
    raise exception 'columns_sql is required (additional columns)';
  end if;

  v_table_ident := quote_ident('public') || '.' || quote_ident(p_table_name);

  -- account_id unique: ts_ensure_my_row의 ON CONFLICT (account_id) 에 필수
  v_sql := format($f$
    create table if not exists %s (
      id uuid primary key default gen_random_uuid(),
      user_id text not null,
      account_id uuid not null unique references auth.users(id) on delete cascade,
      server_id uuid not null references public.game_servers(id),
      updated_at timestamptz not null default now(),
      %s
    );
  $f$, v_table_ident, p_columns_sql);

  execute v_sql;

  execute format('drop trigger if exists set_updated_at on %s', v_table_ident);
  execute format($f$
    create trigger set_updated_at
    before update on %s
    for each row
    execute function public.set_updated_at();
  $f$, v_table_ident);

  execute format('drop trigger if exists update_last_activity_at on %s', v_table_ident);
  execute format($f$
    create trigger update_last_activity_at
    after update on %s
    for each row
    execute function public.ts_update_last_activity_at();
  $f$, v_table_ident);

  execute format('alter table %s enable row level security;', v_table_ident);

  -- 재호출 안전: 기존 정책을 모두 drop 후 재생성
  execute format('drop policy if exists select_all_authenticated on %s', v_table_ident);
  execute format('drop policy if exists select_own_authenticated on %s', v_table_ident);
  if p_access = 'public' then
    execute format($f$
      create policy select_all_authenticated on %s
      for select to authenticated using (true);
    $f$, v_table_ident);
  else
    execute format($f$
      create policy select_own_authenticated on %s
      for select to authenticated using (account_id = auth.uid());
    $f$, v_table_ident);
  end if;

  execute format('drop policy if exists insert_own_authenticated on %s', v_table_ident);
  execute format($f$
    create policy insert_own_authenticated on %s
    for insert to authenticated with check (account_id = auth.uid());
  $f$, v_table_ident);

  -- update: 기본 정책 생성 후 ts_protected_fields 보호 재적용
  execute format('drop policy if exists update_own_authenticated on %s', v_table_ident);
  execute format($f$
    create policy update_own_authenticated on %s
    for update to authenticated
    using (account_id = auth.uid()) with check (account_id = auth.uid());
  $f$, v_table_ident);

  if to_regclass('public.ts_protected_fields') is not null then
    for v_pf_rec in
      select column_name, min_value
      from public.ts_protected_fields
      where table_name = p_table_name
    loop
      perform public.ts_protect_field(p_table_name, v_pf_rec.column_name, v_pf_rec.min_value);
    end loop;
  end if;

  execute format('drop policy if exists delete_own_authenticated on %s', v_table_ident);
  execute format($f$
    create policy delete_own_authenticated on %s
    for delete to authenticated using (account_id = auth.uid());
  $f$, v_table_ident);

  insert into public.user_table_metadata (table_name, description, enabled, updated_at)
  values (p_table_name, nullif(trim(p_description), ''), coalesce(p_enabled, true), now())
  on conflict (table_name) do update
  set description = excluded.description,
      enabled = excluded.enabled,
      updated_at = excluded.updated_at;

  if coalesce(trim(p_description), '') <> '' then
    execute format('comment on table %s is %L;', v_table_ident, p_description);
  end if;

end;$function$;

comment on function public.admin_create_user_table(text, text, text, boolean, text) is
  '커스텀 세이브 테이블 생성. account_id UNIQUE 포함, ts_ensure_my_row와 함께 동작.';
