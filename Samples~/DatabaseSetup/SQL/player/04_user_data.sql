-- 유저 세이브 공통 인프라, user_data 테이블, 선택적 필드 보호를 제공합니다.
-- ts_ensure_my_row RPC로 로그인 유저의 행 존재를 보장하고,
-- admin_add_user_data_column RPC로 컬럼을 무중단 추가합니다.
--
-- =============================================================================
-- 유저 세이브 — 공통 인프라 + user_data 테이블 + 필드 보호
-- 선행: 02_profiles.sql (user_profiles, auth_user_server_id, ts_default_server_id)
-- =============================================================================


-- =============================================================================
-- 공통 인프라 — 트리거 함수 + ts_ensure_my_row
-- =============================================================================

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
  'BEFORE UPDATE 트리거: new.updated_at을 now()로 갱신합니다.';

-- ---------------------------------------------------------------------------
-- ts_update_last_activity_at — user_data UPDATE 시 profiles.last_activity_at 갱신
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
  'user_data 테이블 UPDATE 시 user_profiles.last_activity_at을 갱신합니다.';

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


-- =============================================================================
-- user_data — 표준 유저 세이브 테이블 (단일 테이블 방식)
-- =============================================================================
-- 모든 게임 세이브 데이터를 하나의 테이블에 저장합니다.
-- 새 컬럼은 admin_add_user_data_column RPC로 추가합니다.
-- [append-only 정책] 컬럼 삭제·이름 변경은 금지합니다.

create table if not exists public.user_data (
  id         uuid        primary key default gen_random_uuid(),
  user_id    text        not null,
  account_id uuid        not null unique references auth.users(id) on delete cascade,
  server_id  uuid        not null references public.game_servers(id),
  updated_at timestamptz not null default now(),

  -- ─── 게임 데이터 컬럼 ─────────────────────────────────────────────────────
  -- 새 컬럼 추가: SELECT admin_add_user_data_column('컬럼명', '타입 NOT NULL DEFAULT 값');
  -- [append-only 정책] 컬럼 삭제·이름 변경은 클라이언트 호환성 문제를 유발하므로 금지합니다.
  level      int         not null default 1,
  coins      int         not null default 0
);

comment on table public.user_data is
  '표준 유저 세이브 테이블. 모든 게임 데이터를 단일 테이블에 저장. Append-only 정책.';

-- ---------------------------------------------------------------------------
-- RLS
-- ---------------------------------------------------------------------------
alter table public.user_data enable row level security;

drop policy if exists select_own_authenticated on public.user_data;
create policy select_own_authenticated on public.user_data
  for select to authenticated using (account_id = auth.uid());

drop policy if exists insert_own_authenticated on public.user_data;
create policy insert_own_authenticated on public.user_data
  for insert to authenticated with check (account_id = auth.uid());

drop policy if exists update_own_authenticated on public.user_data;
create policy update_own_authenticated on public.user_data
  for update to authenticated
  using  (account_id = auth.uid())
  with check (account_id = auth.uid());

drop policy if exists delete_own_authenticated on public.user_data;
create policy delete_own_authenticated on public.user_data
  for delete to authenticated using (account_id = auth.uid());

grant select, insert, update, delete on public.user_data to authenticated;

-- ---------------------------------------------------------------------------
-- 트리거
-- ---------------------------------------------------------------------------
drop trigger if exists set_updated_at on public.user_data;
create trigger set_updated_at
  before update on public.user_data
  for each row execute function public.set_updated_at();

drop trigger if exists update_last_activity_at on public.user_data;
create trigger update_last_activity_at
  after update on public.user_data
  for each row execute function public.ts_update_last_activity_at();

-- ---------------------------------------------------------------------------
-- admin_add_user_data_column — user_data 테이블에 컬럼 추가 (어드민 전용)
-- ---------------------------------------------------------------------------
-- 새 게임 데이터 컬럼을 서비스 중 무중단으로 추가합니다.
-- 이미 존재하는 컬럼이면 아무것도 하지 않습니다(IF NOT EXISTS).
-- [append-only 정책] 컬럼 삭제·이름 변경은 지원하지 않습니다.
--
-- 파라미터:
--   p_column_name — 컬럼명 (^[a-z][a-z0-9_]*$ 패턴, 예약명 금지)
--   p_column_def  — 컬럼 정의 (세미콜론 금지, 예: 'int not null default 0')
--
-- 사용 예:
--   SELECT admin_add_user_data_column('exp',           'int not null default 0');
--   SELECT admin_add_user_data_column('last_login_at', 'timestamptz');
-- ---------------------------------------------------------------------------
create or replace function public.admin_add_user_data_column(
  p_column_name text,
  p_column_def  text
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_reserved text[] := array['id','user_id','account_id','server_id','updated_at'];
begin
  if p_column_name is null or p_column_name !~ '^[a-z][a-z0-9_]*$' then
    raise exception 'Invalid column_name: % (must match ^[a-z][a-z0-9_]*$)', p_column_name;
  end if;

  if p_column_name = any(v_reserved) then
    raise exception 'Reserved column_name: %', p_column_name;
  end if;

  if position(';' in coalesce(p_column_def, '')) > 0 then
    raise exception 'Invalid column_def: semicolon is not allowed';
  end if;

  if coalesce(trim(p_column_def), '') = '' then
    raise exception 'column_def is required';
  end if;

  execute format(
    'alter table public.user_data add column if not exists %I %s',
    p_column_name, p_column_def
  );
end;
$$;

comment on function public.admin_add_user_data_column(text, text) is
  'user_data 테이블에 컬럼 추가. 이미 존재하면 무시(IF NOT EXISTS). append-only 정책.';
-- [의도적] grant 없음 — service_role 전용.


-- ---------------------------------------------------------------------------
-- admin_add_user_data_column (구조화 인자판) — Retool dataManager/addColumn.ts 전용
-- ---------------------------------------------------------------------------
-- 컬럼명·타입·nullable·default를 분리해 받습니다. 이미 존재하면 오류.
--   SELECT admin_add_user_data_column('exp', 'int', false, '0');
-- ---------------------------------------------------------------------------
create or replace function public.admin_add_user_data_column(
  p_colname     text,
  p_coltype     text,
  p_nullable    boolean,
  p_default_sql text
)
returns void
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
  colname        text := nullif(btrim(p_colname), '');
  coltype        text := nullif(btrim(p_coltype), '');
  default_sql    text := nullif(btrim(p_default_sql), '');
  notnull_sql    text;
  default_clause text;
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if coltype is null then
    raise exception 'Column type is required';
  end if;
  if colname !~ '^[a-z_][a-z0-9_]*$' then
    raise exception 'Invalid column name: %', colname;
  end if;
  if colname in ('id','user_id','account_id','server_id','updated_at') then
    raise exception 'Reserved column name: %', colname;
  end if;
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'user_data' and column_name = colname
  ) then
    raise exception 'Column already exists: %', colname;
  end if;

  notnull_sql    := case when p_nullable then '' else 'not null' end;
  default_clause := case when default_sql is null then '' else 'default ' || default_sql end;

  execute format(
    'alter table public.user_data add column %I %s %s %s',
    colname, coltype, notnull_sql, default_clause
  );
end;
$$;

comment on function public.admin_add_user_data_column(text, text, boolean, text) is
  'user_data 컬럼 추가(구조화 인자판, Retool 전용). 이미 존재하면 오류.';
-- [의도적] grant 없음 — service_role 전용.


-- ---------------------------------------------------------------------------
-- admin_update_user_data_column — user_data 컬럼의 NOT NULL/DEFAULT 변경 (어드민/Retool 전용)
-- ---------------------------------------------------------------------------
-- 예약 컬럼은 거부, 존재하지 않으면 오류. default_sql 에 세미콜론 금지(인젝션 차단).
--   SELECT admin_update_user_data_column('exp', true,  null);   -- NULL 허용 + default 제거
--   SELECT admin_update_user_data_column('exp', false, '0');    -- NOT NULL + default 0
-- ---------------------------------------------------------------------------
create or replace function public.admin_update_user_data_column(
  p_colname     text,
  p_nullable    boolean,
  p_default_sql text
)
returns void
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
  colname     text := nullif(btrim(p_colname), '');
  default_sql text := nullif(btrim(p_default_sql), '');
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if colname !~ '^[a-z_][a-z0-9_]*$' then
    raise exception 'Invalid column name: %', colname;
  end if;
  if colname in ('id','user_id','account_id','server_id','created_at','updated_at') then
    raise exception 'Reserved column name: %', colname;
  end if;
  if not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'user_data' and column_name = colname
  ) then
    raise exception 'Column does not exist: %', colname;
  end if;
  if position(';' in coalesce(p_default_sql, '')) > 0 then
    raise exception 'Invalid default: semicolon is not allowed';
  end if;

  if p_nullable then
    execute format('alter table public.user_data alter column %I drop not null', colname);
  else
    execute format('alter table public.user_data alter column %I set not null', colname);
  end if;

  if default_sql is null then
    execute format('alter table public.user_data alter column %I drop default', colname);
  else
    execute format('alter table public.user_data alter column %I set default %s', colname, default_sql);
  end if;
end;
$$;

comment on function public.admin_update_user_data_column(text, boolean, text) is
  'user_data 컬럼의 NOT NULL/DEFAULT 변경(어드민/Retool 전용). 예약 컬럼 거부.';
-- [의도적] grant 없음 — service_role 전용.


-- ---------------------------------------------------------------------------
-- admin_drop_user_data_column — user_data 컬럼 삭제 (어드민/Retool 전용)
-- ---------------------------------------------------------------------------
-- 예약 컬럼은 거부, 존재하지 않으면 오류. (append-only 정책의 예외 — 운영 도구 전용)
--   SELECT admin_drop_user_data_column('exp');
-- ---------------------------------------------------------------------------
create or replace function public.admin_drop_user_data_column(p_colname text)
returns void
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
  colname text := nullif(btrim(p_colname), '');
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if colname !~ '^[a-z_][a-z0-9_]*$' then
    raise exception 'Invalid column name: %', colname;
  end if;
  if colname in ('id','user_id','account_id','server_id','created_at','updated_at') then
    raise exception 'Reserved column name: %', colname;
  end if;
  if not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'user_data' and column_name = colname
  ) then
    raise exception 'Column does not exist: %', colname;
  end if;

  execute format('alter table public.user_data drop column %I', colname);
end;
$$;

comment on function public.admin_drop_user_data_column(text) is
  'user_data 컬럼 삭제(어드민/Retool 전용). 예약 컬럼 거부, 미존재 시 오류.';
-- [의도적] grant 없음 — service_role 전용.


-- =============================================================================
-- 필드 보호 (선택) — 클라이언트 증가 차단 + 최솟값 제약
-- =============================================================================
-- SDK 사용자가 원하는 컬럼에 클라이언트 증가 차단 및 최솟값 제약을
-- 한 번의 함수 호출로 적용할 수 있는 범용 헬퍼를 제공합니다.
--
-- 사용 예:
--   SELECT ts_protect_field('user_data', 'coins');    -- 0 이상, 클라이언트 증가 불가
--   SELECT ts_protect_field('user_data', 'gems');     -- 동일
--   SELECT ts_unprotect_field('user_data', 'coins');  -- 해제

-- ---------------------------------------------------------------------------
-- 보호 설정 테이블
-- ---------------------------------------------------------------------------
create table if not exists public.ts_protected_fields (
    table_name  text    not null,
    column_name text    not null,
    min_value   numeric not null default 0,
    primary key (table_name, column_name)
);

comment on table public.ts_protected_fields is
    '필드 보호 설정. ts_protect_field() / ts_unprotect_field() 로 관리.';

-- 클라이언트는 조회만 가능, 변경은 service_role(Edge Function 등)만
alter table public.ts_protected_fields enable row level security;

drop policy if exists ts_protected_fields_select on public.ts_protected_fields;
create policy ts_protected_fields_select on public.ts_protected_fields
    for select to authenticated using (true);

grant select on public.ts_protected_fields to authenticated;

-- ---------------------------------------------------------------------------
-- ts_protect_field — 필드 보호 적용
-- ---------------------------------------------------------------------------
-- ① ts_protected_fields 에 설정 저장 (upsert)
-- ② CHECK 제약 추가 (column >= min_value)
-- ③ 이 테이블의 모든 보호 컬럼을 포함해 update_own_authenticated RLS 정책 재구성
--    (클라이언트 PATCH로 보호 컬럼 증가 시도 시 HTTP 403)
-- ---------------------------------------------------------------------------
create or replace function public.ts_protect_field(
    p_table  text,
    p_column text,
    p_min    numeric default 0
) returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_ti   text    := quote_ident(trim(p_table));
    v_ci   text    := quote_ident(trim(p_column));
    v_chk  text[]  := array['account_id = auth.uid()'];
    v_rec  record;
    v_con  text;
begin
    -- ① 설정 저장
    insert into public.ts_protected_fields (table_name, column_name, min_value)
    values (trim(p_table), trim(p_column), p_min)
    on conflict (table_name, column_name) do update
        set min_value = excluded.min_value;

    -- ② CHECK 제약 추가/교체
    v_con := format('%s_%s_min_check', trim(p_table), trim(p_column));
    execute format('alter table public.%s drop constraint if exists %I', v_ti, v_con);
    execute format('alter table public.%s add constraint %I check (%s >= %s)',
        v_ti, v_con, v_ci, p_min);

    -- ③ 이 테이블의 모든 보호 컬럼으로 WITH CHECK 절 동적 구성
    for v_rec in
        select column_name
        from public.ts_protected_fields
        where table_name = trim(p_table)
        order by column_name
    loop
        v_chk := v_chk || format(
            '%s <= (select t.%s from public.%s t where t.account_id = auth.uid())',
            quote_ident(v_rec.column_name),
            quote_ident(v_rec.column_name),
            v_ti);
    end loop;

    -- ④ RLS UPDATE 정책 재구성
    execute format('drop policy if exists update_own_authenticated on public.%s', v_ti);
    execute format(
        'create policy update_own_authenticated on public.%s
         for update to authenticated
         using (account_id = auth.uid())
         with check (%s)',
        v_ti,
        array_to_string(v_chk, E'\n        and '));
end;
$$;

comment on function public.ts_protect_field(text, text, numeric) is
    '필드 보호 적용. CHECK 제약(min_value 이상) + RLS WITH CHECK(클라이언트 증가 차단)을 자동 구성.';

grant execute on function public.ts_protect_field(text, text, numeric) to service_role;
revoke execute on function public.ts_protect_field(text, text, numeric) from authenticated;
revoke execute on function public.ts_protect_field(text, text, numeric) from anon;

-- ---------------------------------------------------------------------------
-- ts_unprotect_field — 필드 보호 해제
-- ---------------------------------------------------------------------------
-- ① ts_protected_fields 에서 설정 삭제
-- ② CHECK 제약 제거
-- ③ 남은 보호 컬럼만으로 RLS 정책 재구성 (보호 컬럼 없으면 기본 정책으로 복원)
-- ---------------------------------------------------------------------------
create or replace function public.ts_unprotect_field(
    p_table  text,
    p_column text
) returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_ti   text   := quote_ident(trim(p_table));
    v_chk  text[] := array['account_id = auth.uid()'];
    v_rec  record;
    v_con  text;
begin
    -- ① 설정 삭제
    delete from public.ts_protected_fields
    where table_name = trim(p_table) and column_name = trim(p_column);

    -- ② CHECK 제약 제거
    v_con := format('%s_%s_min_check', trim(p_table), trim(p_column));
    execute format('alter table public.%s drop constraint if exists %I', v_ti, v_con);

    -- ③ 남은 보호 컬럼으로 WITH CHECK 재구성
    for v_rec in
        select column_name
        from public.ts_protected_fields
        where table_name = trim(p_table)
        order by column_name
    loop
        v_chk := v_chk || format(
            '%s <= (select t.%s from public.%s t where t.account_id = auth.uid())',
            quote_ident(v_rec.column_name),
            quote_ident(v_rec.column_name),
            v_ti);
    end loop;

    -- ④ RLS UPDATE 정책 재구성 (보호 컬럼 없으면 기본 정책으로 복원)
    execute format('drop policy if exists update_own_authenticated on public.%s', v_ti);
    execute format(
        'create policy update_own_authenticated on public.%s
         for update to authenticated
         using (account_id = auth.uid())
         with check (%s)',
        v_ti,
        array_to_string(v_chk, E'\n        and '));
end;
$$;

comment on function public.ts_unprotect_field(text, text) is
    '필드 보호 해제. CHECK 제약 제거 + RLS 정책 재구성. 보호 컬럼이 없으면 기본 정책으로 복원.';

grant execute on function public.ts_unprotect_field(text, text) to service_role;
revoke execute on function public.ts_unprotect_field(text, text) from authenticated;
revoke execute on function public.ts_unprotect_field(text, text) from anon;
