-- TrueBase SDK — 데이터베이스 설치 스크립트
--
-- Supabase SQL Editor 에 이 파일 전체를 붙여넣고 한 번 실행하면 스키마가 모두 설치됩니다.
-- 전부 멱등이라 다시 실행해도 안전합니다.
--
-- [권한 최소화는 반드시 맨 끝]
--   마지막 절은 public 스키마의 모든 테이블·함수 권한을 회수한 뒤 필요한 것만 되돌려 줍니다.
--   앞 절에서 만든 함수를 이름으로 grant 하므로, 순서를 바꾸면 "함수가 없다"며 실패합니다.
--
-- 설치 후 verify.sql 로 확인하세요.
-- =============================================================================



-- #############################################################################
-- 01. 게임 서버 목록
-- #############################################################################

-- 게임 서버 목록을 관리합니다.
-- 클라이언트는 서버 목록을 조회하고, SDK는 플레이어의 기본 서버를 자동 선택합니다.
--
-- =============================================================================
-- 플레이어 스키마 — game_servers + ts_default_server_id + ts_server_now
-- 선행: 없음
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 보안 하드닝 — public 스키마에 새 테이블 생성 시 RLS 자동 활성화
-- ---------------------------------------------------------------------------
-- 실수로 RLS 없이 테이블을 만들어 전면 노출되는 것을 막는 이벤트 트리거.
-- DDL(CREATE TABLE) 직후 해당 테이블에 enable row level security를 적용한다.
-- ---------------------------------------------------------------------------
create or replace function public.rls_auto_enable()
returns event_trigger language plpgsql security definer set search_path to 'pg_catalog' as $function$
declare cmd record;
begin
  for cmd in select * from pg_event_trigger_ddl_commands()
    where command_tag in ('CREATE TABLE','CREATE TABLE AS','SELECT INTO') and object_type in ('table','partitioned table')
  loop
    if cmd.schema_name is not null and cmd.schema_name in ('public') and cmd.schema_name not in ('pg_catalog','information_schema') and cmd.schema_name not like 'pg_toast%' and cmd.schema_name not like 'pg_temp%' then
      begin
        execute format('alter table if exists %s enable row level security', cmd.object_identity);
        raise log 'rls_auto_enable: enabled RLS on %', cmd.object_identity;
      exception when others then raise log 'rls_auto_enable: failed to enable RLS on %', cmd.object_identity;
      end;
    else raise log 'rls_auto_enable: skip % (either system schema or not in enforced list: %.)', cmd.object_identity, cmd.schema_name;
    end if;
  end loop;
end; $function$;

drop event trigger if exists ensure_rls;
create event trigger ensure_rls on ddl_command_end execute function public.rls_auto_enable();

-- ---------------------------------------------------------------------------
-- game_servers (서버/월드 마스터)
-- ---------------------------------------------------------------------------
create table if not exists public.game_servers (
  id uuid primary key default gen_random_uuid(),
  server_code text not null,
  display_name text not null,
  allow_new_signups boolean not null default true,
  allow_transfers boolean not null default true,
  created_at timestamptz not null default now()
);

alter table public.game_servers add column if not exists id uuid;
alter table public.game_servers add column if not exists server_code text;
alter table public.game_servers add column if not exists display_name text;
alter table public.game_servers add column if not exists allow_new_signups boolean not null default true;
alter table public.game_servers add column if not exists allow_transfers boolean not null default true;
alter table public.game_servers add column if not exists created_at timestamptz not null default now();

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'game_servers'
      and c.conname = 'game_servers_server_code_key'
  ) then
    alter table public.game_servers
      add constraint game_servers_server_code_key unique (server_code);
  end if;
end $$;

insert into public.game_servers (server_code, display_name)
select 'GLOBAL', 'Global'
where not exists (
  select 1 from public.game_servers where server_code = 'GLOBAL'
);

create or replace function public.ts_default_server_id()
returns uuid
language sql
stable
security definer
set search_path = public
as $$
  select gs.id
  from public.game_servers gs
  order by
    case when gs.server_code = 'GLOBAL' then 0 else 1 end,
    gs.created_at,
    gs.id
  limit 1;
$$;

comment on function public.ts_default_server_id() is
  '기본 game_servers 행 id. 클라이언트 프로필 upsert 시 server_id 채움·RLS 호환용으로 authenticated 에서 호출 가능.';

grant execute on function public.ts_default_server_id() to anon, authenticated;

comment on table public.game_servers is '게임 서버(월드) 마스터.';
comment on column public.game_servers.server_code is '클라이언트에서 선택/표시하는 고유 코드(예: GLOBAL, KR1).';

alter table public.game_servers enable row level security;
drop policy if exists "game_servers_select_public" on public.game_servers;
create policy "game_servers_select_public"
on public.game_servers for select
using (true);

-- ---------------------------------------------------------------------------
-- ts_server_now — 서버 현재 시각 반환
-- ---------------------------------------------------------------------------
-- 클라이언트 시각 조작 방지용. clock_timestamp()를 사용하여 트랜잭션 내에서도
-- 실제 현재 시각을 반환합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_server_now()
returns table(server_time timestamptz)
language sql
stable
security definer
set search_path = public
as $$
  select clock_timestamp() as server_time;
$$;

comment on function public.ts_server_now() is
  '서버 현재 시각 반환. 클라이언트 시각 조작 방지용.';

grant execute on function public.ts_server_now() to anon, authenticated;


-- #############################################################################
-- 02. 플레이어 프로필 · 닉네임 · 세션
-- #############################################################################

-- 플레이어 공개 프로필, 표시 이름(닉네임), 세션을 관리합니다.
-- 로그인 시 자동으로 프로필 행을 생성하며, 모든 파생 테이블의 server_id 기준이 됩니다.
-- 서버별 유니크 닉네임과 중복 로그인 감지를 함께 제공합니다.
--
-- =============================================================================
-- 플레이어 스키마 — user_profiles + display_names + user_sessions + RLS
-- 선행: 01_servers.sql
-- =============================================================================


-- =============================================================================
-- user_profiles — 플레이어 공개 프로필
-- =============================================================================

create table if not exists public.user_profiles (
  id uuid primary key default gen_random_uuid(),
  user_id text not null,
  account_id uuid unique references auth.users (id) on delete set null,
  server_id uuid references public.game_servers (id) on delete restrict,
  withdrawn_at timestamptz null,
  last_activity_at timestamptz default now(),
  country_code text null,
  platform     text null,
  total_paid_krw bigint not null default 0
);

-- 기존 DB에 컬럼만 없을 때 보강(신규 생성 테이블에서는 IF NOT EXISTS 로 무시됨)
alter table public.user_profiles add column if not exists user_id text;
alter table public.user_profiles add column if not exists account_id uuid;
alter table public.user_profiles add column if not exists server_id uuid;
alter table public.user_profiles add column if not exists withdrawn_at timestamptz;
alter table public.user_profiles add column if not exists last_activity_at timestamptz default now();
alter table public.user_profiles add column if not exists country_code text;
alter table public.user_profiles add column if not exists platform text;
alter table public.user_profiles add column if not exists total_paid_krw bigint not null default 0;

update public.user_profiles p
set server_id = public.ts_default_server_id()
where p.server_id is null;

alter table public.user_profiles
  alter column server_id set default public.ts_default_server_id();

do $$
begin
  if exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'user_profiles'
      and column_name = 'server_id'
      and is_nullable = 'YES'
  ) then
    alter table public.user_profiles
      alter column server_id set not null;
  end if;
exception
  when others then
    raise notice 'profiles.server_id SET NOT NULL skipped: %', sqlerrm;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'user_profiles'
      and c.conname = 'profiles_account_id_fkey'
  ) then
    alter table public.user_profiles
      add constraint profiles_account_id_fkey
      foreign key (account_id) references auth.users (id) on delete set null;
  end if;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'user_profiles'
      and c.conname = 'profiles_account_id_key'
  ) then
    alter table public.user_profiles
      add constraint profiles_account_id_key unique (account_id);
  end if;
end $$;

-- Google 등 OAuth subject(sub)는 UUID 형식이 아닐 수 있어 user_id는 text로 통일합니다.
do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'user_profiles'
      and column_name = 'user_id' and udt_name = 'uuid'
  ) then
    alter table public.user_profiles alter column user_id type text using user_id::text;
  end if;
end $$;

comment on table public.user_profiles is '공개 프로필. 게임 RLS는 account_id. 운영 조회는 user_id.';
comment on column public.user_profiles.user_id is '플레이어 고유 id (동일 Google 등이면 재가입 후에도 동일 값 가능).';
comment on column public.user_profiles.account_id is 'auth.users.id. 탈퇴 시 NULL. 게임 조회·수정 기준.';
comment on column public.user_profiles.server_id is '플레이어가 속한 서버 id (public.game_servers.id).';
comment on column public.user_profiles.withdrawn_at is '탈퇴 표시 시각 (운영 정책에 따라 설정/해제 가능).';
comment on column public.user_profiles.last_activity_at is '마지막 게임 활동 시각. Retool 운영 대시보드용 활동 추적.';
comment on column public.user_profiles.country_code is '최초 가입 시 Cloudflare CF-IPCountry 헤더에서 기록한 ISO 3166-1 alpha-2 국가 코드. 운영 대시보드용.';
comment on column public.user_profiles.platform is '가장 최근 로그인 시 클라이언트가 전달한 플랫폼 (android, ios, windows, macos, webgl 등). 매 로그인마다 갱신.';
comment on column public.user_profiles.total_paid_krw is '누적 결제금액(KRW). purchases INSERT 트리거(07_purchases.sql)가 원자적으로 증분. Retool 운영 대시보드용.';

create index if not exists profiles_user_id_idx on public.user_profiles (user_id);
create index if not exists profiles_server_id_idx on public.user_profiles (server_id);

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'user_profiles'
      and c.conname = 'profiles_server_id_fkey'
  ) then
    alter table public.user_profiles
      add constraint profiles_server_id_fkey
      foreign key (server_id) references public.game_servers (id) on delete restrict;
  end if;
end $$;

create or replace function public.auth_user_server_id()
returns uuid
language sql
stable
security definer
set search_path = public
as $$
  select p.server_id
  from public.user_profiles p
  where p.account_id = auth.uid()
    and p.account_id is not null
  limit 1;
$$;

create or replace function public.ts_my_server_id()
returns table(server_id uuid, server_code text)
language sql
stable
security definer
set search_path = public
as $$
  select p.server_id, gs.server_code
  from public.user_profiles p
  join public.game_servers gs on gs.id = p.server_id
  where p.account_id = auth.uid()
    and p.account_id is not null
  limit 1;
$$;

alter table public.user_profiles enable row level security;

drop policy if exists "profiles_select_public" on public.user_profiles;
drop policy if exists "profiles_insert_own" on public.user_profiles;
drop policy if exists "profiles_update_own" on public.user_profiles;

create policy "profiles_select_public"
on public.user_profiles for select
using (
  auth.uid() is not null
  and server_id is not null
  and server_id = public.auth_user_server_id()
);

-- server_id 는 DEFAULT(ts_default_server_id()) 로 채워지나, PostgREST JSON upsert 시 RLS WITH CHECK 가
-- 기본값 적용 전에 평가되면 server_id 가 null 로 보여 42501 이 날 수 있음 → insert 정책에서는 account_id 만 검증.
create policy "profiles_insert_own"
on public.user_profiles for insert
with check (
  account_id is not null
  and account_id = auth.uid()
);

create policy "profiles_update_own"
on public.user_profiles for update
using (account_id is not null and account_id = auth.uid())
with check (server_id is not null);

grant select, insert, update on public.user_profiles to authenticated;

-- PostgREST upsert(merge-duplicates) UPDATE 분기에서 기존 server_id가 NULL이면 WITH CHECK(server_id is not null)가 계속 실패(42501→403).
-- INSERT 시에도 JSON에 server_id가 없을 때 RLS/기본값 평가 순서에 따라 NULL로 남는 경우가 있어 BEFORE에서 보강.
create or replace function public.ts_profiles_coalesce_server_id()
returns trigger
language plpgsql
security invoker
set search_path = public
as $$
begin
  if new.server_id is null then
    new.server_id := public.ts_default_server_id();
  end if;
  return new;
end;
$$;

comment on function public.ts_profiles_coalesce_server_id() is
  'profiles 행 INSERT·UPDATE 직전 server_id가 NULL이면 ts_default_server_id()로 채움. ensure-profile upsert·RLS 호환.';

drop trigger if exists trg_profiles_coalesce_server_id on public.user_profiles;
create trigger trg_profiles_coalesce_server_id
before insert or update on public.user_profiles
for each row
execute function public.ts_profiles_coalesce_server_id();

-- 클라이언트 ensure-profile: PostgREST upsert만으로는 RLS/병합 순서에 따라 42501이 남을 수 있어 RPC로 통일.
-- account_id는 항상 auth.uid()만 사용(클라이언트 조작 불가). user_id는 p_user_id 또는 uid 문자열.
drop function if exists public.ts_ensure_my_profile(text);
create or replace function public.ts_ensure_my_profile(
  p_user_id  text default null,
  p_platform text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid     uuid;
  v_stable  text;
  v_server  uuid;
  v_country text;
begin
  v_uid := auth.uid();
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  v_stable := coalesce(nullif(trim(p_user_id), ''), v_uid::text);
  v_server := public.ts_default_server_id();

  -- CF-IPCountry 헤더에서 국가 코드 추출 (PostgREST request context)
  -- 'XX' = Cloudflare 미판정, 없으면 NULL (로컬 개발 환경 등)
  begin
    v_country := nullif(trim(
      current_setting('request.headers', true)::jsonb->>'cf-ipcountry'
    ), '');
    if v_country = 'XX' then v_country := null; end if;
  exception when others then
    v_country := null;
  end;

  insert into public.user_profiles (user_id, account_id, withdrawn_at, server_id, country_code, platform, last_activity_at)
  values (v_stable, v_uid, null, v_server, v_country, nullif(trim(coalesce(p_platform, '')), ''), now())
  on conflict (account_id) do update set
    user_id          = excluded.user_id,
    withdrawn_at     = excluded.withdrawn_at,
    server_id        = coalesce(user_profiles.server_id, excluded.server_id),
    country_code     = coalesce(user_profiles.country_code, excluded.country_code),
    platform         = excluded.platform,
    last_activity_at = now();  -- 로그인 시각도 활동으로 기록(데이터 저장 트리거와 별개)
end;
$$;

comment on function public.ts_ensure_my_profile(text, text) is
  '로그인 직후 본인 profiles 행 보장(upsert). SECURITY DEFINER. SDK EnsureMyProfileRowAsync 가 호출.';

grant execute on function public.ts_ensure_my_profile(text, text) to authenticated;

-- nickname은 auth.user_metadata.displayName으로 이동했으므로 profiles에 두지 않습니다.


-- =============================================================================
-- display_names — 서버별 유니크 닉네임
-- =============================================================================

-- ---------------------------------------------------------------------------
-- display_names (닉네임 유니크/조회용)
-- - 닉네임 원본은 Auth user metadata(displayName)가 소스이며,
--   DB에서는 유니크 강제/가벼운 공개 조회를 위해 별도 테이블로 관리합니다.
-- ---------------------------------------------------------------------------
create table if not exists public.display_names (
  account_id uuid primary key references auth.users (id) on delete cascade,
  user_id text not null,
  server_id uuid references public.game_servers (id) on delete restrict,
  display_name text not null,
  updated_at timestamptz not null default now()
);

alter table public.display_names add column if not exists account_id uuid;
alter table public.display_names add column if not exists user_id text;
alter table public.display_names add column if not exists server_id uuid;
alter table public.display_names add column if not exists display_name text;
alter table public.display_names add column if not exists updated_at timestamptz not null default now();

update public.display_names d
set server_id = coalesce(p.server_id, public.ts_default_server_id())
from public.user_profiles p
where p.account_id = d.account_id
  and d.server_id is null;

update public.display_names d
set server_id = public.ts_default_server_id()
where d.server_id is null;

do $$
begin
  if exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'display_names'
      and column_name = 'server_id'
      and is_nullable = 'YES'
  ) then
    alter table public.display_names
      alter column server_id set not null;
  end if;
exception
  when others then
    raise notice 'display_names.server_id SET NOT NULL skipped: %', sqlerrm;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'display_names'
      and c.conname = 'display_names_server_id_fkey'
  ) then
    alter table public.display_names
      add constraint display_names_server_id_fkey
      foreign key (server_id) references public.game_servers (id) on delete restrict;
  end if;
end $$;

do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'display_names'
      and column_name = 'user_id' and udt_name = 'uuid'
  ) then
    alter table public.display_names alter column user_id type text using user_id::text;
  end if;
end $$;

comment on table public.display_names is '닉네임 전역 유니크/조회용. 정본은 auth.user_metadata.displayName, 이 테이블은 그 미러(seeding 트리거 + displayname-set).';
comment on column public.display_names.account_id is 'auth.users.id (RLS: auth.uid()).';
comment on column public.display_names.user_id is '플레이어 안정 id (profiles.user_id와 동일 값).';
comment on column public.display_names.server_id is '표시 이름이 속한 서버 id.';
comment on column public.display_names.display_name is '표시용 닉네임(원문). 유니크 인덱스는 lower(trim(...)) 기준.';

create index if not exists display_names_user_id_idx on public.display_names (user_id);
create index if not exists display_names_server_id_idx on public.display_names (server_id);

alter table public.display_names enable row level security;

drop policy if exists "display_names_select_public" on public.display_names;
drop policy if exists "display_names_select_own" on public.display_names;
drop policy if exists "display_names_insert_own" on public.display_names;
drop policy if exists "display_names_update_own" on public.display_names;

-- 직접 REST 조회는 본인 행만. 남의 닉네임은 displayname-get(service_role)으로만 노출한다.
create policy "display_names_select_own"
on public.display_names for select
using (account_id = auth.uid());

create policy "display_names_insert_own"
on public.display_names for insert
with check (
  account_id is not null
  and account_id = auth.uid()
  and server_id is not null
  and server_id = public.auth_user_server_id()
);

create policy "display_names_update_own"
on public.display_names for update
using (account_id is not null and account_id = auth.uid())
with check (
  server_id is not null
  and server_id = public.auth_user_server_id()
);

-- 닉네임 전역 고유(서버 무관).
create unique index if not exists display_names_display_name_unique
on public.display_names (lower(trim(display_name)))
where trim(display_name) <> '';

-- INSERT/UPDATE 시 server_id가 NULL이면 현재 유저의 server_id로 자동 보완 (profiles 패턴과 동일).
create or replace function public.ts_display_names_coalesce_server_id()
returns trigger
language plpgsql
security invoker
set search_path = public
as $$
begin
  if new.server_id is null then
    new.server_id := public.auth_user_server_id();
  end if;
  if new.server_id is null then
    new.server_id := public.ts_default_server_id();
  end if;
  return new;
end;
$$;

comment on function public.ts_display_names_coalesce_server_id() is
  'display_names INSERT/UPDATE 전 server_id NULL 보완. RLS WITH CHECK(server_id is not null) 와 호환.';

drop trigger if exists trg_display_names_coalesce_server_id on public.display_names;
create trigger trg_display_names_coalesce_server_id
before insert or update on public.display_names
for each row
execute function public.ts_display_names_coalesce_server_id();

grant select, insert, update on public.display_names to authenticated;

-- displayname-get·displayname-set Edge Function은 service_role(secret key)로 RLS를 우회해
-- 남의 닉네임까지 조회·설정한다. 플랫폼 기본 권한이 누락된 프로젝트에서도 동작하도록 명시적으로 부여한다.
grant select, insert, update on public.display_names to service_role;

-- user_profiles 생성 시 display_names 자동 seeding: user_metadata.displayName, 없으면 Player_<account8>.
-- 익명(metadata 빈 경우) metadata도 같은 값으로 채워 본인화면=조회=집계를 일치시킨다.
create or replace function public.ts_seed_display_name_for_profile()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_meta_name text;
  v_default   text;
  v_name      text;
begin
  if new.server_id is null then
    return new;
  end if;

  if exists (select 1 from public.display_names where account_id = new.account_id) then
    return new;
  end if;

  select nullif(trim(u.raw_user_meta_data->>'displayName'), '')
    into v_meta_name
  from auth.users u
  where u.id = new.account_id;

  v_default := 'Player_' || lower(left(replace(new.account_id::text, '-', ''), 8));
  v_name := coalesce(v_meta_name, v_default);

  -- 이름 충돌 시 account_id 기반 기본값으로 폴백
  if exists (
    select 1 from public.display_names d
    where lower(trim(d.display_name)) = lower(trim(v_name))
  ) then
    v_name := v_default;
  end if;

  insert into public.display_names (account_id, user_id, server_id, display_name, updated_at)
  values (new.account_id, new.user_id, new.server_id, v_name, now())
  on conflict (account_id) do nothing;

  -- metadata가 비면(익명) 같은 값으로 채움
  if v_meta_name is null then
    update auth.users
       set raw_user_meta_data = coalesce(raw_user_meta_data, '{}'::jsonb)
           || jsonb_build_object('displayName', v_name, 'full_name', v_name, 'name', v_name)
     where id = new.account_id;
  end if;

  return new;
exception when others then
  -- seeding 실패가 프로필 생성 자체를 막지 않도록 방어
  raise warning 'ts_seed_display_name_for_profile failed for %: %', new.account_id, sqlerrm;
  return new;
end;
$$;

comment on function public.ts_seed_display_name_for_profile() is
  'user_profiles INSERT 시 display_names 자동 seeding. 정본=user_metadata.displayName, 없으면 Player_<account8>. 익명은 metadata도 채움(기존 보존). SECURITY DEFINER.';

drop trigger if exists trg_seed_display_name on public.user_profiles;
create trigger trg_seed_display_name
after insert on public.user_profiles
for each row execute function public.ts_seed_display_name_for_profile();

-- 기존 설치본 1회 백필(신규 설치는 트리거가 처리 → 불필요). 필요 시 주석 해제.
-- insert into public.display_names (account_id, user_id, server_id, display_name, updated_at)
-- select p.account_id, p.user_id, p.server_id,
--        case when exists (select 1 from public.display_names d2
--                          where lower(trim(d2.display_name)) = lower(trim(nm.intended)))
--             then nm.default_name else nm.intended end,
--        now()
-- from public.user_profiles p
-- join auth.users u on u.id = p.account_id
-- left join public.display_names d on d.account_id = p.account_id
-- cross join lateral (
--   select coalesce(nullif(trim(u.raw_user_meta_data->>'displayName'),''),
--                   'Player_' || lower(left(replace(p.account_id::text,'-',''),8))) as intended,
--          'Player_' || lower(left(replace(p.account_id::text,'-',''),8)) as default_name
-- ) nm
-- where p.server_id is not null and d.account_id is null
-- on conflict (account_id) do nothing;
--
-- update auth.users u
--    set raw_user_meta_data = coalesce(u.raw_user_meta_data,'{}'::jsonb)
--        || jsonb_build_object('displayName', d.display_name, 'full_name', d.display_name, 'name', d.display_name)
-- from public.display_names d
-- where d.account_id = u.id
--   and nullif(trim(u.raw_user_meta_data->>'displayName'),'') is null;

-- ---------------------------------------------------------------------------
-- ts_is_display_name_available
-- RLS를 우회(SECURITY DEFINER)하여 닉네임 사용 가능 여부를 전역 기준으로 확인합니다.
-- display_names SELECT RLS 에 의존하지 않으므로 RLS 설정과 무관하게 정확한 결과를 반환합니다.
-- p_display_name  : 확인할 닉네임
-- p_ignore_account_id : 본인 이름 수정 시 자신의 account_id를 넘기면 중복에서 제외합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_is_display_name_available(
  p_display_name      text,
  p_ignore_account_id uuid default null
)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select not exists (
    select 1
    from public.display_names
    where lower(trim(display_name)) = lower(trim(p_display_name))
      and trim(display_name) <> ''
      and (p_ignore_account_id is null or account_id <> p_ignore_account_id)
  );
$$;

comment on function public.ts_is_display_name_available(text, uuid) is
  '닉네임 사용 가능 여부(전역 고유). SECURITY DEFINER로 RLS 우회, 대소문자 무시 비교.';

grant execute on function public.ts_is_display_name_available(text, uuid) to authenticated;


-- =============================================================================
-- user_sessions — 중복 로그인 감지
-- =============================================================================

-- ---------------------------------------------------------------------------
-- user_sessions (중복 로그인 감지 — 계정당 하나의 활성 세션 토큰)
-- SDK가 로그인 시 새 토큰을 upsert하고, 다른 기기에서 로그인하면 토큰이 바뀌어 이전 기기에서 감지합니다.
-- ---------------------------------------------------------------------------
create table if not exists public.user_sessions (
  account_id uuid primary key references auth.users (id) on delete cascade,
  server_id uuid references public.game_servers (id) on delete restrict,
  session_token uuid not null,
  updated_at timestamptz not null default now()
);

alter table public.user_sessions add column if not exists account_id uuid;
alter table public.user_sessions add column if not exists server_id uuid;
alter table public.user_sessions add column if not exists session_token uuid;
alter table public.user_sessions add column if not exists updated_at timestamptz not null default now();

update public.user_sessions s
set server_id = coalesce(p.server_id, public.ts_default_server_id())
from public.user_profiles p
where p.account_id = s.account_id
  and s.server_id is null;

update public.user_sessions s
set server_id = public.ts_default_server_id()
where s.server_id is null;

do $$
begin
  if exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'user_sessions'
      and column_name = 'server_id'
      and is_nullable = 'YES'
  ) then
    alter table public.user_sessions
      alter column server_id set not null;
  end if;
exception
  when others then
    raise notice 'user_sessions.server_id SET NOT NULL skipped: %', sqlerrm;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'user_sessions'
      and c.conname = 'user_sessions_server_id_fkey'
  ) then
    alter table public.user_sessions
      add constraint user_sessions_server_id_fkey
      foreign key (server_id) references public.game_servers (id) on delete restrict;
  end if;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'user_sessions'
      and c.conname = 'user_sessions_account_id_fkey'
  ) then
    alter table public.user_sessions
      add constraint user_sessions_account_id_fkey
      foreign key (account_id) references auth.users (id) on delete cascade;
  end if;
end $$;

comment on table public.user_sessions is '기기별 세션 식별. 최신 로그인이 이 행의 session_token을 덮어씀.';
comment on column public.user_sessions.server_id is '세션 토큰이 속한 서버 id.';
comment on column public.user_sessions.session_token is '클라이언트가 생성한 UUID. 다른 기기에서 로그인하면 값이 바뀜.';

alter table public.user_sessions enable row level security;

drop policy if exists "user_sessions_select_own" on public.user_sessions;
drop policy if exists "user_sessions_insert_own" on public.user_sessions;
drop policy if exists "user_sessions_update_own" on public.user_sessions;
drop policy if exists "user_sessions_delete_own" on public.user_sessions;

create policy "user_sessions_select_own"
on public.user_sessions for select
using (account_id = auth.uid());

create policy "user_sessions_insert_own"
on public.user_sessions for insert
with check (
  account_id = auth.uid()
  and server_id is not null
  and server_id = public.auth_user_server_id()
);

create policy "user_sessions_update_own"
on public.user_sessions for update
using (account_id = auth.uid())
with check (
  server_id is not null
  and server_id = public.auth_user_server_id()
);

create policy "user_sessions_delete_own"
on public.user_sessions for delete
using (account_id = auth.uid());

grant select, insert, update, delete on public.user_sessions to authenticated;

notify pgrst, 'reload schema';


-- #############################################################################
-- 03. 익명 계정 복구
-- #############################################################################

-- 기기 지문을 이용해 익명 계정의 refresh_token을 저장·복원합니다.
-- 앱 재설치 후에도 같은 기기라면 이전 익명 계정으로 자동 복구됩니다.
--
-- =============================================================================
-- 익명 로그인 복구 — anonymous_recovery_tokens + RPC + auth 트리거
-- 선행: 01_servers.sql, 02_profiles.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- anonymous_recovery_tokens (device-only best-effort 익명 복구)
-- 앱 재설치/로그아웃으로 로컬 refresh_token이 사라진 경우를 대비해
-- 디바이스 지문 해시 기준으로 refresh_token을 보관합니다.
-- ---------------------------------------------------------------------------
create table if not exists public.anonymous_recovery_tokens (
  fingerprint_hash text not null,
  server_id uuid not null references public.game_servers (id) on delete restrict,
  refresh_token text not null,
  account_id uuid null references auth.users (id) on delete set null,
  updated_at timestamptz not null default now(),
  primary key (fingerprint_hash, server_id)
);

alter table public.anonymous_recovery_tokens add column if not exists fingerprint_hash text;
alter table public.anonymous_recovery_tokens add column if not exists server_id uuid;
alter table public.anonymous_recovery_tokens add column if not exists refresh_token text;
alter table public.anonymous_recovery_tokens add column if not exists account_id uuid;
alter table public.anonymous_recovery_tokens add column if not exists updated_at timestamptz not null default now();

update public.anonymous_recovery_tokens t
set server_id = coalesce(p.server_id, public.ts_default_server_id())
from public.user_profiles p
where p.account_id = t.account_id
  and t.server_id is null;

update public.anonymous_recovery_tokens t
set server_id = public.ts_default_server_id()
where t.server_id is null;

do $$
begin
  if exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'anonymous_recovery_tokens'
      and column_name = 'server_id'
      and is_nullable = 'YES'
  ) then
    alter table public.anonymous_recovery_tokens
      alter column server_id set not null;
  end if;
exception
  when others then
    raise notice 'anonymous_recovery_tokens.server_id SET NOT NULL skipped: %', sqlerrm;
end $$;

do $$
begin
  if exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'anonymous_recovery_tokens'
      and c.contype = 'p'
      and c.conname <> 'anonymous_recovery_tokens_pkey'
  ) then
    -- no-op: custom PK name 환경은 건드리지 않습니다.
    null;
  end if;
end $$;

do $$
begin
  if exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'anonymous_recovery_tokens'
      and c.contype = 'p'
      and pg_get_constraintdef(c.oid) not ilike '%(fingerprint_hash, server_id)%'
  ) then
    alter table public.anonymous_recovery_tokens
      drop constraint if exists anonymous_recovery_tokens_pkey;
  end if;

  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'anonymous_recovery_tokens'
      and c.contype = 'p'
      and pg_get_constraintdef(c.oid) ilike '%(fingerprint_hash, server_id)%'
  ) then
    alter table public.anonymous_recovery_tokens
      add constraint anonymous_recovery_tokens_pkey primary key (fingerprint_hash, server_id);
  end if;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'anonymous_recovery_tokens'
      and c.conname = 'anonymous_recovery_tokens_account_id_fkey'
  ) then
    alter table public.anonymous_recovery_tokens
      add constraint anonymous_recovery_tokens_account_id_fkey
      foreign key (account_id) references auth.users (id) on delete set null;
  end if;
end $$;

comment on table public.anonymous_recovery_tokens is
  'device-only 익명 복구용 refresh_token 저장소(best-effort). 탈퇴 요청(ts_request_withdrawal→ts_delete_my_anon_recovery_tokens), auth.users 삭제/익명해제, auth.identities에 비익명 provider 추가 시 해당 account 행 자동 삭제.';
comment on column public.anonymous_recovery_tokens.fingerprint_hash is '클라이언트가 만든 SHA-256 해시 지문.';
comment on column public.anonymous_recovery_tokens.server_id is '복구 토큰이 속한 서버 id.';
comment on column public.anonymous_recovery_tokens.refresh_token is '복구용 refresh_token.';

create index if not exists anonymous_recovery_tokens_account_id_idx
on public.anonymous_recovery_tokens (account_id)
where account_id is not null;

create index if not exists anonymous_recovery_tokens_server_id_idx
on public.anonymous_recovery_tokens (server_id);

alter table public.anonymous_recovery_tokens enable row level security;

-- 정책은 두지 않고 RPC(SECURITY DEFINER)로만 접근합니다.

-- p_server_code 추가 이전 구버전 오버로드 제거 (PostgREST 404 방지)
drop function if exists public.ts_anon_recovery_get_refresh_token(text);
drop function if exists public.ts_anon_recovery_upsert_refresh_token(text, text, uuid);
drop function if exists public.ts_anon_recovery_delete_by_fingerprint(text);

create or replace function public.ts_anon_recovery_get_refresh_token(
  p_fingerprint_hash text,
  p_server_code text default null
)
returns table(refresh_token text)
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_fingerprint_hash is null or length(trim(p_fingerprint_hash)) = 0 then
    return;
  end if;

  return query
  with target_server as (
    select gs.id
    from public.game_servers gs
    where
      case
        when p_server_code is null or length(trim(p_server_code)) = 0 then gs.id = public.ts_default_server_id()
        else gs.server_code = trim(p_server_code)
      end
    limit 1
  )
  select t.refresh_token
  from public.anonymous_recovery_tokens t
  join target_server s on s.id = t.server_id
  where t.fingerprint_hash = trim(p_fingerprint_hash)
  limit 1;
end;
$$;

create or replace function public.ts_anon_recovery_upsert_refresh_token(
  p_fingerprint_hash text,
  p_refresh_token text,
  p_account_id uuid default null,
  p_server_code text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_server_id uuid;
begin
  if p_fingerprint_hash is null or length(trim(p_fingerprint_hash)) = 0 then
    return;
  end if;

  if p_refresh_token is null or length(trim(p_refresh_token)) = 0 then
    return;
  end if;

  if p_account_id is not null then
    select p.server_id into v_server_id
    from public.user_profiles p
    where p.account_id = p_account_id
    limit 1;
  end if;

  if v_server_id is null and p_server_code is not null and length(trim(p_server_code)) > 0 then
    select gs.id into v_server_id
    from public.game_servers gs
    where gs.server_code = trim(p_server_code)
    limit 1;
  end if;

  if v_server_id is null then
    v_server_id := public.ts_default_server_id();
  end if;

  insert into public.anonymous_recovery_tokens (fingerprint_hash, server_id, refresh_token, account_id, updated_at)
  values (trim(p_fingerprint_hash), v_server_id, trim(p_refresh_token), p_account_id, now())
  on conflict (fingerprint_hash, server_id)
  do update set
    refresh_token = excluded.refresh_token,
    account_id = excluded.account_id,
    updated_at = now();
end;
$$;

create or replace function public.ts_anon_recovery_delete_by_fingerprint(
  p_fingerprint_hash text,
  p_server_code text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_server_id uuid;
begin
  if p_fingerprint_hash is null or length(trim(p_fingerprint_hash)) = 0 then
    return;
  end if;

  if p_server_code is not null and length(trim(p_server_code)) > 0 then
    select gs.id into v_server_id
    from public.game_servers gs
    where gs.server_code = trim(p_server_code)
    limit 1;
  else
    v_server_id := public.ts_default_server_id();
  end if;

  if v_server_id is null then
    return;
  end if;

  delete from public.anonymous_recovery_tokens
  where fingerprint_hash = trim(p_fingerprint_hash)
    and server_id = v_server_id;
end;
$$;

grant execute on function public.ts_anon_recovery_get_refresh_token(text, text) to anon, authenticated;
grant execute on function public.ts_anon_recovery_upsert_refresh_token(text, text, uuid, text) to authenticated;
grant execute on function public.ts_anon_recovery_delete_by_fingerprint(text, text) to anon, authenticated;

-- 본인 account_id(auth.uid())에 매달린 익명 복구 행만 삭제. 탈퇴 RPC(ts_request_withdrawal) 등에서 호출.
create or replace function public.ts_delete_my_anon_recovery_tokens()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id uuid := auth.uid();
begin
  if v_id is null then
    return;
  end if;

  delete from public.anonymous_recovery_tokens
  where account_id = v_id;
end;
$$;

comment on function public.ts_delete_my_anon_recovery_tokens() is
  '현재 JWT 사용자에 대한 anonymous_recovery_tokens 행 삭제(로그아웃 정리·탈퇴 요청 등).';

grant execute on function public.ts_delete_my_anon_recovery_tokens() to authenticated;

-- 트리거 전용: 임의 account_id(검증은 호출부). PostgREST에 노출하지 않음.
create or replace function public._ts_delete_anon_recovery_tokens_by_account_id(p_account_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_account_id is null then
    return;
  end if;

  delete from public.anonymous_recovery_tokens
  where account_id = p_account_id;
end;
$$;

revoke all on function public._ts_delete_anon_recovery_tokens_by_account_id(uuid) from public;
revoke all on function public._ts_delete_anon_recovery_tokens_by_account_id(uuid) from anon, authenticated;

-- auth.users: 익명→소셜 등 연동 시 is_anonymous true→false, 또는 계정 하드 삭제 시 복구 토큰 제거
create or replace function public.ts_auth_users_anon_recovery_cleanup()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if tg_op = 'DELETE' then
    perform public._ts_delete_anon_recovery_tokens_by_account_id(old.id);
    return old;
  end if;

  if tg_op = 'UPDATE' then
    if coalesce(old.is_anonymous, false) = true
      and coalesce(new.is_anonymous, false) = false then
      perform public._ts_delete_anon_recovery_tokens_by_account_id(new.id);
    end if;
    return new;
  end if;

  return new;
end;
$$;

do $$
begin
  if exists (
    select 1
    from information_schema.columns c
    where c.table_schema = 'auth'
      and c.table_name = 'users'
      and c.column_name = 'is_anonymous'
  ) then
    drop trigger if exists trg_auth_users_anon_recovery_cleanup_u on auth.users;
    create trigger trg_auth_users_anon_recovery_cleanup_u
      after update of is_anonymous on auth.users
      for each row
      execute function public.ts_auth_users_anon_recovery_cleanup();
  end if;
exception
  when others then
    raise notice 'trg_auth_users_anon_recovery_cleanup_u: skipped — %', sqlerrm;
end $$;

do $$
begin
  drop trigger if exists trg_auth_users_anon_recovery_cleanup_d on auth.users;
  create trigger trg_auth_users_anon_recovery_cleanup_d
    after delete on auth.users
    for each row
    execute function public.ts_auth_users_anon_recovery_cleanup();
exception
  when others then
    raise notice 'trg_auth_users_anon_recovery_cleanup_d: skipped — %', sqlerrm;
end $$;

-- 익명 계정에 Google 등 두 번째 identity 가 붙을 때(INSERT)에도 정리. is_anonymous 갱신 타이밍과 무관하게 동작.
create or replace function public.ts_auth_identities_anon_recovery_cleanup()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.user_id is not null then
    perform public._ts_delete_anon_recovery_tokens_by_account_id(new.user_id);
  end if;
  return new;
end;
$$;

do $$
begin
  if exists (
    select 1
    from information_schema.tables t
    where t.table_schema = 'auth'
      and t.table_name = 'identities'
  )
  and exists (
    select 1
    from information_schema.columns c
    where c.table_schema = 'auth'
      and c.table_name = 'identities'
      and c.column_name = 'provider'
  )
  and exists (
    select 1
    from information_schema.columns c
    where c.table_schema = 'auth'
      and c.table_name = 'identities'
      and c.column_name = 'user_id'
  ) then
    drop trigger if exists trg_auth_identities_anon_recovery_cleanup on auth.identities;
    create trigger trg_auth_identities_anon_recovery_cleanup
      after insert on auth.identities
      for each row
      when (new.provider is distinct from 'anonymous')
      execute function public.ts_auth_identities_anon_recovery_cleanup();
  end if;
exception
  when others then
    raise notice 'trg_auth_identities_anon_recovery_cleanup: skipped — %', sqlerrm;
end $$;


-- #############################################################################
-- 04. 유저 데이터 저장
-- #############################################################################

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
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
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
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
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
  if colname !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
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
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
as $$
declare
  colname     text := nullif(btrim(p_colname), '');
  default_sql text := nullif(btrim(p_default_sql), '');
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if colname !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
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
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
as $$
declare
  colname text := nullif(btrim(p_colname), '');
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if colname !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
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


-- #############################################################################
-- 05. 서버 이주 · 탈퇴
-- #############################################################################

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


-- #############################################################################
-- 06. 우편함
-- #############################################################################

-- 서버에서 플레이어에게 아이템·메시지를 전달하는 우편함 기능을 제공합니다.
-- 수령(ts_claim_mail_items), 삭제(ts_delete_mail_for_user),
-- 만료 정리(ts_cleanup_expired_mails) RPC를 포함합니다.
-- 클라이언트 직접 쓰기는 차단되며 모든 우편 조작은 RPC를 통해서만 가능합니다.
--
-- =============================================================================
-- 우편함 (mails) — 테이블 + RLS + RPC
-- 선행: 01_servers.sql, 02_profiles.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- mails
-- ---------------------------------------------------------------------------
create table if not exists public.mails (
  id uuid primary key default gen_random_uuid(),
  account_id uuid references auth.users (id) on delete set null,
  user_id text not null,
  sender_type text not null default 'system',
  title text not null default '',
  content text not null default '',
  expires_at timestamptz not null,
  created_at timestamptz not null default now(),
  items jsonb null,
  items_claimed_at timestamptz null,
  deleted_at timestamptz null,
  category text not null default 'default'
);

alter table public.mails add column if not exists account_id uuid;
alter table public.mails add column if not exists user_id text;
alter table public.mails add column if not exists sender_type text;
alter table public.mails add column if not exists title text;
alter table public.mails add column if not exists content text;
alter table public.mails add column if not exists expires_at timestamptz;
alter table public.mails add column if not exists created_at timestamptz;
alter table public.mails add column if not exists items jsonb;
alter table public.mails add column if not exists items_claimed_at timestamptz;
alter table public.mails add column if not exists deleted_at timestamptz;
alter table public.mails add column if not exists category text not null default 'default';

-- is_read 폐지: items_claimed_at 단일 축(수령/미수령)으로 통일합니다.
-- 기존에 열람됐던 텍스트 메일을 수령 상태로 보존한 뒤 컬럼을 제거합니다(재실행 안전).
do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'mails' and column_name = 'is_read'
  ) then
    update public.mails set items_claimed_at = now()
      where items_claimed_at is null and is_read is true;
    alter table public.mails drop column is_read;
  end if;
end $$;

do $$
begin
  if not exists (
    select 1 from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public' and t.relname = 'mails' and c.conname = 'mails_account_id_fkey'
  ) then
    alter table public.mails
      add constraint mails_account_id_fkey
      foreign key (account_id) references auth.users (id) on delete set null;
  end if;
end $$;

comment on table public.mails is '시스템 우편. RLS는 account_id + profiles.server_id 조인.';
comment on column public.mails.items is '보상 배열 [{key,count}, ...]. NULL/[] 는 텍스트 전용.';
comment on column public.mails.deleted_at is '플레이어 소프트 삭제(숨김).';
comment on column public.mails.category is
  '분류 파티션 키(자유 텍스트, 기본값 default). RPC p_category 파라미터로 필터링. 카탈로그 테이블 없음.';

create index if not exists mails_account_id_created_idx on public.mails (account_id, created_at desc)
  where account_id is not null and deleted_at is null;
create index if not exists mails_account_id_expires_idx on public.mails (account_id, expires_at)
  where account_id is not null;
create index if not exists mails_user_id_created_idx on public.mails (user_id, created_at desc);
create index if not exists mails_expires_at_idx on public.mails (expires_at);
create index if not exists mails_account_id_category_created_idx on public.mails (account_id, category, created_at desc)
  where account_id is not null and deleted_at is null;

-- 위 인덱스는 전부 부분 인덱스(account_id is not null, deleted_at is null)라 게임 클라이언트 조회 전용이다.
-- 어드민 우편 내역은 계정으로 좁히지 않고 삭제된 우편도 보므로 그 인덱스들을 탈 수 없어, 전체 정렬용을 따로 둔다.
create index if not exists mails_created_at_idx on public.mails (created_at desc);

alter table public.mails enable row level security;

drop policy if exists "mails_select_own" on public.mails;
drop policy if exists "mails_update_own" on public.mails;

-- 본인 + profiles.user_id 일치 + 현재 세션 서버 일치 + 숨김 아님 + 기한 안 지남
-- 만료 조건은 ts_mail_inbox_counts·ts_claim_all_mail_items 와 같은 기준이다.
-- 여기만 빠지면 배지 숫자에는 안 잡히는 우편이 목록에는 보이고, 수령하면 mail_expired 로 실패한다.
create policy "mails_select_own"
on public.mails for select
using (
  account_id is not null
  and account_id = auth.uid()
  and deleted_at is null
  and expires_at > now()
  and exists (
    select 1
    from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = mails.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  )
);

-- ---------------------------------------------------------------------------
-- RPC: 메일 상세 조회 — items 없음(NULL/비배열/빈 배열)이면 읽음 처리 후 반환
-- ---------------------------------------------------------------------------
create or replace function public.ts_view_mail_for_user(p_mail_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  m public.mails%rowtype;
  no_attachment boolean;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into m from public.mails where id = p_mail_id for update;
  if not found then
    raise exception 'mail_not_found';
  end if;

  if m.account_id is null or m.account_id <> auth.uid() then
    raise exception 'forbidden';
  end if;

  if not exists (
    select 1 from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = m.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  ) then
    raise exception 'forbidden_server';
  end if;

  if m.deleted_at is not null then
    raise exception 'mail_deleted';
  end if;

  no_attachment :=
    m.items is null
    or jsonb_typeof(m.items) <> 'array'
    or jsonb_array_length(m.items) = 0;

  -- 첨부 없는 텍스트 메일은 열람 시 수령 처리(items_claimed_at). 보상 메일은 수령 시에만 처리.
  if no_attachment and m.items_claimed_at is null then
    update public.mails set items_claimed_at = now() where id = p_mail_id;
  end if;

  return (select to_jsonb(t) from public.mails t where t.id = p_mail_id);
end;
$$;

comment on function public.ts_view_mail_for_user(uuid) is
  '본인·프로필 서버 일치 메일 1건 JSON. 보상 items 없으면 열람 시 items_claimed_at(수령) 처리. SECURITY DEFINER.';

revoke all on function public.ts_view_mail_for_user(uuid) from public;
grant execute on function public.ts_view_mail_for_user(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 단일 메일 보상 일괄 수령 — 반환 jsonb 배열 [{index,key,count}, ...] (빈 배열 = no-op)
-- ---------------------------------------------------------------------------
create or replace function public.ts_claim_mail_items(p_mail_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  m public.mails%rowtype;
  elem jsonb;
  items_out jsonb;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into m from public.mails where id = p_mail_id for update;
  if not found then
    raise exception 'mail_not_found';
  end if;

  if m.account_id is null or m.account_id <> auth.uid() then
    raise exception 'forbidden';
  end if;

  if not exists (
    select 1 from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = m.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  ) then
    raise exception 'forbidden_server';
  end if;

  if m.deleted_at is not null then
    raise exception 'mail_deleted';
  end if;

  if m.expires_at <= now() then
    raise exception 'mail_expired';
  end if;

  if m.items is null or jsonb_typeof(m.items) <> 'array' or jsonb_array_length(m.items) = 0 then
    return '[]'::jsonb;
  end if;

  if m.items_claimed_at is not null then
    raise exception 'already_claimed';
  end if;

  for elem in select * from jsonb_array_elements(m.items)
  loop
    if trim(coalesce(elem->>'key', '')) = '' then
      raise exception 'invalid_items_payload';
    end if;
    begin
      if (elem->>'count')::int is null or (elem->>'count')::int <= 0 then
        raise exception 'invalid_items_payload';
      end if;
    exception
      when others then
        raise exception 'invalid_items_payload';
    end;
  end loop;

  update public.mails
  set items_claimed_at = now()
  where id = p_mail_id;

  select coalesce(
    jsonb_agg(
      jsonb_build_object(
        'index', (t.ord::int - 1),
        'key', t.e->>'key',
        'count', (t.e->>'count')::int
      )
      order by t.ord
    ),
    '[]'::jsonb
  )
  into items_out
  from jsonb_array_elements(m.items) with ordinality as t(e, ord);

  return items_out;
end;
$$;

comment on function public.ts_claim_mail_items(uuid) is
  '본인·프로필 서버 일치 메일 보상 전부 수령(items_claimed_at). items 비면 [] 반환(no-op). SECURITY DEFINER.';

revoke all on function public.ts_claim_mail_items(uuid) from public;
grant execute on function public.ts_claim_mail_items(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 우편함 전체 일괄 수령 — 반환 [{mail_id, items:[{index,key,count},...]}, ...]
-- ---------------------------------------------------------------------------
drop function if exists public.ts_claim_all_mail_items();

create or replace function public.ts_claim_all_mail_items(p_category text default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  r record;
  elem jsonb;
  items_out jsonb;
  acc jsonb := '[]'::jsonb;
  one_mail jsonb;
  v_category text;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  v_category := nullif(btrim(p_category), '');

  -- 한 루프에서 검증·갱신·결과 누적(행 잠금 유지)
  for r in
    select m.id, m.items
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.expires_at > now()
      and m.items_claimed_at is null
      and m.items is not null
      and jsonb_typeof(m.items) = 'array'
      and jsonb_array_length(m.items) > 0
      and (v_category is null or m.category = v_category)
      and exists (
        select 1 from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
      )
    order by m.created_at asc
    for update of m
  loop
    for elem in select * from jsonb_array_elements(r.items)
    loop
      if trim(coalesce(elem->>'key', '')) = '' then
        raise exception 'invalid_items_payload';
      end if;
      begin
        if (elem->>'count')::int is null or (elem->>'count')::int <= 0 then
          raise exception 'invalid_items_payload';
        end if;
      exception
        when others then
          raise exception 'invalid_items_payload';
      end;
    end loop;

    update public.mails
    set items_claimed_at = now()
    where id = r.id;

    select coalesce(
      jsonb_agg(
        jsonb_build_object(
          'index', (t.ord::int - 1),
          'key', t.e->>'key',
          'count', (t.e->>'count')::int
        )
        order by t.ord
      ),
      '[]'::jsonb
    )
    into items_out
    from jsonb_array_elements(r.items) with ordinality as t(e, ord);

    one_mail := jsonb_build_object('mail_id', r.id::text, 'items', items_out);
    acc := acc || jsonb_build_array(one_mail);
  end loop;

  return acc;
end;
$$;

comment on function public.ts_claim_all_mail_items(text) is
  '미수령 보상 메일 전부 일괄 수령(items_claimed_at). p_category=null이면 전체 분류. SECURITY DEFINER.';

revoke all on function public.ts_claim_all_mail_items(text) from public;
grant execute on function public.ts_claim_all_mail_items(text) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 플레이어 소프트 삭제 (미수령 보상 있으면 거부)
-- ---------------------------------------------------------------------------
create or replace function public.ts_delete_mail_for_user(p_mail_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  m public.mails%rowtype;
  has_reward boolean;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into m from public.mails where id = p_mail_id for update;
  if not found then
    raise exception 'mail_not_found';
  end if;

  if m.account_id is null or m.account_id <> auth.uid() then
    raise exception 'forbidden';
  end if;

  if not exists (
    select 1 from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = m.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  ) then
    raise exception 'forbidden_server';
  end if;

  if m.deleted_at is not null then
    return;
  end if;

  has_reward :=
    m.items is not null
    and jsonb_typeof(m.items) = 'array'
    and jsonb_array_length(m.items) > 0;

  if has_reward and m.items_claimed_at is null then
    raise exception 'cannot_delete_unclaimed';
  end if;

  update public.mails set deleted_at = now() where id = p_mail_id;
end;
$$;

comment on function public.ts_delete_mail_for_user(uuid) is
  '우편함에서 숨김. 보상 미수령이면 거부. SECURITY DEFINER.';

revoke all on function public.ts_delete_mail_for_user(uuid) from public;
grant execute on function public.ts_delete_mail_for_user(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 수령한 우편 일괄 소프트 삭제 (미수령 = items_claimed_at is null 은 제외)
-- ---------------------------------------------------------------------------
drop function if exists public.ts_delete_read_mails_for_user();
drop function if exists public.ts_delete_read_mails_for_user(text);
drop function if exists public.ts_delete_claimed_mails_for_user();

create or replace function public.ts_delete_claimed_mails_for_user(p_category text default null)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  n int;
  v_category text;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  v_category := nullif(btrim(p_category), '');

  with victims as (
    select m.id
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.items_claimed_at is not null
      and (v_category is null or m.category = v_category)
      and exists (
        select 1 from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
      )
  )
  update public.mails u
  set deleted_at = now()
  from victims v
  where u.id = v.id;

  get diagnostics n = row_count;
  return coalesce(n, 0);
end;
$$;

comment on function public.ts_delete_claimed_mails_for_user(text) is
  '수령한(items_claimed_at) 메일만 일괄 숨김. 미수령은 제외. p_category=null이면 전체 분류. 반환: 처리 행 수. SECURITY DEFINER.';

revoke all on function public.ts_delete_claimed_mails_for_user(text) from public;
grant execute on function public.ts_delete_claimed_mails_for_user(text) to authenticated;

-- ---------------------------------------------------------------------------
-- 만료 메일 하드 삭제 (서비스 롤·cron 전용)
-- ---------------------------------------------------------------------------
create or replace function public.ts_cleanup_expired_mails(p_batch int default 500)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  n int;
begin
  if p_batch is null or p_batch < 1 then
    p_batch := 500;
  end if;
  if p_batch > 10000 then
    p_batch := 10000;
  end if;

  delete from public.mails
  where id in (
    select m.id
    from public.mails m
    where m.expires_at < now()
    limit p_batch
  );

  get diagnostics n = row_count;
  return n;
end;
$$;

comment on function public.ts_cleanup_expired_mails(int) is
  'expires_at < now() 인 메일을 배치 삭제. service_role 전용 호출 권장.';

revoke all on function public.ts_cleanup_expired_mails(int) from public;
grant execute on function public.ts_cleanup_expired_mails(int) to service_role;

-- ---------------------------------------------------------------------------
-- 우편함 배지: 미수령 메일 수 (items_claimed_at is null — 미열람 텍스트 + 미수령 보상)
-- ---------------------------------------------------------------------------
create or replace function public.ts_mail_inbox_counts()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_unclaimed int;
  v_by_category jsonb;
begin
  if auth.uid() is null then
    return null;
  end if;

  with my_mails as (
    select m.category, m.items_claimed_at
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.expires_at > now()
      and exists (
        select 1
        from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
      )
  ),
  per_category as (
    select category,
           count(*) filter (where items_claimed_at is null)::int as unclaimed
    from my_mails
    group by category
  )
  select
    coalesce(sum(unclaimed), 0)::int,
    coalesce(
      jsonb_object_agg(category, jsonb_build_object('unclaimed', unclaimed)),
      '{}'::jsonb
    )
  into v_unclaimed, v_by_category
  from per_category;

  return jsonb_build_object(
    'unclaimed', v_unclaimed,
    'by_category', v_by_category
  );
end;
$$;

comment on function public.ts_mail_inbox_counts() is
  '미수령 메일 개수(items_claimed_at is null, 전체 집계) + by_category 분류별 세부 내역. SECURITY DEFINER.';

revoke all on function public.ts_mail_inbox_counts() from public;
grant execute on function public.ts_mail_inbox_counts() to authenticated;

-- ---------------------------------------------------------------------------
-- 우편함 쓰기 권한 강화 — 클라이언트(authenticated) 직접 INSERT/UPDATE/DELETE 차단
-- ---------------------------------------------------------------------------
-- 우편 변경은 RPC(ts_view_mail_for_user, ts_claim_*, ts_delete_*)만 쓰도록 좁힙니다.
-- [시스템 메일 발송은 service_role / SQL Editor / Edge Function만 사용해야 합니다]
-- 롤백: grant insert, update, delete on table public.mails to authenticated;

-- 직접 UPDATE 경로 제거 (읽음·수령·삭제는 전부 SECURITY DEFINER RPC)
drop policy if exists "mails_update_own" on public.mails;

revoke insert, update, delete on table public.mails from authenticated;

-- anon 은 보통 mails 미사용. 목록 REST가 anon 이면 아래 한 줄 대신 grant select 만 조정.
revoke all on table public.mails from anon;

grant select on table public.mails to authenticated;


-- #############################################################################
-- 07. 인앱 결제 영수증
-- #############################################################################

-- 인앱 결제 영수증 서버 검증 결과를 기록합니다.
-- INSERT는 Edge Function(service_role)만 수행하며, 클라이언트는 본인 내역 조회만 가능합니다.
-- purchase_token UNIQUE 제약으로 동일 영수증의 이중 지급을 DB 수준에서 차단합니다.
--
-- =============================================================================
-- 인앱 구매 영수증 검증 기록 테이블 (Google Play + Apple App Store)
-- 선행: 없음 (auth.users 참조만 필요)
--
-- 동일 purchase_token 이중 처리를 DB UNIQUE 제약으로 차단합니다.
-- INSERT는 purchase-verify-google / purchase-verify-apple Edge Function만 수행합니다.
-- Edge Function은 service_role 키로 실행되므로 RLS를 우회합니다.
-- 클라이언트에는 SELECT(본인 내역)만 허용합니다.
-- =============================================================================

create table if not exists public.purchases (
  id              bigint generated always as identity primary key,
  account_id      uuid references auth.users(id) on delete set null,
  user_id         text,
  product_id      text        not null,
  purchase_token  text        not null unique,  -- Google: purchaseToken / Apple: transaction_id
  order_id        text,                         -- Google: orderId / Apple: transaction_id
  package_name    text        not null,         -- Google: packageName / Apple: bundleId
  store           text        not null default 'google_play',  -- 'google_play' | 'apple_app_store'
  verified_at     timestamptz not null default now()
);

-- 기존 테이블이 있는 경우 컬럼 추가 (기존 데이터 영향 없음)
alter table public.purchases
  add column if not exists store          text   not null default 'google_play',
  add column if not exists price_amount     bigint,     -- 결제 금액(micros = 주 단위 ×1,000,000, price_currency 기준). 내부용
  add column if not exists price_currency   text,       -- ISO 4217 통화 코드 (예: "KRW", "USD")
  add column if not exists price_amount_krw bigint;     -- KRW 환산 금액(원, 정수). 매출 확인용 — 구매 시점 환율 적용

-- 검증 함수는 완료된 구매만 기록하므로 상태 컬럼은 사용하지 않는다(기존 설치본 정리).
alter table public.purchases drop column if exists purchase_state;

-- 어드민 영수증 검증 화면은 verified_at 내림차순 정렬 + 기간 필터가 기본이고,
-- 계정·주문번호로 검색한다. 결제 기록은 삭제되지 않고 계속 쌓이므로 인덱스가 없으면 갈수록 느려진다.
create index if not exists purchases_verified_at_idx on public.purchases (verified_at desc);
create index if not exists purchases_account_id_idx  on public.purchases (account_id) where account_id is not null;
create index if not exists purchases_order_id_idx    on public.purchases (order_id)   where order_id is not null;
-- 어드민 화면의 상점 목록(DISTINCT store)이 힙 대신 인덱스만 읽도록.
create index if not exists purchases_store_idx       on public.purchases (store);

alter table public.purchases enable row level security;

-- 본인 구매 내역 조회만 허용. INSERT는 purchase-verify Edge Function이 service_role로만 처리한다.
-- (유저 직접 INSERT 정책을 두지 않아 가짜 결제 기록·total_paid_krw 조작을 차단)
drop policy if exists "users_insert_own_purchases" on public.purchases;
drop policy if exists "users_read_own_purchases" on public.purchases;
create policy "users_read_own_purchases"
  on public.purchases for select
  using (account_id = auth.uid());

-- =============================================================================
-- 결제 완료 시 user_profiles.total_paid_krw 원자적 증분 트리거
-- 선행: 02_profiles.sql (user_profiles.total_paid_krw 컬럼)
-- =============================================================================

create or replace function public.ts_after_purchase_insert()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.price_amount_krw is not null and new.price_amount_krw > 0 then
    update public.user_profiles
       set total_paid_krw = total_paid_krw + new.price_amount_krw
     where account_id = new.account_id;
  end if;
  return new;
end;
$$;

comment on function public.ts_after_purchase_insert() is
  'purchases INSERT 후 user_profiles.total_paid_krw를 price_amount_krw만큼 증분. SECURITY DEFINER.';

drop trigger if exists tr_after_purchase_insert on public.purchases;
create trigger tr_after_purchase_insert
  after insert on public.purchases
  for each row execute function public.ts_after_purchase_insert();

-- 기존 결제 데이터 초기화 (마이그레이션 1회 실행, 이후 불필요)
-- UPDATE public.user_profiles p
--    SET total_paid_krw = COALESCE((
--      SELECT SUM(pu.price_amount_krw)
--      FROM public.purchases pu
--      WHERE pu.account_id = p.account_id
--        AND pu.price_amount_krw IS NOT NULL
--    ), 0);


-- #############################################################################
-- 08. 원격 설정
-- #############################################################################

-- 서버에서 클라이언트 설정값을 런타임에 변경할 수 있는 Remote Config 테이블입니다.
-- 읽기는 anon·authenticated 모두 가능하고, 쓰기는 service_role(Retool·대시보드)만 허용합니다.
--
-- =============================================================================
-- 플레이어 스키마 — remote_config (Retool / 클라이언트 원격 설정)
-- 선행: 없음 (독립 테이블)
-- =============================================================================
--
-- 설계: 1키 = 1설정묶음(JSON 클러스터링)
-- 관련 설정은 하나의 키에 JSON 객체로 묶어 관리합니다.
-- 예: key="gameplay_v1", value_json={"stamina":{...},"battle":{...}}
-- 폴링 주기는 DB에 저장하지 않으며, 클라이언트(Unity)에서 키 조회 시 지정합니다.

-- 기존 테이블/컬럼 마이그레이션용 (category, poll_interval_seconds, client_version_min/max, max_stale_seconds 제거)
alter table if exists public.remote_config drop column if exists category;
alter table if exists public.remote_config drop column if exists poll_interval_seconds;
alter table if exists public.remote_config drop column if exists client_version_min;
alter table if exists public.remote_config drop column if exists client_version_max;
alter table if exists public.remote_config drop column if exists max_stale_seconds;

create table if not exists public.remote_config (
  key text primary key,
  value_json jsonb not null,  -- JSON 객체 루트 ({...}) 필수. jsonb로 쓰기 시점 JSON 검증 + 서버측 쿼리. (SDK는 jsonb·text 응답 모두 처리)
  updated_at timestamptz not null default now(),
  version int not null default 1,
  enabled boolean not null default true,
  description text,
  requires_auth boolean not null default false
  -- 폴링 주기·캐시 유효 시간은 클라이언트(Unity)에서 키 조회 시 지정합니다.
);

alter table public.remote_config add column if not exists value_json jsonb;
alter table public.remote_config add column if not exists updated_at timestamptz not null default now();
alter table public.remote_config add column if not exists version int not null default 1;
alter table public.remote_config add column if not exists enabled boolean not null default true;
-- category, poll_interval_seconds, client_version_min/max, max_stale_seconds 컬럼 제거됨
alter table public.remote_config add column if not exists description text;
alter table public.remote_config add column if not exists requires_auth boolean not null default false;

-- updated_at 자동 갱신 (Retool UPDATE 시에도 일관되게 갱신)
create or replace function public.ts_remote_config_set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at := now();
  return new;
end;
$$;

drop trigger if exists tr_remote_config_set_updated_at on public.remote_config;
create trigger tr_remote_config_set_updated_at
  before update on public.remote_config
  for each row
  execute function public.ts_remote_config_set_updated_at();

alter table public.remote_config enable row level security;

-- anon: 읽기만 (로그인 없이 클라이언트 조회)
drop policy if exists remote_config_select_anon on public.remote_config;
create policy remote_config_select_anon
  on public.remote_config
  for select
  to anon
  using (true);

-- authenticated: 읽기 (로그인 사용자)
drop policy if exists remote_config_select_authenticated on public.remote_config;
create policy remote_config_select_authenticated
  on public.remote_config
  for select
  to authenticated
  using (true);

-- 쓰기: service_role 전용 (Retool 등은 Service Role 키 사용 권장)
drop policy if exists remote_config_all_service_role on public.remote_config;
create policy remote_config_all_service_role
  on public.remote_config
  for all
  to service_role
  using (true)
  with check (true);


-- #############################################################################
-- 09. 자동화 크론 잡
-- #############################################################################

-- 주기적인 정리 작업을 pg_cron으로 자동화합니다.
-- · 만료된 우편 삭제          매일 03:00  ts_cleanup_expired_mails(500)
-- · 탈퇴 예약 만료 계정 처리  매일 02:00  ts_withdrawal_cleanup_batch(100)
--
-- =============================================================================
-- 크론 잡 — 만료 정리·탈퇴 배치 (pg_cron)
-- 선행: 06_mails.sql, 05_account_management.sql
--
-- [주의] pg_cron 확장이 필요합니다.
--   아래 CREATE EXTENSION 명령이 자동으로 활성화합니다 (대부분의 Supabase 플랜에서 지원).
-- =============================================================================

-- ---------------------------------------------------------------------------
-- pg_cron 확장 활성화 (이미 활성화되어 있으면 무시)
-- ---------------------------------------------------------------------------
create extension if not exists pg_cron;

-- ---------------------------------------------------------------------------
-- withdrawal_delete_queue — 탈퇴 완료 계정 auth 삭제 대기 목록
-- ---------------------------------------------------------------------------
-- ts_withdrawal_cleanup_batch가 참조하므로 함수보다 먼저 생성합니다.
-- 관리자가 주기적으로 처리. 클라이언트 접근 없음 (RLS 활성화 + policy 없음).
-- ---------------------------------------------------------------------------
create table if not exists public.withdrawal_delete_queue (
  user_id      uuid        primary key references auth.users(id) on delete cascade,
  queued_at    timestamptz not null default now(),
  processed    boolean     not null default false,
  processed_at timestamptz null
);

comment on table public.withdrawal_delete_queue is
  '탈퇴 완료 계정의 auth 삭제 대기 목록. 관리자가 주기적으로 처리.';

alter table public.withdrawal_delete_queue enable row level security;
-- [의도적] 정책 없음 — service_role 전용.

-- ---------------------------------------------------------------------------
-- ts_withdrawal_cleanup_batch — 탈퇴 예약 만료 계정 일괄 처리
-- ---------------------------------------------------------------------------
-- withdrawn_at <= now()인 계정을 account_closures에 기록하고
-- withdrawal_delete_queue에 삭제 대기 등록합니다.
-- auth.admin.deleteUser()는 SQL에서 직접 불가 — 실제 삭제는 별도 워크플로우.
-- ---------------------------------------------------------------------------
create or replace function public.ts_withdrawal_cleanup_batch(p_batch int default 100)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  n   int := 0;
  rec record;
begin
  if p_batch is null or p_batch < 1 then p_batch := 100; end if;
  if p_batch > 500 then p_batch := 500; end if;

  for rec in
    select user_id, account_id
    from public.user_profiles
    where withdrawn_at is not null
      and withdrawn_at <= now()
    limit p_batch
  loop
    -- account_closures 기록: user_id = 영구 플레이어 ID, account_id = auth UUID
    insert into public.account_closures (user_id, account_id, closed_at, note)
    values (rec.user_id, rec.account_id, now(), 'withdrawal_cleanup')
    on conflict (user_id) do update
    set closed_at = now(), note = 'withdrawal_cleanup';

    -- 삭제 대기 큐에 추가 (별도 관리자 처리용)
    insert into public.withdrawal_delete_queue (user_id, queued_at, processed)
    values (rec.account_id, now(), false)
    on conflict (user_id) do nothing;

    n := n + 1;
  end loop;

  return jsonb_build_object('processed', n, 'queued_for_deletion', n);
end;
$$;

revoke all on function public.ts_withdrawal_cleanup_batch(int) from public;
-- pg_cron은 내부 실행이므로 authenticated grant 불필요

-- ---------------------------------------------------------------------------
-- 크론 잡 등록 (멱등: 기존 잡이 있으면 교체)
-- ---------------------------------------------------------------------------

-- 1. 메일 만료 정리 — 매일 새벽 3시 (batch 500)
do $$
begin
  if exists (select 1 from cron.job where jobname = 'cleanup-expired-mails') then
    perform cron.unschedule('cleanup-expired-mails');
  end if;
end $$;
select cron.schedule(
  'cleanup-expired-mails',
  '0 3 * * *',
  'select public.ts_cleanup_expired_mails(500)'
);

-- 2. 탈퇴 계정 정리 — 매일 새벽 2시 (메일 정리 1시간 전, batch 100)
do $$
begin
  if exists (select 1 from cron.job where jobname = 'withdrawal-cleanup') then
    perform cron.unschedule('withdrawal-cleanup');
  end if;
end $$;
select cron.schedule(
  'withdrawal-cleanup',
  '0 2 * * *',
  'select public.ts_withdrawal_cleanup_batch(100)'
);

-- ---------------------------------------------------------------------------
-- 관리용 명령어 (필요 시 SQL Editor에서 직접 실행)
-- ---------------------------------------------------------------------------

-- cron job 목록 확인
-- select * from cron.job;

-- job 실행 로그 확인
-- select * from cron.job_run_details
-- where jobname in ('cleanup-expired-mails', 'withdrawal-cleanup')
-- order by start_time desc limit 20;

-- job 삭제 (필요 시)
-- select cron.unschedule('cleanup-expired-mails');
-- select cron.unschedule('withdrawal-cleanup');


-- #############################################################################
-- 10. 계정 차단 메시지
-- #############################################################################

-- =============================================================================
-- 10_bans.sql  — 차단(Ban) 어드민 메시지
-- 선행: 01_servers.sql ~ 09_cron_jobs.sql
-- =============================================================================
-- Supabase 네이티브 ban(auth.users.banned_until)을 사용하는 프로젝트에서
-- 어드민이 작성한 사유 메시지를 플레이어에게 전달하기 위한 테이블.
-- SDK의 get-ban-info Edge Function이 이 테이블을 읽어 SupabaseBanInfo.BanMessage로 반환합니다.
-- =============================================================================

-- 어드민 차단 메시지
create table if not exists public.user_ban_messages (
  account_id  uuid        primary key references auth.users(id) on delete cascade,
  ban_message text,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

-- RLS 활성화 — 아래에서 모든 public 접근을 거부
-- 읽기는 get-ban-info Edge Function(service_role 클라이언트)만 허용
alter table public.user_ban_messages enable row level security;

comment on table public.user_ban_messages is
  '차단된 플레이어에게 표시할 어드민 메시지. auth.users.banned_until과 함께 사용.
   쓰기: service_role (Retool 또는 admin Edge Function).
   읽기: get-ban-info Edge Function (service_role) 경유 — 게임 클라이언트는 직접 접근 불가.';

notify pgrst, 'reload schema';


-- #############################################################################
-- 11. 유저 데이터 변경 로그
-- #############################################################################

-- 15_user_data_logs.sql
-- user_data 변경 diff 로그 테이블 및 트리거
-- 변경된 필드의 이전 값(OLD)만 저장하고, 역추적으로 특정 시점 상태를 재구성한다.

-- ── 테이블 ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.user_data_logs (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  account_id uuid   NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
  diff       jsonb  NOT NULL,   -- 변경된 필드의 OLD 값만 포함
  created_at timestamptz NOT NULL DEFAULT now(),
  source     text            -- 변경 주체. 세션 변수 app.log_source 값을 그대로 기록
);

ALTER TABLE public.user_data_logs ADD COLUMN IF NOT EXISTS source text;

CREATE INDEX IF NOT EXISTS user_data_logs_account_id_created_idx
  ON public.user_data_logs (account_id, created_at DESC);

-- 어드민 저장 로그 화면의 필드 필터(jsonb_exists(diff, '컬럼명'))용.
-- 없으면 계정별 로그를 전부 훑는다.
CREATE INDEX IF NOT EXISTS user_data_logs_diff_gin
  ON public.user_data_logs USING gin (diff jsonb_path_ops);

ALTER TABLE public.user_data_logs ENABLE ROW LEVEL SECURITY;
-- 플레이어 직접 접근 없음 (서비스 롤 + 어드민 전용)

-- ── 트리거 함수 ────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION ts_log_user_data_diff()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
  v_old  jsonb := to_jsonb(OLD);
  v_new  jsonb := to_jsonb(NEW);
  v_diff jsonb := '{}';
  v_key  text;
  -- 시스템 컬럼은 diff 제외
  v_skip text[] := ARRAY['id','account_id','user_id','server_id','created_at','updated_at'];
  -- 변경 주체. 호출자가 set_config('app.log_source', ...)로 지정하지 않으면 NULL
  v_source text := nullif(current_setting('app.log_source', true), '');
BEGIN
  FOR v_key IN SELECT jsonb_object_keys(v_new)
  LOOP
    IF v_key = ANY(v_skip) THEN CONTINUE; END IF;
    IF (v_old->v_key) IS DISTINCT FROM (v_new->v_key) THEN
      v_diff := v_diff || jsonb_build_object(v_key, v_old->v_key);
    END IF;
  END LOOP;

  IF v_diff != '{}'::jsonb THEN
    INSERT INTO public.user_data_logs (account_id, diff, source)
    VALUES (NEW.account_id, v_diff, v_source);
  END IF;
  RETURN NEW;
END;
$$;

-- ── 트리거 ────────────────────────────────────────────────────────────────
DROP TRIGGER IF EXISTS trg_user_data_log ON public.user_data;
CREATE TRIGGER trg_user_data_log
AFTER UPDATE ON public.user_data
FOR EACH ROW EXECUTE FUNCTION ts_log_user_data_diff();

-- ── 7일 초과 로그 자동 정리 (pg_cron 필요) ───────────────────────────────
SELECT cron.schedule(
  'cleanup-user-data-logs',
  '0 3 * * *',
  $$DELETE FROM public.user_data_logs WHERE created_at < now() - interval '7 days'$$
);


-- #############################################################################
-- 12. 어드민 우편 발송 · 아이템 카탈로그
-- #############################################################################

-- 어드민 우편 발송 인프라를 제공합니다.
-- 아이템 카탈로그(game_items) + 발송 캠페인 그룹(mail_batches) + mails.batch_id +
-- 발송/카탈로그 관리 RPC. 클라이언트 우편 계층(06_mails)은 건드리지 않습니다.
-- 발송·카탈로그 쓰기는 모두 SECURITY DEFINER RPC(service_role)로만 수행합니다.
--
-- =============================================================================
-- 어드민 우편 발송 — game_items + mail_batches + mails.batch_id + RPC
-- 선행: 01_servers.sql, 02_profiles.sql, 06_mails.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- game_items — 아이템 카탈로그(발송 선택 편의·표시명). 실제 지급은 게임 IMailItemHandler.
-- ---------------------------------------------------------------------------
create table if not exists public.game_items (
  key          text primary key,
  display_name text not null default '',
  created_at   timestamptz not null default now()
);
alter table public.game_items add column if not exists display_name text not null default '';
alter table public.game_items add column if not exists created_at   timestamptz not null default now();
alter table public.game_items drop column if exists description;
alter table public.game_items drop column if exists category;
alter table public.game_items drop column if exists sort_order;
alter table public.game_items drop column if exists is_active;
alter table public.game_items drop column if exists updated_at;

drop index if exists public.game_items_category_sort_idx;
drop index if exists public.game_items_active_idx;

comment on table public.game_items is '어드민 우편 발송용 아이템 카탈로그. mails.items[].key 선택 편의·표시명. 실제 지급은 게임 IMailItemHandler.';

alter table public.game_items enable row level security;
drop policy if exists "game_items_select_all" on public.game_items;
create policy "game_items_select_all" on public.game_items for select using (true);
revoke all on table public.game_items from anon;
revoke insert, update, delete on table public.game_items from authenticated;
grant select on table public.game_items to authenticated;
-- service_role DML 제거(모든 쓰기는 RPC). 프로젝트 기본 권한 차이와 무관하게 동일 상태 보장.
revoke select, insert, update, delete on table public.game_items from service_role;

-- ---------------------------------------------------------------------------
-- mail_batches — 한 번의 발송(캠페인) 그룹. 이력·집계 스냅샷 보존.
-- ---------------------------------------------------------------------------
create table if not exists public.mail_batches (
  id              uuid primary key default gen_random_uuid(),
  target_mode     text not null check (target_mode in ('all','server','players')),
  server_id       uuid null references public.game_servers (id) on delete set null,
  title           text not null default '',
  content         text not null default '',
  items           jsonb null,
  expires_at      timestamptz not null,
  recipient_count int not null default 0,
  created_by      text null,
  created_at      timestamptz not null default now(),
  category        text not null default 'default'
);
alter table public.mail_batches add column if not exists target_mode     text;
alter table public.mail_batches add column if not exists server_id       uuid;
alter table public.mail_batches add column if not exists title           text;
alter table public.mail_batches add column if not exists content         text;
alter table public.mail_batches add column if not exists items           jsonb;
alter table public.mail_batches add column if not exists expires_at      timestamptz;
alter table public.mail_batches add column if not exists recipient_count int not null default 0;
alter table public.mail_batches add column if not exists created_by      text;
alter table public.mail_batches add column if not exists created_at      timestamptz not null default now();
alter table public.mail_batches add column if not exists category        text not null default 'default';
-- 언어별 제목·본문 오버라이드. ts_admin_send_mail 이 INSERT 하므로 컬럼이 없으면 발송이 실패한다.
alter table public.mail_batches add column if not exists localized       jsonb;

create index if not exists mail_batches_created_idx on public.mail_batches (created_at desc);

comment on table public.mail_batches is '어드민 우편 발송 캠페인 그룹. recipient_count는 발송 시점 스냅샷(권위값).';
comment on column public.mail_batches.category is '발송 캠페인 분류(자유 텍스트, 기본값 default). 수신자 mails.category와 동일 값.';

alter table public.mail_batches enable row level security;
revoke all on table public.mail_batches from anon, authenticated;
revoke select, insert, update, delete on table public.mail_batches from service_role;

-- ---------------------------------------------------------------------------
-- mails.batch_id — 발송 그룹 연결(만료 하드삭제돼도 batch 이력은 보존)
-- ---------------------------------------------------------------------------
alter table public.mails add column if not exists batch_id uuid;
alter table public.mails add column if not exists localized jsonb;
do $$
begin
  if not exists (
    select 1 from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public' and t.relname = 'mails' and c.conname = 'mails_batch_id_fkey'
  ) then
    alter table public.mails
      add constraint mails_batch_id_fkey
      foreign key (batch_id) references public.mail_batches (id) on delete set null;
  end if;
end $$;
create index if not exists mails_batch_id_idx on public.mails (batch_id) where batch_id is not null;

-- mails 도 service_role DML 제거(발송은 ts_admin_send_mail RPC(owner) 경유). 프로젝트 간 동일 상태 보장.
revoke select, insert, update, delete on table public.mails from service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_send_mail — 대상(all/server/players) 해석 + 수신자별 mails INSERT
--   p_target_mode : 'all' | 'server' | 'players'
--   p_account_ids : players 모드에서 user_profiles.account_id 의 jsonb 배열 (["uuid", ...])
--   p_server_id   : server 모드 대상 서버
--   p_items       : 보상 배열 [{key,count}]. game_items 검증(우회 = p_skip_item_validation)
-- 반환: {batch_id, recipient_count}
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean);
drop function if exists public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean,text,jsonb);

create or replace function public.ts_admin_send_mail(
  p_target_mode          text,
  p_title                text,
  p_expires_at           timestamptz,
  p_account_ids          jsonb   default null,
  p_server_id            uuid    default null,
  p_content              text    default '',
  p_items                jsonb   default null,
  p_created_by           text    default null,
  p_skip_item_validation boolean default false,
  p_category             text    default 'default',
  p_localized            jsonb   default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_batch_id    uuid;
  v_count       int;
  v_item        jsonb;
  v_key         text;
  v_account_ids uuid[];
  v_category    text;
begin
  if p_target_mode not in ('all','server','players') then
    raise exception 'invalid_target_mode: %', p_target_mode;
  end if;
  if p_title is null or btrim(p_title) = '' then
    raise exception 'title_empty';
  end if;
  if p_expires_at is null or p_expires_at <= now() then
    raise exception 'invalid_expires_at';
  end if;
  if p_target_mode = 'server' and p_server_id is null then
    raise exception 'server_id_required';
  end if;

  v_category := coalesce(nullif(btrim(p_category), ''), 'default');

  if p_account_ids is not null and jsonb_typeof(p_account_ids) = 'array' then
    select array_agg((value)::uuid) into v_account_ids
    from jsonb_array_elements_text(p_account_ids) as t(value);
  end if;

  if p_target_mode = 'players' and (v_account_ids is null or array_length(v_account_ids, 1) is null) then
    raise exception 'account_ids_required';
  end if;

  -- items 검증(있을 때): 배열·각 key non-empty·count 양의 정수·카탈로그 존재
  if p_items is not null then
    if jsonb_typeof(p_items) <> 'array' then
      raise exception 'items_not_array';
    end if;
    for v_item in select value from jsonb_array_elements(p_items) as t(value) loop
      v_key := v_item->>'key';
      if v_key is null or btrim(v_key) = '' then
        raise exception 'item_key_empty';
      end if;
      if (v_item->>'count') is null
         or (v_item->>'count') !~ '^[0-9]+$'
         or (v_item->>'count')::int <= 0 then
        raise exception 'item_count_invalid: %', v_key;
      end if;
      if not p_skip_item_validation
         and not exists (select 1 from public.game_items gi where gi.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  insert into public.mail_batches
    (target_mode, server_id, title, content, items, expires_at, created_by, category, localized)
  values
    (p_target_mode,
     case when p_target_mode = 'server' then p_server_id else null end,
     p_title, coalesce(p_content, ''),
     p_items, p_expires_at, p_created_by, v_category, p_localized)
  returning id into v_batch_id;

  insert into public.mails
    (account_id, user_id, sender_type, title, content,
     expires_at, created_at, items, batch_id, category, localized)
  select p.account_id, p.user_id, 'system',
         p_title, coalesce(p_content, ''), p_expires_at, now(), p_items, v_batch_id, v_category, p_localized
  from public.user_profiles p
  where p.account_id is not null
    and p.withdrawn_at is null
    and (
      p_target_mode = 'all'
      or (p_target_mode = 'server'  and p.server_id = p_server_id)
      or (p_target_mode = 'players' and p.account_id = any (v_account_ids))
    );

  get diagnostics v_count = row_count;
  update public.mail_batches set recipient_count = v_count where id = v_batch_id;

  return jsonb_build_object('batch_id', v_batch_id, 'recipient_count', v_count);
end;
$$;

comment on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb) is
  '어드민 우편 발송. 대상 all/server/players(account_id jsonb 배열) 해석(탈퇴 제외) → 수신자별 mails INSERT + mail_batches 스냅샷. items는 game_items 검증(우회 플래그). p_category 비었거나 null이면 default. p_localized는 언어별 제목·본문 오버라이드(없으면 base fallback).';

-- ---------------------------------------------------------------------------
-- ts_admin_upsert_game_item / ts_admin_delete_game_item — 카탈로그 관리
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_upsert_game_item(text,text,text,text,int);

create or replace function public.ts_admin_upsert_game_item(
  p_key          text,
  p_display_name text
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_key is null or btrim(p_key) = '' then
    raise exception 'key_empty';
  end if;
  insert into public.game_items (key, display_name)
  values (btrim(p_key), coalesce(p_display_name, ''))
  on conflict (key) do update set
    display_name = excluded.display_name;
end;
$$;

comment on function public.ts_admin_upsert_game_item(text,text) is
  '아이템 카탈로그 upsert(어드민). 발송된 mails.items 에는 영향 없음.';

create or replace function public.ts_admin_delete_game_item(p_key text)
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.game_items where key = p_key;
$$;

comment on function public.ts_admin_delete_game_item(text) is
  '아이템 카탈로그 삭제(어드민). 카탈로그만 제거, 발송 이력 불변.';

-- ---------------------------------------------------------------------------
-- ts_admin_count_recipients — 발송 전 대상 수 프리뷰(all/server)
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_count_recipients(
  p_target_mode text,
  p_server_id   uuid default null
)
returns int
language sql
stable
security definer
set search_path = public
as $$
  select count(*)::int
  from public.user_profiles p
  where p.account_id is not null
    and p.withdrawn_at is null
    and (p_target_mode = 'all' or (p_target_mode = 'server' and p.server_id = p_server_id));
$$;

comment on function public.ts_admin_count_recipients(text,uuid) is
  '발송 전 대상 수 프리뷰(all/server). 탈퇴 제외.';

-- ---------------------------------------------------------------------------
-- 어드민 RPC EXECUTE — service_role 전용(클라이언트 금지)
-- ---------------------------------------------------------------------------
revoke all on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb) from public, anon, authenticated;
grant execute on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb) to service_role;
revoke all on function public.ts_admin_upsert_game_item(text,text) from public, anon, authenticated;
grant execute on function public.ts_admin_upsert_game_item(text,text) to service_role;
revoke all on function public.ts_admin_delete_game_item(text) from public, anon, authenticated;
grant execute on function public.ts_admin_delete_game_item(text) to service_role;
revoke all on function public.ts_admin_count_recipients(text,uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_count_recipients(text,uuid) to service_role;

notify pgrst, 'reload schema';


-- #############################################################################
-- 13. 어드민 우편 예약 · 반복 발송
-- #############################################################################

-- 우편 예약·반복 발송 인프라를 제공합니다.
-- 대기 발송 테이블(mail_schedules) + 러너(ts_run_due_mail_schedules) + pg_cron(매분).
-- 러너는 만기 스케줄마다 ts_admin_send_mail(12_admin_mail)을 호출합니다.
-- 즉시 발송은 스케줄 없이 ts_admin_send_mail을 바로 쓰므로 여기 대상이 아닙니다.
--
-- =============================================================================
-- 우편 예약 발송 — mail_schedules + 러너 + cron
-- 선행: 09_cron_jobs.sql(pg_cron), 12_admin_mail.sql(ts_admin_send_mail)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- mail_schedules — 예약(1회)·반복(매일 시각) 발송 대기 목록
-- ---------------------------------------------------------------------------
create table if not exists public.mail_schedules (
  id            uuid primary key default gen_random_uuid(),
  schedule_type text not null check (schedule_type in ('scheduled','repeat')),
  target_mode   text not null check (target_mode in ('all','server','players')),
  server_id     uuid null references public.game_servers (id) on delete set null,
  account_ids   jsonb null,
  title         text not null default '',
  content       text not null default '',
  items         jsonb null,
  localized     jsonb null,
  category      text not null default 'default',
  expires_days  int  not null default 7 check (expires_days >= 1),
  scheduled_at  timestamptz null,                       -- scheduled(1회) 대상 시각
  repeat_time   time null,                              -- repeat 대상 시각
  repeat_tz     text not null default 'Asia/Seoul',
  repeat_unit   text not null default 'day' check (repeat_unit in ('day','week','month')),
  repeat_dow    int null,                               -- week: 0=일 ~ 6=토
  repeat_dom    int null,                               -- month: 1~31 (말일 초과 시 말일)
  next_run_at   timestamptz not null,                   -- 러너 판단 기준(두 타입 통일)
  is_active     boolean not null default true,
  last_run_at   timestamptz null,
  run_count     int not null default 0,
  created_by    text null,
  created_at    timestamptz not null default now()
);
alter table public.mail_schedules add column if not exists account_ids  jsonb;
alter table public.mail_schedules add column if not exists items        jsonb;
alter table public.mail_schedules add column if not exists localized    jsonb;
alter table public.mail_schedules add column if not exists category     text not null default 'default';
alter table public.mail_schedules add column if not exists expires_days int not null default 7;
alter table public.mail_schedules add column if not exists scheduled_at timestamptz;
alter table public.mail_schedules add column if not exists repeat_time  time;
alter table public.mail_schedules add column if not exists repeat_tz    text not null default 'Asia/Seoul';
alter table public.mail_schedules add column if not exists repeat_unit  text not null default 'day';
alter table public.mail_schedules add column if not exists repeat_dow   int;
alter table public.mail_schedules add column if not exists repeat_dom   int;
alter table public.mail_schedules add column if not exists last_run_at  timestamptz;
alter table public.mail_schedules add column if not exists run_count    int not null default 0;
alter table public.mail_schedules add column if not exists created_by   text;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'mail_schedules_repeat_unit_chk') then
    alter table public.mail_schedules add constraint mail_schedules_repeat_unit_chk check (repeat_unit in ('day','week','month'));
  end if;
end $$;

create index if not exists mail_schedules_due_idx on public.mail_schedules (next_run_at) where is_active;
create index if not exists mail_schedules_created_idx on public.mail_schedules (created_at desc);

comment on table public.mail_schedules is
  '우편 예약(scheduled 1회)·반복(repeat day/week/month) 발송 대기 목록. 러너가 next_run_at 만기 시 ts_admin_send_mail 호출.';

alter table public.mail_schedules enable row level security;
revoke all on table public.mail_schedules from anon, authenticated;
revoke select, insert, update, delete on table public.mail_schedules from service_role;

-- ---------------------------------------------------------------------------
-- ts_mail_schedule_next_run — 다음 실행 시각 계산(러너·생성 경로 공유)
--   scheduled : scheduled_at 그대로
--   repeat    : repeat_tz 기준 다음 발생
--               day   = 매일 repeat_time
--               week  = 매주 repeat_dow(0=일~6=토) repeat_time
--               month = 매월 repeat_dom(1~31, 말일 초과 시 말일) repeat_time
-- ---------------------------------------------------------------------------
drop function if exists public.ts_mail_schedule_next_run(text,timestamptz,time,text);

create or replace function public.ts_mail_schedule_next_run(
  p_type         text,
  p_scheduled_at timestamptz,
  p_repeat_time  time,
  p_repeat_tz    text default 'Asia/Seoul',
  p_repeat_unit  text default 'day',
  p_repeat_dow   int  default null,
  p_repeat_dom   int  default null
)
returns timestamptz
language plpgsql
stable
as $$
declare
  v_tz        text := coalesce(p_repeat_tz, 'Asia/Seoul');
  v_unit      text := coalesce(p_repeat_unit, 'day');
  v_now       timestamp;
  v_today     date;
  v_date      date;
  v_next      timestamp;
  v_cur_dow   int;
  v_delta     int;
  v_dim       int;
  v_firstnext date;
begin
  if p_type = 'scheduled' then
    return p_scheduled_at;
  elsif p_type <> 'repeat' then
    raise exception 'invalid_schedule_type: %', p_type;
  end if;

  if p_repeat_time is null then
    raise exception 'repeat_time_required';
  end if;

  v_now   := now() at time zone v_tz;
  v_today := v_now::date;

  if v_unit = 'day' then
    v_next := v_today + p_repeat_time;
    if v_next <= v_now then
      v_next := (v_today + 1) + p_repeat_time;
    end if;

  elsif v_unit = 'week' then
    if p_repeat_dow is null then
      raise exception 'repeat_dow_required';
    end if;
    v_cur_dow := extract(dow from v_today)::int;         -- 0=일 ~ 6=토
    v_delta   := ((p_repeat_dow - v_cur_dow) % 7 + 7) % 7;
    v_date    := v_today + v_delta;
    v_next    := v_date + p_repeat_time;
    if v_next <= v_now then
      v_next := (v_date + 7) + p_repeat_time;
    end if;

  elsif v_unit = 'month' then
    if p_repeat_dom is null then
      raise exception 'repeat_dom_required';
    end if;
    v_dim  := extract(day from (date_trunc('month', v_today::timestamp) + interval '1 month' - interval '1 day'))::int;
    v_date := date_trunc('month', v_today::timestamp)::date + (least(p_repeat_dom, v_dim) - 1);
    v_next := v_date + p_repeat_time;
    if v_next <= v_now then
      v_firstnext := (date_trunc('month', v_today::timestamp) + interval '1 month')::date;
      v_dim  := extract(day from (date_trunc('month', v_firstnext::timestamp) + interval '1 month' - interval '1 day'))::int;
      v_date := v_firstnext + (least(p_repeat_dom, v_dim) - 1);
      v_next := v_date + p_repeat_time;
    end if;

  else
    raise exception 'invalid_repeat_unit: %', v_unit;
  end if;

  return v_next at time zone v_tz;
end;
$$;

comment on function public.ts_mail_schedule_next_run(text,timestamptz,time,text,text,int,int) is
  '예약/반복 다음 실행 시각. repeat_unit=day(매일)/week(repeat_dow 0=일~6=토)/month(repeat_dom 1~31, 말일 초과 시 말일). repeat_tz 기준.';

-- ---------------------------------------------------------------------------
-- ts_run_due_mail_schedules — 만기 스케줄 실행(cron 매분). SECURITY DEFINER.
--   각 스케줄마다 ts_admin_send_mail 호출 후 scheduled=소진 / repeat=다음 시각 갱신.
--   한 건 실패해도 나머지는 계속 진행.
-- ---------------------------------------------------------------------------
create or replace function public.ts_run_due_mail_schedules()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  s record;
  n int := 0;
begin
  for s in
    select *
    from public.mail_schedules
    where is_active
      and next_run_at <= now()
    order by next_run_at asc
    for update skip locked
  loop
    begin
      perform public.ts_admin_send_mail(
        s.target_mode,
        s.title,
        now() + make_interval(days => s.expires_days),
        s.account_ids,
        s.server_id,
        s.content,
        s.items,
        s.created_by,
        false,
        s.category,
        s.localized
      );

      if s.schedule_type = 'scheduled' then
        update public.mail_schedules
        set is_active = false,
            last_run_at = now(),
            run_count = run_count + 1
        where id = s.id;
      else
        update public.mail_schedules
        set next_run_at = public.ts_mail_schedule_next_run('repeat', null, s.repeat_time, s.repeat_tz, s.repeat_unit, s.repeat_dow, s.repeat_dom),
            last_run_at = now(),
            run_count = run_count + 1
        where id = s.id;
      end if;

      n := n + 1;
    exception
      when others then
        raise warning '[ts_run_due_mail_schedules] schedule % failed: %', s.id, sqlerrm;
    end;
  end loop;

  return n;
end;
$$;

comment on function public.ts_run_due_mail_schedules() is
  '만기 우편 스케줄을 발송(ts_admin_send_mail). scheduled=소진, repeat=다음 시각 갱신. cron 매분.';

revoke all on function public.ts_run_due_mail_schedules() from public;

-- ---------------------------------------------------------------------------
-- cron 등록 (멱등: 기존 잡 교체) — 매분
-- ---------------------------------------------------------------------------
do $$
begin
  if exists (select 1 from cron.job where jobname = 'run-mail-schedules') then
    perform cron.unschedule('run-mail-schedules');
  end if;
end $$;
select cron.schedule(
  'run-mail-schedules',
  '* * * * *',
  'select public.ts_run_due_mail_schedules()'
);

notify pgrst, 'reload schema';


-- #############################################################################
-- 14. 우편 분류 목록
-- #############################################################################

-- 발송 폼에서 선택할 우편 분류(category) 사전 목록을 제공합니다.
-- mails.category 에는 여기 key 가 저장되며, 게임 클라이언트의 분류 필터 키와 일치해야 합니다.
-- 발송 시 목록에서만 선택하도록 어드민 UI에서 강제합니다(서버 검증은 두지 않음 — 기존 자유 문자열 호환).
--
-- =============================================================================
-- 우편 분류 사전 목록 — mail_categories
-- 선행: 06_mails.sql(mails.category), 12_admin_mail.sql(ts_admin_send_mail)
-- =============================================================================

create table if not exists public.mail_categories (
  key          text primary key,
  display_name text not null default '',
  sort_order   int  not null default 0,
  created_at   timestamptz not null default now()
);

comment on table public.mail_categories is
  '발송 폼에서 선택할 우편 분류(category) 사전 목록. mails.category에 key가 저장됨. 게임 필터 키와 일치해야 함.';

-- 기본 분류 시드(멱등)
insert into public.mail_categories (key, display_name, sort_order)
values ('default', '기본', 0)
on conflict (key) do nothing;

alter table public.mail_categories enable row level security;
revoke all on table public.mail_categories from anon, authenticated;

notify pgrst, 'reload schema';


-- #############################################################################
-- 15. 리더보드
-- #############################################################################

-- 리더보드(랭킹) 기능을 제공합니다.
-- 리더보드 정의는 운영(service_role)이 관리하고, 게임은 조회·기록만 합니다.
-- 플레이어 데이터 컬럼은 user_data 처럼 실제 컬럼을 추가하고,
-- 어느 리더보드가 어느 컬럼을 쓰는지는 leaderboard_table_columns 로 등록합니다.
--
-- =============================================================================
-- 리더보드 — leaderboard_tables + leaderboard_scores + leaderboard_table_columns + RPC
-- 선행: 01_servers.sql(game_servers, auth_user_server_id), 02_profiles.sql(display_names)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- leaderboard_tables — 리더보드 정의(운영 관리)
--   순위 규칙은 스키마가 아니라 이 행의 데이터다. RPC 가 읽어서 기록·정렬에 적용한다.
--   rotation(초기화 주기)과 ends_at(완전 종료)은 서로 독립된 축이다.
-- ---------------------------------------------------------------------------
create table if not exists public.leaderboard_tables (
  code                    text primary key,
  display_name            text        not null default '',
  scope                   text        not null default 'global',
  record_type             text        not null default 'highest',
  sort_type               text        not null default 'desc',
  rotation                text        not null default 'none',
  rotation_period_seconds int,
  rotation_anchor_at      timestamptz not null default now(),
  rotation_tz             text        not null default 'Asia/Seoul',
  rotation_count          int         not null default 1,
  rotation_started_at     timestamptz not null default now(),
  next_rotation_at        timestamptz,
  ends_at                 timestamptz,
  is_active               boolean     not null default true,
  created_at              timestamptz not null default now(),
  updated_at              timestamptz not null default now()
);

alter table public.leaderboard_tables
  add column if not exists ends_at timestamptz;

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'leaderboard_tables_scope_check') then
    alter table public.leaderboard_tables add constraint leaderboard_tables_scope_check
      check (scope in ('global','server'));
  end if;
  if not exists (select 1 from pg_constraint where conname = 'leaderboard_tables_record_type_check') then
    alter table public.leaderboard_tables add constraint leaderboard_tables_record_type_check
      check (record_type in ('highest','lowest','last','sum'));
  end if;
  if not exists (select 1 from pg_constraint where conname = 'leaderboard_tables_sort_type_check') then
    alter table public.leaderboard_tables add constraint leaderboard_tables_sort_type_check
      check (sort_type in ('desc','asc'));
  end if;
  if not exists (select 1 from pg_constraint where conname = 'leaderboard_tables_rotation_check') then
    alter table public.leaderboard_tables add constraint leaderboard_tables_rotation_check
      check (rotation in ('none','daily','weekly','monthly','custom'));
  end if;
end $$;

comment on table public.leaderboard_tables is
  '리더보드 정의. 기록 방식·정렬·초기화 주기·종료 시각을 데이터로 보관하며 RPC가 읽어 적용한다.';
comment on column public.leaderboard_tables.scope is 'global=전체 통합 / server=현재 접속 서버별 집계.';
comment on column public.leaderboard_tables.record_type is 'highest=최고 / lowest=최저 / last=최신 / sum=누적.';
comment on column public.leaderboard_tables.sort_type is '순위 정렬. 기록 방식과 독립.';
comment on column public.leaderboard_tables.ends_at is '종료 예약 시각. null이면 무기한. rotation과 독립된 축.';

create index if not exists leaderboard_tables_due_idx
  on public.leaderboard_tables (next_rotation_at)
  where is_active and next_rotation_at is not null;

-- ---------------------------------------------------------------------------
-- leaderboard_scores — 회차별 플레이어 기록
--   플레이어 데이터 컬럼은 ts_admin_leaderboard_add_column 으로 여기에 추가된다.
-- ---------------------------------------------------------------------------
create table if not exists public.leaderboard_scores (
  id                bigint generated always as identity primary key,
  table_code        text        not null references public.leaderboard_tables (code) on delete cascade,
  rotation_count    int         not null,
  account_id        uuid        not null references auth.users (id) on delete cascade,
  user_id           text        not null default '',
  server_id         uuid        references public.game_servers (id) on delete set null,
  score             numeric     not null default 0,
  score_achieved_at timestamptz not null default now(),
  first_recorded_at timestamptz not null default now(),
  score_count       int         not null default 1,
  updated_at        timestamptz not null default now(),
  constraint leaderboard_scores_unique unique (table_code, rotation_count, account_id)
);

comment on table public.leaderboard_scores is
  '리더보드 기록. (table_code, rotation_count, account_id) 당 1행. 지난 회차는 무한 보관한다.';
comment on column public.leaderboard_scores.score_achieved_at is
  '동점 처리 기준. 순위 점수가 실제로 바뀐 시각만 갱신한다(같은 점수 재기록으로 순위가 밀리지 않도록).';
comment on column public.leaderboard_scores.server_id is
  '기록 시점의 서버. 서버 이전 후에도 기존 회차 기록은 이전 서버 순위에 그대로 남는다.';

-- 순위 조회는 항상 (table_code, rotation_count) 로 먼저 좁히므로 다른 리더보드 행은 스캔되지 않는다.
create index if not exists leaderboard_scores_rank_idx
  on public.leaderboard_scores (table_code, rotation_count, server_id, score, score_achieved_at);
create index if not exists leaderboard_scores_account_idx
  on public.leaderboard_scores (account_id, table_code);

-- ---------------------------------------------------------------------------
-- leaderboard_table_columns — 리더보드별 사용 컬럼 등록
-- ---------------------------------------------------------------------------
create table if not exists public.leaderboard_table_columns (
  table_code  text not null references public.leaderboard_tables (code) on delete cascade,
  column_name text not null,
  sort_order  int  not null default 0,
  created_at  timestamptz not null default now(),
  primary key (table_code, column_name)
);

comment on table public.leaderboard_table_columns is
  '리더보드별로 사용할 플레이어 데이터 컬럼 등록. 물리 컬럼은 leaderboard_scores 에 공유로 존재한다.';

-- ---------------------------------------------------------------------------
-- RLS — 세 테이블 모두 클라이언트 정책 없음. 접근은 RPC 로만 한다.
-- ---------------------------------------------------------------------------
alter table public.leaderboard_tables        enable row level security;
alter table public.leaderboard_scores        enable row level security;
alter table public.leaderboard_table_columns enable row level security;

revoke all on table public.leaderboard_tables        from anon, authenticated;
revoke all on table public.leaderboard_scores        from anon, authenticated;
revoke all on table public.leaderboard_table_columns from anon, authenticated;

-- leaderboard_scores 에는 클라이언트 grant 를 주지 않는다.
--   · 순위·기록 조회는 전부 SECURITY DEFINER RPC(ts_leaderboard_*)를 통한다.
--   · 리더보드 클래스 생성기도 OpenAPI 가 아니라 ts_leaderboard_columns_meta RPC 로 컬럼을 읽는다.
--   · 직접 테이블 접근 경로가 없으므로 SELECT 노출이 필요 없다.


-- =============================================================================
-- 공통 헬퍼
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_leaderboard_next_rotation_at — 다음 회차 전환 시각
--   daily/weekly/monthly 는 rotation_tz 기준으로 앵커의 시각·요일·일자를 유지한다.
--   monthly 는 말일 초과 시 말일로 클램프한다. custom 은 앵커 + n*period.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_next_rotation_at(
  p_rotation       text,
  p_period_seconds int,
  p_anchor_at      timestamptz,
  p_tz             text        default 'Asia/Seoul',
  p_from           timestamptz default now()
)
returns timestamptz
language plpgsql
stable
as $$
declare
  v_tz     text        := coalesce(nullif(btrim(p_tz), ''), 'Asia/Seoul');
  v_from   timestamptz := coalesce(p_from, now());
  v_anchor timestamptz := coalesce(p_anchor_at, v_from);
  v_la     timestamp;
  v_lf     timestamp;
  v_next   timestamp;
  v_steps  bigint;
  v_months int;
  v_base   date;
  v_dim    int;
  v_day    int;
begin
  if p_rotation is null or p_rotation = 'none' then
    return null;
  end if;

  if p_rotation = 'custom' then
    if p_period_seconds is null or p_period_seconds <= 0 then
      raise exception 'leaderboard_rotation_period_required';
    end if;
    v_steps := floor(extract(epoch from (v_from - v_anchor)) / p_period_seconds)::bigint;
    if v_steps < 0 then
      v_steps := -1;
    end if;
    return v_anchor + ((v_steps + 1) * p_period_seconds) * interval '1 second';
  end if;

  v_la := v_anchor at time zone v_tz;
  v_lf := v_from   at time zone v_tz;
  v_next := v_la;

  if p_rotation = 'daily' then
    if v_next <= v_lf then
      v_next := v_next
              + ((floor(extract(epoch from (v_lf - v_next)) / 86400)::bigint + 1) || ' days')::interval;
    end if;

  elsif p_rotation = 'weekly' then
    if v_next <= v_lf then
      v_next := v_next
              + ((floor(extract(epoch from (v_lf - v_next)) / 604800)::bigint + 1) * 7 || ' days')::interval;
    end if;

  elsif p_rotation = 'monthly' then
    v_months := (extract(year  from v_lf)::int - extract(year  from v_la)::int) * 12
              + (extract(month from v_lf)::int - extract(month from v_la)::int);
    if v_months < 0 then
      v_months := 0;
    end if;
    loop
      v_base := (date_trunc('month', v_la::date) + (v_months || ' months')::interval)::date;
      v_dim  := extract(day from (date_trunc('month', v_base) + interval '1 month - 1 day'))::int;
      v_day  := least(extract(day from v_la)::int, v_dim);
      v_next := (v_base + (v_day - 1)) + v_la::time;
      exit when v_next > v_lf;
      v_months := v_months + 1;
    end loop;

  else
    raise exception 'leaderboard_invalid_rotation: %', p_rotation;
  end if;

  return v_next at time zone v_tz;
end;
$$;

comment on function public.ts_leaderboard_next_rotation_at(text,int,timestamptz,text,timestamptz) is
  '다음 회차 전환 시각. none이면 null. daily/weekly/monthly는 tz 기준 앵커 유지, custom은 앵커+n*period.';

-- ---------------------------------------------------------------------------
-- ts_leaderboard_columns_of — 리더보드에 등록된 컬럼 목록(실제 존재하는 것만)
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_columns_of(p_code text)
returns text[]
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(array_agg(c.column_name order by c.sort_order, c.column_name), '{}'::text[])
  from public.leaderboard_table_columns c
  where c.table_code = p_code
    and exists (
      select 1 from information_schema.columns ic
      where ic.table_schema = 'public'
        and ic.table_name = 'leaderboard_scores'
        and ic.column_name = c.column_name
    );
$$;

comment on function public.ts_leaderboard_columns_of(text) is
  '리더보드에 등록된 플레이어 데이터 컬럼 목록. 물리 컬럼이 실제 존재하는 것만 반환.';

-- ---------------------------------------------------------------------------
-- ts_leaderboard_columns_meta — 등록 컬럼의 이름 + 타입 (Unity 클래스 생성기 전용)
--   에디터 생성기가 publishable(anon) 키로 호출한다. 로그인 세션이 없어 auth 체크를 두지 않는다.
--   반환은 컬럼 스키마 메타(이름·타입)뿐이라 민감정보가 아니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_columns_meta(p_code text)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(
           jsonb_build_object('name', c.column_name, 'type', c.data_type)
           order by lc.sort_order, c.column_name
         ), '[]'::jsonb)
  from public.leaderboard_table_columns lc
  join information_schema.columns c
    on c.table_schema = 'public'
   and c.table_name   = 'leaderboard_scores'
   and c.column_name  = lc.column_name
  where lc.table_code = p_code;
$$;

comment on function public.ts_leaderboard_columns_meta(text) is
  '리더보드 등록 컬럼의 이름+타입(information_schema.data_type). Unity 클래스 생성기 전용. 무인증(anon) 허용.';

revoke all on function public.ts_leaderboard_columns_meta(text) from public;
grant execute on function public.ts_leaderboard_columns_meta(text) to anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_list_meta — 전체 리더보드 코드+이름 (Unity 클래스 생성기 전용)
--   생성기 드롭다운 채우기용. ts_leaderboard_tables 와 달리 비활성·종료된 것도 포함하고
--   무인증(anon)으로 열어, 아직 켜지 않은 리더보드의 클래스도 미리 만들 수 있게 한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_list_meta()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(
           jsonb_build_object('code', code, 'display_name', display_name)
           order by code
         ), '[]'::jsonb)
  from public.leaderboard_tables;
$$;

comment on function public.ts_leaderboard_list_meta() is
  '전체 리더보드 코드+이름(비활성 포함). Unity 클래스 생성기 드롭다운 전용. 무인증(anon) 허용.';

revoke all on function public.ts_leaderboard_list_meta() from public;
grant execute on function public.ts_leaderboard_list_meta() to anon, authenticated;


-- =============================================================================
-- 게임 클라이언트 RPC
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_leaderboard_tables — 사용 가능한 리더보드 목록
--   비활성·종료된 리더보드는 제외한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_tables()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  return coalesce((
    select jsonb_agg(
             jsonb_build_object(
               'code',             t.code,
               'display_name',     t.display_name,
               'scope',            t.scope,
               'record_type',      t.record_type,
               'sort_type',        t.sort_type,
               'rotation',         t.rotation,
               'rotation_count',   t.rotation_count,
               'next_rotation_at', t.next_rotation_at,
               'ends_at',          t.ends_at
             ) order by t.code)
    from public.leaderboard_tables t
    where t.is_active
      and (t.ends_at is null or t.ends_at > now())
  ), '[]'::jsonb);
end;
$$;

comment on function public.ts_leaderboard_tables() is
  '사용 가능한 리더보드 목록(비활성·종료 제외). SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_tables() from public, anon;
grant execute on function public.ts_leaderboard_tables() to authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_table — 리더보드 1건 상세
--   rotation_time_left: 다음 전환까지 남은 초. total_ids: 현재 회차 참여자 수.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_table(p_code text)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  t         public.leaderboard_tables%rowtype;
  v_srv     uuid;
  v_total   int;
  v_ended   boolean;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_ended := (not t.is_active) or (t.ends_at is not null and now() >= t.ends_at);
  v_srv   := public.auth_user_server_id();

  select count(*)::int into v_total
  from public.leaderboard_scores s
  where s.table_code = t.code
    and s.rotation_count = t.rotation_count
    and (t.scope <> 'server' or s.server_id = v_srv);

  return jsonb_build_object(
    'code',                t.code,
    'display_name',        t.display_name,
    'scope',               t.scope,
    'record_type',         t.record_type,
    'sort_type',           t.sort_type,
    'rotation',            t.rotation,
    'rotation_count',      t.rotation_count,
    'next_rotation_at',    t.next_rotation_at,
    'rotation_time_left',  case
                             when t.next_rotation_at is null then null
                             else greatest(0, floor(extract(epoch from (t.next_rotation_at - now())))::int)
                           end,
    'ends_at',             t.ends_at,
    'is_ended',            v_ended,
    'total_ids',           v_total,
    'columns',             to_jsonb(public.ts_leaderboard_columns_of(t.code))
  );
end;
$$;

comment on function public.ts_leaderboard_table(text) is
  '리더보드 1건 상세 + 현재 회차 참여자 수 + 다음 전환까지 남은 초. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_table(text) from public, anon;
grant execute on function public.ts_leaderboard_table(text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_submit_score — 본인 점수 기록
--   record_type 에 따라 최고/최저/최신/누적으로 갱신한다.
--   점수가 실제로 바뀐 경우에만 score_achieved_at 을 갱신한다(동점 처리 기준).
--   p_data 의 키는 이 리더보드에 등록된 컬럼이어야 한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_submit_score(
  p_code       text,
  p_score      numeric,
  p_data       jsonb default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  t        public.leaderboard_tables%rowtype;
  v_uid    uuid := auth.uid();
  v_user   text;
  v_srv    uuid;
  v_cols   text[];
  v_key    text;
  v_ins    text := '';
  v_sel    text := '';
  v_upd    text := '';
  v_sql    text;
  v_new    numeric;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_score is null then
    raise exception 'leaderboard_score_required';
  end if;

  select * into t from public.leaderboard_tables where code = p_code for share;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;
  if (not t.is_active) or (t.ends_at is not null and now() >= t.ends_at) then
    raise exception 'leaderboard_ended';
  end if;

  select p.user_id, p.server_id into v_user, v_srv
  from public.user_profiles p where p.account_id = v_uid;
  v_user := coalesce(v_user, v_uid::text);

  -- p_data 키 검증 — 등록된 컬럼만 허용
  if p_data is not null and jsonb_typeof(p_data) = 'object' then
    v_cols := public.ts_leaderboard_columns_of(t.code);
    for v_key in select jsonb_object_keys(p_data) loop
      if not (v_key = any(v_cols)) then
        raise exception 'leaderboard_column_not_allowed: %', v_key;
      end if;
      v_ins := v_ins || format(', %I', v_key);
      v_sel := v_sel || format(', d.%I', v_key);
      v_upd := v_upd || format(', %1$I = excluded.%1$I', v_key);
    end loop;
  end if;

  v_sql := format($f$
    insert into public.leaderboard_scores
      (table_code, rotation_count, account_id, user_id, server_id, score,
       score_achieved_at, first_recorded_at, score_count, updated_at %s)
    select $1, $2, $3, $4, $5, $6, now(), now(), 1, now() %s
    from jsonb_populate_record(null::public.leaderboard_scores, coalesce($7, '{}'::jsonb)) d
    on conflict (table_code, rotation_count, account_id) do update set
      score = case %L
                when 'highest' then greatest(leaderboard_scores.score, excluded.score)
                when 'lowest'  then least(leaderboard_scores.score, excluded.score)
                when 'sum'     then leaderboard_scores.score + excluded.score
                else excluded.score
              end,
      score_achieved_at = case
        when (case %L
                when 'highest' then greatest(leaderboard_scores.score, excluded.score)
                when 'lowest'  then least(leaderboard_scores.score, excluded.score)
                when 'sum'     then leaderboard_scores.score + excluded.score
                else excluded.score
              end) is distinct from leaderboard_scores.score
        then now() else leaderboard_scores.score_achieved_at end,
      -- 기록 시점의 서버를 유지한다. 회차 도중 서버를 옮겨도 그 회차 점수는 이전 서버 순위에 남는다.
      -- 최초 기록 때 프로필에 서버가 없었던 경우만 뒤늦게 채운다.
      server_id   = coalesce(leaderboard_scores.server_id, excluded.server_id),
      user_id     = excluded.user_id,
      score_count = leaderboard_scores.score_count + 1,
      updated_at  = now() %s
    returning score
  $f$, v_ins, v_sel, t.record_type, t.record_type, v_upd);

  execute v_sql
    into v_new
    using t.code, t.rotation_count, v_uid, v_user, v_srv, p_score, p_data;

  return jsonb_build_object('score', v_new, 'rotation_count', t.rotation_count);
end;
$$;

comment on function public.ts_leaderboard_submit_score(text,numeric,jsonb) is
  '본인 점수 기록. record_type(최고/최저/최신/누적) 적용, 점수가 바뀔 때만 score_achieved_at 갱신. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_submit_score(text,numeric,jsonb) from public, anon;
grant execute on function public.ts_leaderboard_submit_score(text,numeric,jsonb) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_range — 순위 범위 조회
--   p_rotation_count null 이면 현재 회차. 종료된 리더보드도 조회는 허용한다.
--   1회 최대 100건.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_range(
  p_code           text,
  p_start          int default 1,
  p_end            int default 100,
  p_rotation_count int default null
)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  t       public.leaderboard_tables%rowtype;
  v_rot   int;
  v_srv   uuid;
  v_desc  boolean;
  v_from  int := greatest(1, coalesce(p_start, 1));
  v_to    int;
  v_cols  text[];
  v_sel   text := '';
  v_data  text := '''{}''::jsonb';
  v_col   text;
  v_out   jsonb;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_rot := coalesce(p_rotation_count, t.rotation_count);
  if v_rot < 1 or v_rot > t.rotation_count then
    raise exception 'leaderboard_rotation_not_found';
  end if;

  v_to   := least(coalesce(p_end, v_from + 99), v_from + 99);
  v_desc := (t.sort_type = 'desc');
  v_srv  := public.auth_user_server_id();

  v_cols := public.ts_leaderboard_columns_of(t.code);
  if array_length(v_cols, 1) is not null then
    v_data := 'jsonb_build_object(';
    foreach v_col in array v_cols loop
      v_sel  := v_sel || format(', s.%I', v_col);
      v_data := v_data || format('%L, r.%I,', v_col, v_col);
    end loop;
    v_data := left(v_data, length(v_data) - 1) || ')';
  end if;

  execute format($f$
    select coalesce(jsonb_agg(jsonb_build_object(
             'rank',           r.rnk,
             'account_id',     r.account_id,
             'user_id',        r.user_id,
             'display_name',   r.display_name,
             'score',          r.score,
             'rotation_count', r.rotation_count,
             'data',           %s) order by r.rnk), '[]'::jsonb)
    from (
      select s.account_id, s.user_id, s.score, s.rotation_count,
             dn.display_name %s,
             rank() over (order by (case when $4 then -s.score else s.score end) asc,
                                   s.score_achieved_at asc) as rnk
      from public.leaderboard_scores s
      left join public.display_names dn on dn.account_id = s.account_id
      where s.table_code = $1
        and s.rotation_count = $2
        and ($5 is null or s.server_id = $5)
    ) r
    where r.rnk between $3 and $6
  $f$, v_data, v_sel)
  into v_out
  using t.code, v_rot, v_from, v_desc,
        case when t.scope = 'server' then v_srv else null end, v_to;

  return coalesce(v_out, '[]'::jsonb);
end;
$$;

comment on function public.ts_leaderboard_range(text,int,int,int) is
  '순위 범위 조회(1회 최대 100건). p_rotation_count=null이면 현재 회차. 종료된 리더보드도 조회 가능. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_range(text,int,int,int) from public, anon;
grant execute on function public.ts_leaderboard_range(text,int,int,int) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_player — 플레이어 순위 조회
--   p_account_id null 이면 본인. 기록이 없으면 오류가 아니라 registered=false 로 반환.
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_player(
  p_code           text,
  p_account_id     uuid default null,
  p_rotation_count int  default null
)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  t       public.leaderboard_tables%rowtype;
  v_uid   uuid := auth.uid();
  v_acc   uuid;
  v_rot   int;
  v_srv   uuid;
  v_desc  boolean;
  s       public.leaderboard_scores%rowtype;
  v_rank  int;
  v_name  text;
  v_cols  text[];
  v_data  jsonb := '{}'::jsonb;
  v_col   text;
  v_expr  text;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_acc := coalesce(p_account_id, v_uid);
  v_rot := coalesce(p_rotation_count, t.rotation_count);
  if v_rot < 1 or v_rot > t.rotation_count then
    raise exception 'leaderboard_rotation_not_found';
  end if;

  select * into s from public.leaderboard_scores
  where table_code = t.code and rotation_count = v_rot and account_id = v_acc;

  if not found then
    return jsonb_build_object(
      'registered',     false,
      'account_id',     v_acc,
      'rotation_count', v_rot);
  end if;

  v_desc := (t.sort_type = 'desc');
  v_srv  := case when t.scope = 'server' then s.server_id else null end;

  -- 나보다 앞선 기록 수 + 1 (rank() 와 동일 의미, 인덱스 레인지 카운트)
  select count(*)::int + 1 into v_rank
  from public.leaderboard_scores x
  where x.table_code = t.code
    and x.rotation_count = v_rot
    and (v_srv is null or x.server_id = v_srv)
    and (
      (v_desc     and (x.score > s.score or (x.score = s.score and x.score_achieved_at < s.score_achieved_at)))
      or
      (not v_desc and (x.score < s.score or (x.score = s.score and x.score_achieved_at < s.score_achieved_at)))
    );

  select dn.display_name into v_name from public.display_names dn where dn.account_id = v_acc;

  v_cols := public.ts_leaderboard_columns_of(t.code);
  if array_length(v_cols, 1) is not null then
    v_expr := 'jsonb_build_object(';
    foreach v_col in array v_cols loop
      v_expr := v_expr || format('%L, x.%I,', v_col, v_col);
    end loop;
    v_expr := left(v_expr, length(v_expr) - 1) || ')';
    execute format('select %s from public.leaderboard_scores x where x.id = $1', v_expr)
      into v_data using s.id;
  end if;

  return jsonb_build_object(
    'registered',     true,
    'rank',           v_rank,
    'account_id',     s.account_id,
    'user_id',        s.user_id,
    'display_name',   v_name,
    'score',          s.score,
    'rotation_count', s.rotation_count,
    'data',           v_data);
end;
$$;

comment on function public.ts_leaderboard_player(text,uuid,int) is
  '플레이어 순위 조회. p_account_id=null이면 본인. 기록 없으면 registered=false로 성공 반환. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_player(text,uuid,int) from public, anon;
grant execute on function public.ts_leaderboard_player(text,uuid,int) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_set_player_data — 본인 추가 데이터 수정(점수는 바꾸지 않음)
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_set_player_data(
  p_code           text,
  p_data           jsonb default null,
  p_rotation_count int   default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  t      public.leaderboard_tables%rowtype;
  v_uid  uuid := auth.uid();
  v_rot  int;
  v_cols text[];
  v_key  text;
  v_set  text := '';
  v_n    int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  -- 지난 회차는 확정된 결과다. 본인 것이라도 고칠 수 없다(정정은 어드민 RPC).
  v_rot := coalesce(p_rotation_count, t.rotation_count);
  if v_rot <> t.rotation_count then
    raise exception 'leaderboard_rotation_closed';
  end if;

  if p_data is not null and jsonb_typeof(p_data) = 'object' then
    v_cols := public.ts_leaderboard_columns_of(t.code);
    for v_key in select jsonb_object_keys(p_data) loop
      if not (v_key = any(v_cols)) then
        raise exception 'leaderboard_column_not_allowed: %', v_key;
      end if;
      v_set := v_set || format(', %1$I = d.%1$I', v_key);
    end loop;
  end if;

  execute format($f$
    update public.leaderboard_scores s
    set updated_at = now() %s
    from jsonb_populate_record(null::public.leaderboard_scores, coalesce($1, '{}'::jsonb)) d
    where s.table_code = $2 and s.rotation_count = $3 and s.account_id = $4
  $f$, v_set)
  using p_data, t.code, v_rot, v_uid;

  get diagnostics v_n = row_count;
  if v_n = 0 then
    raise exception 'leaderboard_score_not_found';
  end if;
end;
$$;

comment on function public.ts_leaderboard_set_player_data(text,jsonb,int) is
  '본인 등록 컬럼 수정. 점수는 바꾸지 않는다. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_set_player_data(text,jsonb,int) from public, anon;
grant execute on function public.ts_leaderboard_set_player_data(text,jsonb,int) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_leaderboard_delete_my_score — 본인 기록 삭제
-- ---------------------------------------------------------------------------
create or replace function public.ts_leaderboard_delete_my_score(
  p_code           text,
  p_rotation_count int default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  t     public.leaderboard_tables%rowtype;
  v_uid uuid := auth.uid();
  v_rot int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  -- 지난 회차는 확정된 결과다. 본인 것이라도 지울 수 없다(정정은 어드민 RPC).
  v_rot := coalesce(p_rotation_count, t.rotation_count);
  if v_rot <> t.rotation_count then
    raise exception 'leaderboard_rotation_closed';
  end if;

  delete from public.leaderboard_scores
  where table_code = t.code and rotation_count = v_rot and account_id = v_uid;
end;
$$;

comment on function public.ts_leaderboard_delete_my_score(text,int) is
  '본인 기록 삭제. 없으면 no-op. 현재 회차만. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_delete_my_score(text,int) from public, anon;
grant execute on function public.ts_leaderboard_delete_my_score(text,int) to authenticated;


-- =============================================================================
-- 회차 전환 (cron)
-- =============================================================================

create or replace function public.ts_leaderboard_rotate_due()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  r     record;
  v_cnt int := 0;
begin
  for r in
    select * from public.leaderboard_tables
    where is_active
      and rotation <> 'none'
      and next_rotation_at is not null
      and next_rotation_at <= now()
      and (ends_at is null or now() < ends_at)     -- 종료된 리더보드는 전환하지 않음
    for update
  loop
    update public.leaderboard_tables
    set rotation_count      = r.rotation_count + 1,
        rotation_started_at = now(),
        next_rotation_at    = public.ts_leaderboard_next_rotation_at(
                                r.rotation, r.rotation_period_seconds, r.rotation_anchor_at, r.rotation_tz, now()),
        updated_at          = now()
    where code = r.code;
    v_cnt := v_cnt + 1;
  end loop;

  return v_cnt;
end;
$$;

comment on function public.ts_leaderboard_rotate_due() is
  '전환 시각이 지난 리더보드의 회차를 넘긴다. 종료된 리더보드는 건너뛴다. cron 전용.';

revoke all on function public.ts_leaderboard_rotate_due() from public, anon, authenticated;

-- 전환 시각은 rotation_period_seconds 로 분 단위까지 지정할 수 있어 매분 확인한다.
do $$
begin
  if exists (select 1 from pg_extension where extname = 'pg_cron') then
    perform cron.unschedule('ts_leaderboard_rotate_due') where exists (select 1 from cron.job where jobname = 'ts_leaderboard_rotate_due');
    perform cron.schedule('ts_leaderboard_rotate_due', '* * * * *', $cron$ select public.ts_leaderboard_rotate_due(); $cron$);
  end if;
end $$;


-- =============================================================================
-- 어드민 RPC — service_role 전용 (grant 없음)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_upsert_table — 리더보드 생성·수정
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_upsert_table(
  p_code            text,
  p_display_name    text,
  p_scope           text        default 'global',
  p_record_type     text        default 'highest',
  p_sort_type       text        default 'desc',
  p_rotation        text        default 'none',
  p_period_seconds  int         default null,
  p_anchor_at       timestamptz default null,
  p_tz              text        default 'Asia/Seoul',
  p_ends_at         timestamptz default null,
  p_is_active       boolean     default true
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_code   text := nullif(btrim(p_code), '');
  v_anchor timestamptz := coalesce(p_anchor_at, now());
  v_next   timestamptz;
  v_exists boolean;
begin
  if v_code is null then
    raise exception 'leaderboard_code_required';
  end if;
  if v_code !~ '^[a-z][a-z0-9_]*$' then
    raise exception 'leaderboard_invalid_code: % (must match ^[a-z][a-z0-9_]*$)', v_code;
  end if;

  v_next := public.ts_leaderboard_next_rotation_at(p_rotation, p_period_seconds, v_anchor, p_tz, now());

  select exists(select 1 from public.leaderboard_tables where code = v_code) into v_exists;

  insert into public.leaderboard_tables as lt
    (code, display_name, scope, record_type, sort_type, rotation, rotation_period_seconds,
     rotation_anchor_at, rotation_tz, next_rotation_at, ends_at, is_active)
  values
    (v_code, coalesce(p_display_name, ''), p_scope, p_record_type, p_sort_type, p_rotation, p_period_seconds,
     v_anchor, coalesce(nullif(btrim(p_tz), ''), 'Asia/Seoul'), v_next, p_ends_at, coalesce(p_is_active, true))
  on conflict (code) do update set
    display_name            = excluded.display_name,
    scope                   = excluded.scope,
    record_type             = excluded.record_type,
    sort_type               = excluded.sort_type,
    rotation                = excluded.rotation,
    rotation_period_seconds = excluded.rotation_period_seconds,
    rotation_anchor_at      = excluded.rotation_anchor_at,
    rotation_tz             = excluded.rotation_tz,
    next_rotation_at        = excluded.next_rotation_at,
    ends_at                 = excluded.ends_at,
    is_active               = excluded.is_active,
    updated_at              = now();

  return jsonb_build_object('code', v_code, 'created', not v_exists);
end;
$$;

comment on function public.ts_admin_leaderboard_upsert_table(text,text,text,text,text,text,int,timestamptz,text,timestamptz,boolean) is
  '리더보드 생성·수정(어드민). 회차 수는 유지되며 다음 전환 시각만 재계산한다.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_delete_table — 리더보드 삭제(기록·컬럼 등록 cascade)
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_delete_table(p_code text)
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.leaderboard_tables where code = p_code;
$$;

comment on function public.ts_admin_leaderboard_delete_table(text) is
  '리더보드 삭제(어드민). 기록·컬럼 등록이 cascade로 함께 삭제된다.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_rotate — 수동 회차 전환
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_rotate(p_code text)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  t public.leaderboard_tables%rowtype;
begin
  select * into t from public.leaderboard_tables where code = p_code for update;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  update public.leaderboard_tables
  set rotation_count      = t.rotation_count + 1,
      rotation_started_at = now(),
      next_rotation_at    = public.ts_leaderboard_next_rotation_at(
                              t.rotation, t.rotation_period_seconds, t.rotation_anchor_at, t.rotation_tz, now()),
      updated_at          = now()
  where code = t.code;

  return t.rotation_count + 1;
end;
$$;

comment on function public.ts_admin_leaderboard_rotate(text) is
  '수동 회차 전환(어드민). 새 회차 번호를 반환한다.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_set_score — 운영 점수 정정
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_set_score(
  p_code           text,
  p_account_id     uuid,
  p_score          numeric,
  p_rotation_count int default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  t     public.leaderboard_tables%rowtype;
  v_rot int;
  v_n   int;
begin
  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_rot := coalesce(p_rotation_count, t.rotation_count);

  update public.leaderboard_scores
  set score             = p_score,
      score_achieved_at = case when p_score is distinct from score then now() else score_achieved_at end,
      updated_at        = now()
  where table_code = t.code and rotation_count = v_rot and account_id = p_account_id;

  get diagnostics v_n = row_count;
  if v_n = 0 then
    raise exception 'leaderboard_score_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_leaderboard_set_score(text,uuid,numeric,int) is
  '운영 점수 정정(어드민). 치팅 정정·보상 오지급 대응용. record_type 로직을 거치지 않고 값을 그대로 설정한다.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_set_player_data — 운영이 특정 플레이어 데이터 수정
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_set_player_data(
  p_code           text,
  p_account_id     uuid,
  p_data           jsonb default null,
  p_rotation_count int   default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  t      public.leaderboard_tables%rowtype;
  v_rot  int;
  v_cols text[];
  v_key  text;
  v_set  text := '';
  v_n    int;
begin
  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_rot := coalesce(p_rotation_count, t.rotation_count);

  if p_data is not null and jsonb_typeof(p_data) = 'object' then
    v_cols := public.ts_leaderboard_columns_of(t.code);
    for v_key in select jsonb_object_keys(p_data) loop
      if not (v_key = any(v_cols)) then
        raise exception 'leaderboard_column_not_allowed: %', v_key;
      end if;
      v_set := v_set || format(', %1$I = d.%1$I', v_key);
    end loop;
  end if;

  execute format($f$
    update public.leaderboard_scores s
    set updated_at = now() %s
    from jsonb_populate_record(null::public.leaderboard_scores, coalesce($1, '{}'::jsonb)) d
    where s.table_code = $2 and s.rotation_count = $3 and s.account_id = $4
  $f$, v_set)
  using p_data, t.code, v_rot, p_account_id;

  get diagnostics v_n = row_count;
  if v_n = 0 then
    raise exception 'leaderboard_score_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_leaderboard_set_player_data(text,uuid,jsonb,int) is
  '운영이 특정 플레이어의 등록 컬럼을 수정(어드민).';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_delete_score — 운영이 플레이어 기록 삭제
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_delete_score(
  p_code           text,
  p_account_id     uuid,
  p_rotation_count int default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  t     public.leaderboard_tables%rowtype;
  v_rot int;
begin
  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_rot := coalesce(p_rotation_count, t.rotation_count);

  delete from public.leaderboard_scores
  where table_code = t.code and rotation_count = v_rot and account_id = p_account_id;
end;
$$;

comment on function public.ts_admin_leaderboard_delete_score(text,uuid,int) is
  '운영이 플레이어 기록 삭제(어드민). 없으면 no-op.';


-- =============================================================================
-- 플레이어 데이터 컬럼 관리 — service_role 전용
--   04_user_data.sql 의 admin_add_user_data_column 구조화 인자판과 동일한 방식.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_add_column — leaderboard_scores 에 물리 컬럼 추가
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_add_column(
  p_colname     text,
  p_coltype     text,
  p_nullable    boolean default true,
  p_default_sql text    default null
)
returns void
language plpgsql
security definer
set search_path = public, pg_temp
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
as $$
declare
  colname        text := nullif(btrim(p_colname), '');
  coltype        text := nullif(btrim(p_coltype), '');
  default_sql    text := nullif(btrim(p_default_sql), '');
  notnull_sql    text;
  default_clause text;
  v_reserved     text[] := array['id','table_code','rotation_count','account_id','user_id','server_id',
                                 'score','score_achieved_at','first_recorded_at',
                                 'score_count','updated_at'];
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if coltype is null then
    raise exception 'Column type is required';
  end if;
  if colname !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
    raise exception 'Invalid column name: %', colname;
  end if;
  if colname = any(v_reserved) then
    raise exception 'Reserved column name: %', colname;
  end if;
  if position(';' in coalesce(p_coltype, '')) > 0 or position(';' in coalesce(default_sql, '')) > 0 then
    raise exception 'Invalid definition: semicolon is not allowed';
  end if;
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'leaderboard_scores' and column_name = colname
  ) then
    raise exception 'Column already exists: %', colname;
  end if;

  notnull_sql    := case when coalesce(p_nullable, true) then '' else 'not null' end;
  default_clause := case when default_sql is null then '' else 'default ' || default_sql end;

  execute format('alter table public.leaderboard_scores add column %I %s %s %s',
                 colname, coltype, notnull_sql, default_clause);
end;
$$;

comment on function public.ts_admin_leaderboard_add_column(text,text,boolean,text) is
  'leaderboard_scores 에 플레이어 데이터 컬럼 추가(어드민). 예약 컬럼 거부, 이미 있으면 오류.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_update_column — NOT NULL / DEFAULT 변경
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_update_column(
  p_colname     text,
  p_nullable    boolean,
  p_default_sql text default null
)
returns void
language plpgsql
security definer
set search_path = public, pg_temp
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
as $$
declare
  colname     text := nullif(btrim(p_colname), '');
  default_sql text := nullif(btrim(p_default_sql), '');
  v_reserved  text[] := array['id','table_code','rotation_count','account_id','user_id','server_id',
                              'score','score_achieved_at','first_recorded_at',
                              'score_count','updated_at'];
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if colname !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
    raise exception 'Invalid column name: %', colname;
  end if;
  if colname = any(v_reserved) then
    raise exception 'Reserved column name: %', colname;
  end if;
  if position(';' in coalesce(p_default_sql, '')) > 0 then
    raise exception 'Invalid default: semicolon is not allowed';
  end if;
  if not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'leaderboard_scores' and column_name = colname
  ) then
    raise exception 'Column does not exist: %', colname;
  end if;

  if p_nullable then
    execute format('alter table public.leaderboard_scores alter column %I drop not null', colname);
  else
    execute format('alter table public.leaderboard_scores alter column %I set not null', colname);
  end if;

  if default_sql is null then
    execute format('alter table public.leaderboard_scores alter column %I drop default', colname);
  else
    execute format('alter table public.leaderboard_scores alter column %I set default %s', colname, default_sql);
  end if;
end;
$$;

comment on function public.ts_admin_leaderboard_update_column(text,boolean,text) is
  'leaderboard_scores 컬럼의 NOT NULL/DEFAULT 변경(어드민). 예약 컬럼 거부.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_drop_column — 물리 컬럼 삭제
--   어떤 리더보드에도 등록돼 있지 않을 때만 허용한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_drop_column(p_colname text)
returns void
language plpgsql
security definer
set search_path = public, pg_temp
set lock_timeout = '3s'   -- DDL은 ACCESS EXCLUSIVE 락. 오래 잡히면 게임 전체가 멈추므로 빨리 포기한다.
as $$
declare
  colname    text := nullif(btrim(p_colname), '');
  v_used     text;
  v_reserved text[] := array['id','table_code','rotation_count','account_id','user_id','server_id',
                             'score','score_achieved_at','first_recorded_at',
                             'score_count','updated_at'];
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if colname !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
    raise exception 'Invalid column name: %', colname;
  end if;
  if colname = any(v_reserved) then
    raise exception 'Reserved column name: %', colname;
  end if;
  if not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'leaderboard_scores' and column_name = colname
  ) then
    raise exception 'Column does not exist: %', colname;
  end if;

  select string_agg(table_code, ', ' order by table_code) into v_used
  from public.leaderboard_table_columns where column_name = colname;

  if v_used is not null then
    raise exception 'Column is still attached to leaderboard(s): %', v_used;
  end if;

  execute format('alter table public.leaderboard_scores drop column %I', colname);
end;
$$;

comment on function public.ts_admin_leaderboard_drop_column(text) is
  'leaderboard_scores 컬럼 삭제(어드민). 리더보드에 등록돼 있으면 거부한다.';

-- ---------------------------------------------------------------------------
-- ts_admin_leaderboard_attach_column / detach_column — 리더보드에 컬럼 등록·해제
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_leaderboard_attach_column(
  p_code       text,
  p_colname    text,
  p_sort_order int default 0
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  colname text := nullif(btrim(p_colname), '');
begin
  if colname is null then
    raise exception 'Column name is required';
  end if;
  if not exists (select 1 from public.leaderboard_tables where code = p_code) then
    raise exception 'leaderboard_table_not_found';
  end if;
  if not exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'leaderboard_scores' and column_name = colname
  ) then
    raise exception 'Column does not exist: %', colname;
  end if;

  insert into public.leaderboard_table_columns (table_code, column_name, sort_order)
  values (p_code, colname, coalesce(p_sort_order, 0))
  on conflict (table_code, column_name) do update set sort_order = excluded.sort_order;
end;
$$;

comment on function public.ts_admin_leaderboard_attach_column(text,text,int) is
  '리더보드에 플레이어 데이터 컬럼 등록(어드민).';

create or replace function public.ts_admin_leaderboard_detach_column(
  p_code    text,
  p_colname text
)
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.leaderboard_table_columns
  where table_code = p_code and column_name = btrim(p_colname);
$$;

comment on function public.ts_admin_leaderboard_detach_column(text,text) is
  '리더보드에서 플레이어 데이터 컬럼 등록 해제(어드민). 물리 컬럼은 남는다.';


-- =============================================================================
-- 어드민·cron RPC 권한 — 클라이언트에 열지 않는다
-- =============================================================================
revoke all on function public.ts_leaderboard_next_rotation_at(text,int,timestamptz,text,timestamptz) from public, anon, authenticated;
revoke all on function public.ts_leaderboard_columns_of(text) from public, anon, authenticated;
revoke all on function public.ts_leaderboard_rotate_due() from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_upsert_table(text,text,text,text,text,text,int,timestamptz,text,timestamptz,boolean) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_delete_table(text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_rotate(text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_set_score(text,uuid,numeric,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_set_player_data(text,uuid,jsonb,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_delete_score(text,uuid,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_add_column(text,text,boolean,text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_update_column(text,boolean,text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_drop_column(text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_attach_column(text,text,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_detach_column(text,text) from public, anon, authenticated;

notify pgrst, 'reload schema';


-- #############################################################################
-- 16. 운영자 스키마 변경 버전관리
-- #############################################################################

-- =============================================================================
-- 17_admin_schema_versions.sql — 운영자 스키마 변경 버전관리(스테이징 → 게시 → 롤백)
--
-- 운영자가 Retool에서 하는 라이브 스키마 작업(리더보드 필드·테이블, user_data 필드,
-- RemoteConfig 키)을 즉시 적용하지 않고 draft로 쌓아뒀다가, "게시" 한 번으로
-- 한 트랜잭션에서 원자 적용한다. 각 게시가 하나의 버전이 되고 이전 버전으로 롤백한다.
--
-- 게시/롤백은 기존 admin DDL RPC(ts_admin_leaderboard_*_column, admin_*_user_data_column 등)를
-- 그대로 호출한다. 그 함수들은 SECURITY DEFINER라 이 함수의 트랜잭션을 공유하므로, 배치 중
-- 하나라도 raise 하면 트랜잭션 전체가 롤백되어 라이브 스키마가 어중간하게 남지 않는다.
--
-- 전부 service_role 전용(클라이언트 grant 없음). 선행: 16_leaderboard.sql, 04_user_data.sql, 08_remote_config.sql.
--
-- params 규약 (Retool 백엔드가 이 형태로 stage 한다):
--   leaderboard_field / user_data_field
--     add    : {colname, coltype, nullable, default_sql}
--     update : {colname, nullable, default_sql}
--     drop   : {colname}
--   leaderboard_field 전용
--     attach : {code, colname, sort_order}
--     detach : {code, colname}
--   leaderboard_table
--     create/update : {code, display_name, scope, record_type, sort_type, rotation,
--                      period_seconds, anchor_at, tz, ends_at, is_active}
--     delete : {code}
--   remote_config (행 단위 — Retool 이 최종 value_json 을 계산해 넘긴다)
--     add/update : {key, value_json, enabled, requires_auth, description}
--     delete : {key}
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 테이블
-- ---------------------------------------------------------------------------
create table if not exists public.ts_schema_draft (
  id          bigint generated always as identity primary key,
  created_at  timestamptz not null default now(),
  operator    text,
  feature     text not null,   -- leaderboard_field | leaderboard_table | user_data_field | remote_config
  action      text not null,   -- add | update | drop | attach | detach | create | delete
  object_name text not null,   -- 컬럼명 / 리더보드 code / config key
  params      jsonb not null default '{}'::jsonb,
  sort_order  int  not null default 0,
  status      text not null default 'pending'   -- pending | discarded
);

comment on table public.ts_schema_draft is
  '게시 대기 중인 운영자 스키마 변경(라이브 미반영). 게시 성공 시 비운다.';

create table if not exists public.ts_schema_version (
  id           bigint generated always as identity primary key,
  published_at timestamptz not null default now(),
  operator     text,
  label        text,
  ops          jsonb not null default '[]'::jsonb,  -- [{feature,action,object_name,params,before_state,reversible}]
  reversible   boolean not null default true,       -- 배치 전체가 되돌릴 수 있는지(파괴적 op 하나라도 있으면 false)
  status       text not null default 'published',   -- published | reverted
  reverted_at  timestamptz,
  reverted_by  text
);

comment on table public.ts_schema_version is
  '스키마 변경 게시 이력(버전). ops에 적용된 배치와 op별 before_state를 담아 롤백에 쓴다.';

alter table public.ts_schema_draft   enable row level security;
alter table public.ts_schema_version enable row level security;
-- 클라이언트 정책 0개. service_role(Retool/대시보드)만 접근한다.

-- ---------------------------------------------------------------------------
-- ts_admin_schema_stage — 변경을 draft에 담는다(라이브 무영향)
--   상세 검증은 게시 시 실제 admin RPC가 최종 게이트로 수행한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_schema_stage(
  p_feature     text,
  p_action      text,
  p_object_name text,
  p_params      jsonb default '{}'::jsonb,
  p_operator    text  default null
)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id   bigint;
  v_sort int;
begin
  if p_feature not in ('leaderboard_field','leaderboard_table','user_data_field','remote_config') then
    raise exception 'schema_stage_invalid_feature: %', p_feature;
  end if;
  if p_action not in ('add','update','drop','attach','detach','create','delete') then
    raise exception 'schema_stage_invalid_action: %', p_action;
  end if;
  if coalesce(btrim(p_object_name), '') = '' then
    raise exception 'schema_stage_object_required';
  end if;

  -- 중복 방지 — 같은 변경(feature+action+object+params)이 이미 대기 중이면 그 id 재사용.
  -- 모달에서 같은 버튼을 두 번 눌러도 draft에 한 번만 담긴다.
  select id into v_id
  from public.ts_schema_draft
  where status = 'pending'
    and feature = p_feature and action = p_action and object_name = p_object_name
    and params = coalesce(p_params, '{}'::jsonb)
  limit 1;
  if v_id is not null then
    return v_id;
  end if;

  select coalesce(max(sort_order), 0) + 1 into v_sort
  from public.ts_schema_draft where status = 'pending';

  insert into public.ts_schema_draft (operator, feature, action, object_name, params, sort_order)
  values (p_operator, p_feature, p_action, p_object_name, coalesce(p_params, '{}'::jsonb), v_sort)
  returning id into v_id;

  return v_id;
end;
$$;

comment on function public.ts_admin_schema_stage(text,text,text,jsonb,text) is
  '운영자 스키마 변경을 draft에 적재(라이브 미반영). 게시 전까지 대기.';

-- ---------------------------------------------------------------------------
-- 내부: 컬럼 현재 상태 스냅샷(update/drop 롤백용)
-- ---------------------------------------------------------------------------
create or replace function public.ts_schema_col_snapshot(p_table text, p_column text)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
           'is_nullable',    c.is_nullable,
           'column_default', c.column_default,
           'data_type',      c.data_type)
  from information_schema.columns c
  where c.table_schema = 'public' and c.table_name = p_table and c.column_name = p_column;
$$;

-- ---------------------------------------------------------------------------
-- ts_admin_schema_publish — draft 전체를 한 트랜잭션에서 원자 적용
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_schema_publish(
  p_operator text default null,
  p_label    text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  d            record;
  v_key        text;
  v_before     jsonb;
  v_op_rev     boolean;
  v_ops        jsonb := '[]'::jsonb;
  v_all_rev    boolean := true;
  v_version_id bigint;
  v_count      int := 0;
begin
  for d in
    select * from public.ts_schema_draft
    where status = 'pending'
    order by sort_order, id
    for update
  loop
    v_key := d.feature || '.' || d.action;
    v_before := null;
    v_op_rev := true;

    -- ── 리더보드 필드 ─────────────────────────────────────────────────
    if v_key = 'leaderboard_field.add' then
      perform public.ts_admin_leaderboard_add_column(
        d.params->>'colname', d.params->>'coltype',
        coalesce((d.params->>'nullable')::boolean, true), d.params->>'default_sql');

    elsif v_key = 'leaderboard_field.update' then
      v_before := public.ts_schema_col_snapshot('leaderboard_scores', d.params->>'colname');
      perform public.ts_admin_leaderboard_update_column(
        d.params->>'colname',
        coalesce((d.params->>'nullable')::boolean, true), d.params->>'default_sql');

    elsif v_key = 'leaderboard_field.drop' then
      v_before := public.ts_schema_col_snapshot('leaderboard_scores', d.params->>'colname');
      v_op_rev := false;
      perform public.ts_admin_leaderboard_drop_column(d.params->>'colname');

    elsif v_key = 'leaderboard_field.attach' then
      select jsonb_build_object('existed', true, 'sort_order', sort_order) into v_before
      from public.leaderboard_table_columns
      where table_code = d.params->>'code' and column_name = d.params->>'colname';
      perform public.ts_admin_leaderboard_attach_column(
        d.params->>'code', d.params->>'colname', coalesce((d.params->>'sort_order')::int, 0));

    elsif v_key = 'leaderboard_field.detach' then
      select jsonb_build_object('existed', true, 'sort_order', sort_order) into v_before
      from public.leaderboard_table_columns
      where table_code = d.params->>'code' and column_name = d.params->>'colname';
      perform public.ts_admin_leaderboard_detach_column(d.params->>'code', d.params->>'colname');

    -- ── user_data 필드 ───────────────────────────────────────────────
    elsif v_key = 'user_data_field.add' then
      perform public.admin_add_user_data_column(
        d.params->>'colname', d.params->>'coltype',
        coalesce((d.params->>'nullable')::boolean, true), d.params->>'default_sql');

    elsif v_key = 'user_data_field.update' then
      v_before := public.ts_schema_col_snapshot('user_data', d.params->>'colname');
      perform public.admin_update_user_data_column(
        d.params->>'colname',
        coalesce((d.params->>'nullable')::boolean, true), d.params->>'default_sql');

    elsif v_key = 'user_data_field.drop' then
      v_before := public.ts_schema_col_snapshot('user_data', d.params->>'colname');
      v_op_rev := false;
      perform public.admin_drop_user_data_column(d.params->>'colname');

    -- ── 리더보드 테이블(정의) ────────────────────────────────────────
    elsif v_key = 'leaderboard_table.create' then
      perform public.ts_admin_leaderboard_upsert_table(
        d.params->>'code', d.params->>'display_name',
        coalesce(d.params->>'scope','global'), coalesce(d.params->>'record_type','highest'),
        coalesce(d.params->>'sort_type','desc'), coalesce(d.params->>'rotation','none'),
        (d.params->>'period_seconds')::int, (d.params->>'anchor_at')::timestamptz,
        coalesce(d.params->>'tz','Asia/Seoul'), (d.params->>'ends_at')::timestamptz,
        coalesce((d.params->>'is_active')::boolean, true));

    elsif v_key = 'leaderboard_table.update' then
      select to_jsonb(t) into v_before from public.leaderboard_tables t where code = d.params->>'code';
      perform public.ts_admin_leaderboard_upsert_table(
        d.params->>'code', d.params->>'display_name',
        coalesce(d.params->>'scope','global'), coalesce(d.params->>'record_type','highest'),
        coalesce(d.params->>'sort_type','desc'), coalesce(d.params->>'rotation','none'),
        (d.params->>'period_seconds')::int, (d.params->>'anchor_at')::timestamptz,
        coalesce(d.params->>'tz','Asia/Seoul'), (d.params->>'ends_at')::timestamptz,
        coalesce((d.params->>'is_active')::boolean, true));

    elsif v_key = 'leaderboard_table.delete' then
      select to_jsonb(t) into v_before from public.leaderboard_tables t where code = d.params->>'code';
      v_op_rev := false;  -- cascade 로 회차 기록 소실
      perform public.ts_admin_leaderboard_delete_table(d.params->>'code');

    -- ── RemoteConfig (행 단위) ───────────────────────────────────────
    elsif v_key = 'remote_config.add' then
      insert into public.remote_config (key, value_json, enabled, requires_auth, description)
      values (d.params->>'key', coalesce(d.params->'value_json','{}'::jsonb),
              coalesce((d.params->>'enabled')::boolean, true),
              coalesce((d.params->>'requires_auth')::boolean, false),
              d.params->>'description')
      on conflict (key) do update set
        value_json = excluded.value_json, enabled = excluded.enabled,
        requires_auth = excluded.requires_auth, description = excluded.description,
        updated_at = now(), version = public.remote_config.version + 1;

    elsif v_key = 'remote_config.update' then
      select to_jsonb(rc) into v_before from public.remote_config rc where key = d.params->>'key';
      insert into public.remote_config (key, value_json, enabled, requires_auth, description)
      values (d.params->>'key', coalesce(d.params->'value_json','{}'::jsonb),
              coalesce((d.params->>'enabled')::boolean, true),
              coalesce((d.params->>'requires_auth')::boolean, false),
              d.params->>'description')
      on conflict (key) do update set
        value_json = excluded.value_json, enabled = excluded.enabled,
        requires_auth = excluded.requires_auth, description = excluded.description,
        updated_at = now(), version = public.remote_config.version + 1;

    elsif v_key = 'remote_config.delete' then
      select to_jsonb(rc) into v_before from public.remote_config rc where key = d.params->>'key';
      delete from public.remote_config where key = d.params->>'key';

    else
      raise exception 'schema_feature_not_implemented: %', v_key;
    end if;

    v_ops := v_ops || jsonb_build_object(
      'feature',     d.feature,
      'action',      d.action,
      'object_name', d.object_name,
      'params',      d.params,
      'before_state', v_before,
      'reversible',  v_op_rev);
    if not v_op_rev then v_all_rev := false; end if;
    v_count := v_count + 1;
  end loop;

  if v_count = 0 then
    raise exception 'schema_draft_empty';
  end if;

  insert into public.ts_schema_version (operator, label, ops, reversible)
  values (p_operator, p_label, v_ops, v_all_rev)
  returning id into v_version_id;

  delete from public.ts_schema_draft where status = 'pending';

  return jsonb_build_object('version_id', v_version_id, 'op_count', v_count, 'reversible', v_all_rev);
end;
$$;

comment on function public.ts_admin_schema_publish(text,text) is
  'draft 전체를 한 트랜잭션에서 원자 적용하고 버전 1건을 기록한다. 하나라도 실패하면 전체 롤백.';

-- ---------------------------------------------------------------------------
-- ts_admin_schema_revert — 특정 버전의 ops를 역순으로 되돌린다
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_schema_revert(
  p_version_id bigint,
  p_operator   text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v        public.ts_schema_version%rowtype;
  op       jsonb;
  v_key    text;
  v_params jsonb;
  v_before jsonb;
  i        int;
begin
  select * into v from public.ts_schema_version where id = p_version_id for update;
  if not found then
    raise exception 'schema_version_not_found';
  end if;
  if v.status = 'reverted' then
    raise exception 'schema_version_already_reverted';
  end if;
  if not v.reversible then
    raise exception 'schema_version_not_reversible';  -- 파괴적 op 포함
  end if;

  -- 역순으로 역연산
  for i in reverse jsonb_array_length(v.ops) - 1 .. 0 loop
    op       := v.ops -> i;
    v_key    := (op->>'feature') || '.' || (op->>'action');
    v_params := op->'params';
    v_before := op->'before_state';

    -- 리더보드 필드
    if v_key = 'leaderboard_field.add' then
      perform public.ts_admin_leaderboard_drop_column(v_params->>'colname');
    elsif v_key = 'leaderboard_field.update' then
      perform public.ts_admin_leaderboard_update_column(
        v_params->>'colname', coalesce((v_before->>'is_nullable') = 'YES', true), v_before->>'column_default');
    elsif v_key = 'leaderboard_field.attach' then
      if coalesce((v_before->>'existed')::boolean, false) then
        perform public.ts_admin_leaderboard_attach_column(
          v_params->>'code', v_params->>'colname', coalesce((v_before->>'sort_order')::int, 0));
      else
        perform public.ts_admin_leaderboard_detach_column(v_params->>'code', v_params->>'colname');
      end if;
    elsif v_key = 'leaderboard_field.detach' then
      perform public.ts_admin_leaderboard_attach_column(
        v_params->>'code', v_params->>'colname', coalesce((v_before->>'sort_order')::int, 0));

    -- user_data 필드
    elsif v_key = 'user_data_field.add' then
      perform public.admin_drop_user_data_column(v_params->>'colname');
    elsif v_key = 'user_data_field.update' then
      perform public.admin_update_user_data_column(
        v_params->>'colname', coalesce((v_before->>'is_nullable') = 'YES', true), v_before->>'column_default');

    -- 리더보드 테이블
    elsif v_key = 'leaderboard_table.create' then
      perform public.ts_admin_leaderboard_delete_table(v_params->>'code');
    elsif v_key = 'leaderboard_table.update' then
      perform public.ts_admin_leaderboard_upsert_table(
        v_before->>'code', v_before->>'display_name', v_before->>'scope', v_before->>'record_type',
        v_before->>'sort_type', v_before->>'rotation', (v_before->>'rotation_period_seconds')::int,
        (v_before->>'rotation_anchor_at')::timestamptz, v_before->>'rotation_tz',
        (v_before->>'ends_at')::timestamptz, (v_before->>'is_active')::boolean);

    -- RemoteConfig
    elsif v_key = 'remote_config.add' then
      delete from public.remote_config where key = v_params->>'key';
    elsif v_key = 'remote_config.update' or v_key = 'remote_config.delete' then
      insert into public.remote_config
      select * from jsonb_populate_record(null::public.remote_config, v_before)
      on conflict (key) do update set
        value_json = excluded.value_json, updated_at = excluded.updated_at, version = excluded.version,
        enabled = excluded.enabled, description = excluded.description, requires_auth = excluded.requires_auth;

    else
      raise exception 'schema_revert_not_implemented: %', v_key;
    end if;
  end loop;

  update public.ts_schema_version
     set status = 'reverted', reverted_at = now(), reverted_by = p_operator
   where id = p_version_id;

  return jsonb_build_object('version_id', p_version_id, 'reverted', true);
end;
$$;

comment on function public.ts_admin_schema_revert(bigint,text) is
  '한 버전의 변경을 역순으로 되돌린다(한 트랜잭션). 파괴적 op 포함 버전은 거부.';

-- ---------------------------------------------------------------------------
-- RemoteConfig 전용 스테이징 — 값이 jsonb 병합이라 키당 대기 op를 하나로 유지하고
-- 유효 기준(대기 op 있으면 그 값, 없으면 라이브 행)에 편집을 얹어 전체 행을 담는다.
-- 이렇게 해야 "키 추가 → 항목 추가 → 게시"처럼 순차 편집이 올바르게 합쳐진다.
-- ---------------------------------------------------------------------------
create or replace function public._ts_config_base(p_key text)
returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare v jsonb;
begin
  select params into v from public.ts_schema_draft
   where status='pending' and feature='remote_config' and object_name=p_key order by id limit 1;
  if v is not null then return v; end if;
  select jsonb_build_object('key',key,'value_json',coalesce(value_json,'{}'::jsonb),
                            'enabled',enabled,'requires_auth',requires_auth,'description',description)
    into v from public.remote_config where key=p_key;
  return v;
end; $$;

create or replace function public._ts_stage_config_row(
  p_key text, p_value_json jsonb, p_enabled boolean, p_requires_auth boolean, p_description text, p_operator text)
returns bigint
language plpgsql security definer set search_path = public as $$
declare v_pending_action text; v_action text; v_sort int; v_id bigint;
begin
  select action into v_pending_action from public.ts_schema_draft
   where status='pending' and feature='remote_config' and object_name=p_key order by id limit 1;
  if v_pending_action = 'add'
     or (v_pending_action is null and not exists(select 1 from public.remote_config where key=p_key)) then
    v_action := 'add';
  else
    v_action := 'update';
  end if;
  delete from public.ts_schema_draft
   where status='pending' and feature='remote_config' and object_name=p_key;
  select coalesce(max(sort_order),0)+1 into v_sort from public.ts_schema_draft where status='pending';
  insert into public.ts_schema_draft(operator, feature, action, object_name, params, sort_order)
  values (p_operator, 'remote_config', v_action, p_key,
          jsonb_build_object('key',p_key,'value_json',coalesce(p_value_json,'{}'::jsonb),
                             'enabled',p_enabled,'requires_auth',p_requires_auth,'description',p_description),
          v_sort)
  returning id into v_id;
  return v_id;
end; $$;

-- 키 신규 생성
create or replace function public.ts_admin_schema_stage_config_new(
  p_key text, p_enabled boolean, p_requires_auth boolean, p_operator text default null)
returns bigint language plpgsql security definer set search_path = public as $$
begin
  return public._ts_stage_config_row(p_key, '{}'::jsonb, coalesce(p_enabled,true), coalesce(p_requires_auth,false), null, p_operator);
end; $$;

-- 키 메타(enabled/requires_auth) 수정
create or replace function public.ts_admin_schema_stage_config_meta(
  p_key text, p_enabled boolean, p_requires_auth boolean, p_operator text default null)
returns bigint language plpgsql security definer set search_path = public as $$
declare b jsonb := public._ts_config_base(p_key);
begin
  if b is null then raise exception 'remote_config_key_not_found: %', p_key; end if;
  return public._ts_stage_config_row(p_key, b->'value_json', coalesce(p_enabled,true), coalesce(p_requires_auth,false), b->>'description', p_operator);
end; $$;

-- 항목 추가·수정(값 세팅). 백엔드가 타입→jsonb 변환·검증을 하고 결과 jsonb와 C# 타입을 넘긴다.
create or replace function public.ts_admin_schema_stage_config_item(
  p_key text, p_item_key text, p_item_value jsonb, p_meta_type text, p_operator text default null)
returns bigint language plpgsql security definer set search_path = public as $$
declare b jsonb := public._ts_config_base(p_key); vj jsonb; meta jsonb;
begin
  if b is null then raise exception 'remote_config_key_not_found: %', p_key; end if;
  if p_item_key = '__meta' then raise exception 'reserved_item_key'; end if;
  vj := coalesce(b->'value_json','{}'::jsonb) || jsonb_build_object(p_item_key, p_item_value);
  if p_meta_type is null then
    meta := coalesce(vj->'__meta','{}'::jsonb) - p_item_key;
  else
    meta := coalesce(vj->'__meta','{}'::jsonb) || jsonb_build_object(p_item_key, to_jsonb(p_meta_type));
  end if;
  vj := vj || jsonb_build_object('__meta', meta);
  return public._ts_stage_config_row(p_key, vj, coalesce((b->>'enabled')::boolean,true),
                                     coalesce((b->>'requires_auth')::boolean,false), b->>'description', p_operator);
end; $$;

-- 항목 삭제
create or replace function public.ts_admin_schema_stage_config_item_delete(
  p_key text, p_item_key text, p_operator text default null)
returns bigint language plpgsql security definer set search_path = public as $$
declare b jsonb := public._ts_config_base(p_key); vj jsonb;
begin
  if b is null then raise exception 'remote_config_key_not_found: %', p_key; end if;
  vj := coalesce(b->'value_json','{}'::jsonb) - p_item_key;
  if vj ? '__meta' then
    vj := vj || jsonb_build_object('__meta', (vj->'__meta') - p_item_key);
  end if;
  return public._ts_stage_config_row(p_key, vj, coalesce((b->>'enabled')::boolean,true),
                                     coalesce((b->>'requires_auth')::boolean,false), b->>'description', p_operator);
end; $$;

-- 키 삭제. 라이브에 없던 키(대기 add)를 지우면 순 변화 없음 → 아무 것도 안 담음.
create or replace function public.ts_admin_schema_stage_config_delete(
  p_key text, p_operator text default null)
returns bigint language plpgsql security definer set search_path = public as $$
declare v_pending_action text; v_sort int; v_id bigint;
begin
  select action into v_pending_action from public.ts_schema_draft
   where status='pending' and feature='remote_config' and object_name=p_key order by id limit 1;
  delete from public.ts_schema_draft
   where status='pending' and feature='remote_config' and object_name=p_key;
  if v_pending_action = 'add' then return null; end if;
  if not exists(select 1 from public.remote_config where key=p_key) then return null; end if;
  select coalesce(max(sort_order),0)+1 into v_sort from public.ts_schema_draft where status='pending';
  insert into public.ts_schema_draft(operator, feature, action, object_name, params, sort_order)
  values (p_operator, 'remote_config', 'delete', p_key, jsonb_build_object('key',p_key), v_sort)
  returning id into v_id;
  return v_id;
end; $$;

-- ---------------------------------------------------------------------------
-- 권한 — service_role 전용. public/anon/authenticated 전부 회수.
-- ---------------------------------------------------------------------------
revoke all on function public.ts_admin_schema_stage(text,text,text,jsonb,text)  from public, anon, authenticated;
revoke all on function public.ts_admin_schema_publish(text,text)                from public, anon, authenticated;
revoke all on function public.ts_admin_schema_revert(bigint,text)               from public, anon, authenticated;
revoke all on function public.ts_schema_col_snapshot(text,text)                 from public, anon, authenticated;
revoke all on function public._ts_config_base(text)                             from public, anon, authenticated;
revoke all on function public._ts_stage_config_row(text,jsonb,boolean,boolean,text,text) from public, anon, authenticated;
revoke all on function public.ts_admin_schema_stage_config_new(text,boolean,boolean,text)  from public, anon, authenticated;
revoke all on function public.ts_admin_schema_stage_config_meta(text,boolean,boolean,text) from public, anon, authenticated;
revoke all on function public.ts_admin_schema_stage_config_item(text,text,jsonb,text,text)  from public, anon, authenticated;
revoke all on function public.ts_admin_schema_stage_config_item_delete(text,text,text)      from public, anon, authenticated;
revoke all on function public.ts_admin_schema_stage_config_delete(text,text)                from public, anon, authenticated;

notify pgrst, 'reload schema';


-- #############################################################################
-- 17. 쿠폰
-- #############################################################################

-- 쿠폰 기능을 제공합니다.
-- 쿠폰 정의·발급은 운영(service_role)이 하고, 게임은 코드를 보내 사용하는 것만 합니다.
-- 보상은 게임에 직접 주지 않고 우편(mails)으로 넣습니다 — 실제 지급은 기존 IMailItemHandler 경로를 그대로 씁니다.
--
-- 쿠폰은 두 종류입니다.
--   normal  : 발급 수량만큼 랜덤 코드를 만들고, 코드 1개당 1회만 쓸 수 있습니다.
--   keyword : 운영자가 코드를 직접 정하고, 최대 횟수까지 여러 명이 씁니다(한 플레이어는 1회).
--
-- =============================================================================
-- 쿠폰 — coupons + coupon_codes + coupon_redemptions + RPC
-- 선행: 02_profiles.sql(user_profiles), 06_mails.sql(mails), 12_admin_mail.sql(game_items)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- coupons — 쿠폰 정의. 보상·유효기간·사용 규칙이 모두 이 행의 데이터다.
--   expires_at 이 null 이면 무기한, max_uses 가 null 이면 무제한(keyword 전용).
-- ---------------------------------------------------------------------------
create table if not exists public.coupons (
  id                uuid        primary key default gen_random_uuid(),
  kind              text        not null default 'normal',
  title             text        not null default '',
  content           text        not null default '',
  category          text        not null default 'default',
  items             jsonb,
  localized         jsonb,
  expires_at        timestamptz,
  mail_expires_days int         not null default 7,
  max_uses          int,
  used_count        int         not null default 0,
  is_active         boolean     not null default true,
  created_by        text,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

alter table public.coupons add column if not exists kind              text        not null default 'normal';
alter table public.coupons add column if not exists title             text        not null default '';
alter table public.coupons add column if not exists content           text        not null default '';
alter table public.coupons add column if not exists category          text        not null default 'default';
alter table public.coupons add column if not exists items             jsonb;
alter table public.coupons add column if not exists localized         jsonb;
alter table public.coupons add column if not exists expires_at        timestamptz;
alter table public.coupons add column if not exists mail_expires_days int         not null default 7;
alter table public.coupons add column if not exists max_uses          int;
alter table public.coupons add column if not exists used_count        int         not null default 0;
alter table public.coupons add column if not exists is_active         boolean     not null default true;
alter table public.coupons add column if not exists created_by        text;
alter table public.coupons add column if not exists created_at        timestamptz not null default now();
alter table public.coupons add column if not exists updated_at        timestamptz not null default now();

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'coupons_kind_chk') then
    alter table public.coupons
      add constraint coupons_kind_chk check (kind in ('normal', 'keyword'));
  end if;
  if not exists (select 1 from pg_constraint where conname = 'coupons_mail_expires_days_chk') then
    alter table public.coupons
      add constraint coupons_mail_expires_days_chk check (mail_expires_days > 0);
  end if;
end $$;

create index if not exists coupons_active_idx  on public.coupons (is_active, expires_at);
create index if not exists coupons_created_idx on public.coupons (created_at desc);

comment on table  public.coupons is
  '쿠폰 정의. 보상은 items(jsonb 배열 [{key,count}])이며 사용 시 mails 로 지급된다. kind=normal 은 코드 1회, keyword 는 1인 1회.';
comment on column public.coupons.expires_at is '쿠폰 사용 마감. null 이면 무기한.';
comment on column public.coupons.max_uses   is 'keyword 전용 최대 사용 횟수. null 이면 무제한.';
comment on column public.coupons.mail_expires_days is '지급 우편의 수령 기한(일). 쿠폰 만료와 별개다.';

-- ---------------------------------------------------------------------------
-- coupon_codes — 발급된 코드. normal 은 수량만큼, keyword 는 1개.
--   코드는 항상 대문자로 정규화해 저장한다(입력도 대문자로 맞춰 비교).
-- ---------------------------------------------------------------------------
create table if not exists public.coupon_codes (
  code       text        primary key,
  coupon_id  uuid        not null references public.coupons(id) on delete cascade,
  created_at timestamptz not null default now()
);

alter table public.coupon_codes add column if not exists created_at timestamptz not null default now();

create index if not exists coupon_codes_coupon_idx on public.coupon_codes (coupon_id);

comment on table public.coupon_codes is '발급된 쿠폰 코드. 대문자 정규화. 쿠폰 삭제 시 함께 삭제된다.';

-- ---------------------------------------------------------------------------
-- coupon_redemptions — 사용 이력 전체.
--   중복 사용 차단을 이 테이블의 부분 유니크 인덱스로 표현한다. 종류마다 규칙이 다르므로
--   kind 를 비정규화해 둔다.
--     normal  : 코드 1개는 한 번만       → unique (code)
--     keyword : 한 플레이어는 1회         → unique (coupon_id, account_id)
--   normal 은 같은 쿠폰의 다른 코드를 여러 장 받았다면 각각 쓸 수 있다(코드 = 개별 선물).
-- ---------------------------------------------------------------------------
create table if not exists public.coupon_redemptions (
  id          bigserial   primary key,
  coupon_id   uuid        not null references public.coupons(id) on delete cascade,
  code        text        not null,
  kind        text        not null,
  account_id  uuid        not null,
  user_id     text        not null default '',
  mail_id     uuid,
  redeemed_at timestamptz not null default now()
);

alter table public.coupon_redemptions add column if not exists kind        text        not null default 'normal';
alter table public.coupon_redemptions add column if not exists user_id     text        not null default '';
alter table public.coupon_redemptions add column if not exists mail_id     uuid;
alter table public.coupon_redemptions add column if not exists redeemed_at timestamptz not null default now();

create unique index if not exists coupon_redemptions_code_uidx
  on public.coupon_redemptions (code) where kind = 'normal';
create unique index if not exists coupon_redemptions_keyword_uidx
  on public.coupon_redemptions (coupon_id, account_id) where kind = 'keyword';

create index if not exists coupon_redemptions_coupon_idx  on public.coupon_redemptions (coupon_id, redeemed_at desc);
create index if not exists coupon_redemptions_account_idx on public.coupon_redemptions (account_id, redeemed_at desc);

comment on table public.coupon_redemptions is
  '쿠폰 사용 이력. 부분 유니크 인덱스가 중복 사용을 막는다(normal=코드 1회, keyword=1인 1회).';

-- ---------------------------------------------------------------------------
-- 권한 — 세 테이블 모두 클라이언트 직접 접근 없음. 사용은 ts_coupon_redeem RPC 로만.
-- ---------------------------------------------------------------------------
alter table public.coupons            enable row level security;
alter table public.coupon_codes       enable row level security;
alter table public.coupon_redemptions enable row level security;

revoke all on table public.coupons            from anon, authenticated;
revoke all on table public.coupon_codes       from anon, authenticated;
revoke all on table public.coupon_redemptions from anon, authenticated;


-- =============================================================================
-- 클라이언트 RPC
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_coupon_redeem — 쿠폰 사용. 성공하면 보상 우편이 생성된다.
--   실패 사유: coupon_not_found · coupon_inactive · coupon_expired
--              coupon_already_used · coupon_exhausted
-- ---------------------------------------------------------------------------
drop function if exists public.ts_coupon_redeem(text);

create or replace function public.ts_coupon_redeem(p_code text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid    uuid := auth.uid();
  v_user   text;
  v_code   text;
  c        public.coupons%rowtype;
  v_red_id bigint;
  v_mail   uuid;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  v_code := upper(btrim(coalesce(p_code, '')));
  if v_code = '' then
    raise exception 'coupon_not_found';
  end if;

  select cp.* into c
    from public.coupon_codes cc
    join public.coupons cp on cp.id = cc.coupon_id
   where cc.code = v_code;
  if not found then
    raise exception 'coupon_not_found';
  end if;

  if not c.is_active then
    raise exception 'coupon_inactive';
  end if;
  if c.expires_at is not null and c.expires_at <= now() then
    raise exception 'coupon_expired';
  end if;

  select p.user_id into v_user from public.user_profiles p where p.account_id = v_uid;
  v_user := coalesce(v_user, v_uid::text);

  -- 이력을 먼저 넣어 중복 사용을 유니크 인덱스로 잡는다.
  -- 뒤에서 예외가 나면 함수 전체가 롤백되므로 이력만 남는 일은 없다.
  begin
    insert into public.coupon_redemptions (coupon_id, code, kind, account_id, user_id)
    values (c.id, v_code, c.kind, v_uid, v_user)
    returning id into v_red_id;
  exception when unique_violation then
    raise exception 'coupon_already_used';
  end;

  -- 키워드는 최대 횟수를 원자적으로 확인·증가한다(동시 요청에서 초과 지급 방지).
  if c.kind = 'keyword' then
    update public.coupons
       set used_count = used_count + 1, updated_at = now()
     where id = c.id
       and (max_uses is null or used_count < max_uses);
    if not found then
      raise exception 'coupon_exhausted';
    end if;
  else
    update public.coupons
       set used_count = used_count + 1, updated_at = now()
     where id = c.id;
  end if;

  -- 발신자 이름은 저장하지 않는다. sender_type='system' 이면 게임이 자기 로케일로 표기하므로
  -- DB에 특정 언어 문자열을 박으면 다른 언어권 플레이어에게 그대로 나간다.
  insert into public.mails
    (account_id, user_id, sender_type, title, content,
     expires_at, created_at, items, category, localized)
  values
    (v_uid, v_user, 'system', c.title, c.content,
     now() + make_interval(days => c.mail_expires_days), now(), c.items, c.category, c.localized)
  returning id into v_mail;

  update public.coupon_redemptions set mail_id = v_mail where id = v_red_id;

  return jsonb_build_object('mail_id', v_mail);
end;
$$;

comment on function public.ts_coupon_redeem(text) is
  '쿠폰 사용. 코드 검증 후 보상을 우편으로 지급하고 이력을 남긴다. 중복 사용은 부분 유니크 인덱스로 차단.';

revoke all on function public.ts_coupon_redeem(text) from public, anon;
grant execute on function public.ts_coupon_redeem(text) to authenticated;


-- =============================================================================
-- 운영자 RPC (service_role 전용)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_create — 쿠폰 생성. kind 로 분기한다.
--   normal  : p_prefix + 랜덤 p_random_len 자리로 p_quantity 개 생성
--   keyword : p_code 1개 생성, p_max_uses 로 최대 횟수 지정(null=무제한)
--   랜덤 문자셋에서 혼동하기 쉬운 0·O·1·I 는 뺀다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_coupon_create(text,text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text);
drop function if exists public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text);

create or replace function public.ts_admin_coupon_create(
  p_kind              text,
  p_title             text,
  p_content           text    default '',
  p_category          text    default 'default',
  p_items             jsonb   default null,
  p_localized         jsonb   default null,
  p_expires_at        timestamptz default null,
  p_mail_expires_days int     default 7,
  p_is_active         boolean default true,
  p_code              text    default null,
  p_max_uses          int     default null,
  p_prefix            text    default '',
  p_random_len        int     default 6,
  p_quantity          int     default 1,
  p_created_by        text    default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_charset  constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  v_id       uuid;
  v_key      text;
  v_cnt      text;
  v_prefix   text;
  v_code     text;
  v_made     int := 0;
  v_attempts int := 0;
  v_max_try  int;
  i          int;
begin
  if p_kind not in ('normal', 'keyword') then
    raise exception 'invalid_coupon_kind: %', p_kind;
  end if;
  if coalesce(btrim(p_title), '') = '' then
    raise exception 'title_empty';
  end if;
  if coalesce(p_mail_expires_days, 0) <= 0 then
    raise exception 'invalid_mail_expires_days';
  end if;

  -- 보상 아이템은 카탈로그에 있는 키만 허용한다(우편 발송과 같은 규칙).
  if p_items is not null then
    if jsonb_typeof(p_items) <> 'array' then
      raise exception 'items_not_array';
    end if;
    for v_key, v_cnt in
      select e->>'key', e->>'count' from jsonb_array_elements(p_items) e
    loop
      if coalesce(btrim(v_key), '') = '' then
        raise exception 'item_key_empty';
      end if;
      if v_cnt !~ '^[0-9]+$' or v_cnt::bigint <= 0 then
        raise exception 'item_count_invalid: %', v_key;
      end if;
      if not exists (select 1 from public.game_items g where g.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  insert into public.coupons
    (kind, title, content, category, items, localized,
     expires_at, mail_expires_days, max_uses, is_active, created_by)
  values
    (p_kind, btrim(p_title), coalesce(p_content, ''),
     coalesce(nullif(btrim(p_category), ''), 'default'), p_items, p_localized,
     p_expires_at, p_mail_expires_days,
     case when p_kind = 'keyword' then p_max_uses else null end,
     coalesce(p_is_active, true), p_created_by)
  returning id into v_id;

  if p_kind = 'keyword' then
    v_code := upper(btrim(coalesce(p_code, '')));
    if v_code = '' then
      raise exception 'coupon_code_required';
    end if;
    if v_code !~ '^[A-Z0-9_-]+$' then
      raise exception 'invalid_coupon_code: %', v_code;
    end if;
    if exists (select 1 from public.coupon_codes where code = v_code) then
      raise exception 'coupon_code_taken: %', v_code;
    end if;

    insert into public.coupon_codes (code, coupon_id) values (v_code, v_id);
    return jsonb_build_object('coupon_id', v_id, 'code', v_code, 'issued', 1);
  end if;

  -- normal — 접두사 + 랜덤
  if coalesce(p_quantity, 0) <= 0 then
    raise exception 'invalid_quantity';
  end if;
  if coalesce(p_random_len, 0) <= 0 then
    raise exception 'invalid_random_len';
  end if;

  v_prefix := upper(btrim(coalesce(p_prefix, '')));
  if v_prefix <> '' and v_prefix !~ '^[A-Z0-9_-]+$' then
    raise exception 'invalid_coupon_prefix: %', v_prefix;
  end if;

  -- 충돌로 무한 반복하지 않도록 시도 횟수를 제한한다.
  v_max_try := greatest(p_quantity * 20, 1000);

  while v_made < p_quantity and v_attempts < v_max_try loop
    v_code := v_prefix;
    for i in 1..p_random_len loop
      v_code := v_code || substr(v_charset, 1 + floor(random() * length(v_charset))::int, 1);
    end loop;

    insert into public.coupon_codes (code, coupon_id)
    values (v_code, v_id)
    on conflict (code) do nothing;

    if found then
      v_made := v_made + 1;
    end if;
    v_attempts := v_attempts + 1;
  end loop;

  if v_made < p_quantity then
    raise exception 'coupon_code_space_exhausted: made % of %', v_made, p_quantity;
  end if;

  return jsonb_build_object('coupon_id', v_id, 'issued', v_made);
end;
$$;

comment on function public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text) is
  '쿠폰 생성(어드민). normal 은 접두사+랜덤으로 수량만큼, keyword 는 지정 코드 1개. items 는 game_items 검증.';

revoke all on function public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_update — 쿠폰 수정. 코드는 바꾸지 않는다(이미 배포됐을 수 있음).
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_coupon_update(uuid,text,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean);
drop function if exists public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean);

create or replace function public.ts_admin_coupon_update(
  p_id                uuid,
  p_title             text,
  p_content           text    default '',
  p_category          text    default 'default',
  p_items             jsonb   default null,
  p_localized         jsonb   default null,
  p_expires_at        timestamptz default null,
  p_mail_expires_days int     default 7,
  p_max_uses          int     default null,
  p_is_active         boolean default true
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_kind text;
  v_key  text;
  v_cnt  text;
begin
  select kind into v_kind from public.coupons where id = p_id;
  if not found then
    raise exception 'coupon_not_found';
  end if;
  if coalesce(btrim(p_title), '') = '' then
    raise exception 'title_empty';
  end if;
  if coalesce(p_mail_expires_days, 0) <= 0 then
    raise exception 'invalid_mail_expires_days';
  end if;

  if p_items is not null then
    if jsonb_typeof(p_items) <> 'array' then
      raise exception 'items_not_array';
    end if;
    for v_key, v_cnt in
      select e->>'key', e->>'count' from jsonb_array_elements(p_items) e
    loop
      if coalesce(btrim(v_key), '') = '' then
        raise exception 'item_key_empty';
      end if;
      if v_cnt !~ '^[0-9]+$' or v_cnt::bigint <= 0 then
        raise exception 'item_count_invalid: %', v_key;
      end if;
      if not exists (select 1 from public.game_items g where g.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  update public.coupons
     set title             = btrim(p_title),
         content           = coalesce(p_content, ''),
         category          = coalesce(nullif(btrim(p_category), ''), 'default'),
         items             = p_items,
         localized         = p_localized,
         expires_at        = p_expires_at,
         mail_expires_days = p_mail_expires_days,
         max_uses          = case when v_kind = 'keyword' then p_max_uses else null end,
         is_active         = coalesce(p_is_active, true),
         updated_at        = now()
   where id = p_id;
end;
$$;

comment on function public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean) is
  '쿠폰 수정(어드민). 발급된 코드는 건드리지 않는다.';

revoke all on function public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_delete — 쿠폰 삭제. 코드·이력도 함께 사라진다(cascade).
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_coupon_delete(uuid);

create or replace function public.ts_admin_coupon_delete(p_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  delete from public.coupons where id = p_id;
  if not found then
    raise exception 'coupon_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_coupon_delete(uuid) is
  '쿠폰 삭제(어드민). coupon_codes·coupon_redemptions 도 cascade 로 함께 삭제된다.';

revoke all on function public.ts_admin_coupon_delete(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_delete(uuid) to service_role;


notify pgrst, 'reload schema';


-- #############################################################################
-- 18. 채팅
-- #############################################################################

-- 게임 내 채팅을 제공합니다.
-- 채널 정의(chat_channels) + 메시지(chat_messages) + 뮤트(chat_mutes) 구조입니다.
--
-- 채널은 종류(kind)와 범위(scope_key) 두 축으로 갈립니다. 채널 행은 "정의"일 뿐이고,
-- 실제로 대화가 갈라지는 단위는 메시지의 scope_key 입니다.
--   global : scope_key = ''                  누구나 같은 방
--   server : scope_key = 서버 id             같은 서버끼리
--   group  : scope_key = 게임이 정한 키       길드·파티 (미구현)
--   direct : scope_key = 정렬된 계정쌍         귓속말 (미구현)
-- 서버가 늘어도 채널 행을 추가할 필요가 없습니다.
--
-- =============================================================================
-- 채팅 — chat_channels + chat_messages + chat_mutes + RPC
-- 선행: 01(game_servers, auth_user_server_id), 02(user_profiles, display_names)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- chat_channels — 채널 정의. 운영자가 Retool 에서 만들고 설정을 바꾼다.
--   slow_mode_seconds = 0 이면 도배 제한 없음. 값을 넣으면 그때부터 적용된다.
-- ---------------------------------------------------------------------------
create table if not exists public.chat_channels (
  id                uuid        primary key default gen_random_uuid(),
  kind              text        not null default 'global',
  code              text        not null default '',
  display_name      text        not null default '',
  is_active         boolean     not null default true,
  slow_mode_seconds int         not null default 0,
  max_length        int         not null default 200,
  retention_days    int         not null default 7,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

alter table public.chat_channels add column if not exists kind              text        not null default 'global';
alter table public.chat_channels add column if not exists code              text        not null default '';
alter table public.chat_channels add column if not exists display_name      text        not null default '';
alter table public.chat_channels add column if not exists is_active         boolean     not null default true;
alter table public.chat_channels add column if not exists slow_mode_seconds int         not null default 0;
alter table public.chat_channels add column if not exists max_length        int         not null default 200;
alter table public.chat_channels add column if not exists retention_days    int         not null default 7;
alter table public.chat_channels add column if not exists created_at        timestamptz not null default now();
alter table public.chat_channels add column if not exists updated_at        timestamptz not null default now();

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'chat_channels_kind_chk') then
    alter table public.chat_channels
      add constraint chat_channels_kind_chk check (kind in ('global','server','group','direct'));
  end if;
  if not exists (select 1 from pg_constraint where conname = 'chat_channels_slow_chk') then
    alter table public.chat_channels
      add constraint chat_channels_slow_chk check (slow_mode_seconds >= 0);
  end if;
  if not exists (select 1 from pg_constraint where conname = 'chat_channels_len_chk') then
    alter table public.chat_channels
      add constraint chat_channels_len_chk check (max_length between 1 and 4000);
  end if;
  if not exists (select 1 from pg_constraint where conname = 'chat_channels_retention_chk') then
    alter table public.chat_channels
      add constraint chat_channels_retention_chk check (retention_days > 0);
  end if;
end $$;

create unique index if not exists chat_channels_code_uidx on public.chat_channels (code);

comment on table  public.chat_channels is
  '채팅 채널 정의. 대화가 실제로 갈라지는 단위는 chat_messages.scope_key 다. 서버별 채널을 따로 만들 필요가 없다.';
comment on column public.chat_channels.slow_mode_seconds is '같은 사람의 연속 채팅 최소 간격(초). 0 이면 제한 없음.';
comment on column public.chat_channels.retention_days    is '메시지 보관 기간(일). 지난 메시지는 크론이 지운다.';

-- ---------------------------------------------------------------------------
-- chat_messages — 메시지. id 가 조회 커서다.
--   시각이 아니라 bigserial 을 커서로 쓰는 이유: 같은 시각의 메시지가 겹치면
--   경계에서 빠뜨리거나 중복해서 받는다.
-- ---------------------------------------------------------------------------
create table if not exists public.chat_messages (
  id         bigserial   primary key,
  channel_id uuid        not null references public.chat_channels(id) on delete cascade,
  scope_key  text        not null default '',
  account_id   uuid        not null,
  user_id      text        not null default '',
  display_name text        not null default '',
  content      text        not null,
  created_at   timestamptz not null default now(),
  deleted_at   timestamptz,
  deleted_by   text
);

alter table public.chat_messages add column if not exists scope_key    text not null default '';
alter table public.chat_messages add column if not exists user_id      text not null default '';
alter table public.chat_messages add column if not exists display_name text not null default '';
alter table public.chat_messages add column if not exists deleted_at   timestamptz;
alter table public.chat_messages add column if not exists deleted_by   text;

-- 쓰기가 잦은 테이블이라 인덱스를 최소로 둔다.
--   커서 조회는 항상 (채널, 범위, id > 커서) 형태고, 정리 작업은 채널별 created_at 범위다.
create index if not exists chat_messages_cursor_idx  on public.chat_messages (channel_id, scope_key, id);
create index if not exists chat_messages_cleanup_idx on public.chat_messages (channel_id, created_at);

comment on table public.chat_messages is
  '채팅 메시지. id 가 조회 커서. scope_key 로 전체·서버·길드·귓속말이 갈린다.';
comment on column public.chat_messages.scope_key is
  'global='''' · server=서버 id · group=게임이 정한 키 · direct=정렬된 계정쌍.';
comment on column public.chat_messages.display_name is
  '보낸 시점의 닉네임 스냅샷. 개명해도 과거 대화는 그때 이름으로 남는다. 조회에서 조인을 없애려는 목적도 있다.';

-- ---------------------------------------------------------------------------
-- chat_mutes — 채팅 차단. channel_id 가 null 이면 전 채널.
--   도배 제한(slow_mode)을 끈 상태에서 악용을 막는 실질적인 수단이다.
-- ---------------------------------------------------------------------------
create table if not exists public.chat_mutes (
  id         bigserial   primary key,
  account_id uuid        not null,
  channel_id uuid        references public.chat_channels(id) on delete cascade,
  until      timestamptz not null,
  reason     text        not null default '',
  created_by text,
  created_at timestamptz not null default now()
);

create index if not exists chat_mutes_lookup_idx on public.chat_mutes (account_id, until desc);

comment on table public.chat_mutes is '채팅 차단. channel_id 가 null 이면 모든 채널에 적용된다.';

-- ---------------------------------------------------------------------------
-- 권한 — 클라이언트 직접 접근 없음. 발송·조회는 RPC 로만.
--   메시지를 테이블로 직접 열면 삭제된 메시지·다른 서버 대화가 새어 나간다.
-- ---------------------------------------------------------------------------
alter table public.chat_channels enable row level security;
alter table public.chat_messages enable row level security;
alter table public.chat_mutes    enable row level security;

revoke all on table public.chat_channels from anon, authenticated;
revoke all on table public.chat_messages from anon, authenticated;
revoke all on table public.chat_mutes    from anon, authenticated;


-- =============================================================================
-- 내부 헬퍼
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_chat_scope_key — 채널 종류에 맞는 내 범위 키를 만든다.
--   호출자가 범위를 지정하지 못하게 서버에서 계산한다. 클라이언트가 넘기면
--   다른 서버 대화에 끼어들 수 있다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_chat_scope_key(p_kind text, p_uid uuid)
returns text
language plpgsql
stable
security definer
set search_path = public
as $$
declare v_server uuid;
begin
  if p_kind = 'global' then
    return '';
  elsif p_kind = 'server' then
    select server_id into v_server from public.user_profiles where account_id = p_uid;
    if v_server is null then
      raise exception 'chat_scope_unavailable';
    end if;
    return v_server::text;
  end if;

  -- group·direct 는 아직 구현하지 않았다. 채널 종류를 바꾸면 여기부터 손댄다.
  raise exception 'chat_kind_unsupported: %', p_kind;
end;
$$;

comment on function public.ts_chat_scope_key(text, uuid) is
  '채널 종류별 범위 키. 클라이언트가 지정하지 못하도록 서버에서 계산한다.';

revoke all on function public.ts_chat_scope_key(text, uuid) from public, anon, authenticated;


-- =============================================================================
-- 클라이언트 RPC
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_chat_send — 메시지 발송.
--   실패 사유: chat_channel_not_found · chat_channel_inactive · chat_muted
--              chat_message_empty · chat_message_too_long · chat_too_fast
-- ---------------------------------------------------------------------------
drop function if exists public.ts_chat_send(text, text);

create or replace function public.ts_chat_send(p_code text, p_content text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid    uuid := auth.uid();
  v_user   text;
  v_name   text;
  c        public.chat_channels%rowtype;
  v_scope  text;
  v_text   text;
  v_last   timestamptz;
  v_id     bigint;
  v_at     timestamptz;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into c from public.chat_channels where code = btrim(coalesce(p_code, ''));
  if not found then
    raise exception 'chat_channel_not_found';
  end if;
  if not c.is_active then
    raise exception 'chat_channel_inactive';
  end if;

  v_text := btrim(coalesce(p_content, ''));
  if v_text = '' then
    raise exception 'chat_message_empty';
  end if;
  if char_length(v_text) > c.max_length then
    raise exception 'chat_message_too_long';
  end if;

  if exists (
    select 1 from public.chat_mutes m
     where m.account_id = v_uid
       and m.until > now()
       and (m.channel_id is null or m.channel_id = c.id)
  ) then
    raise exception 'chat_muted';
  end if;

  v_scope := public.ts_chat_scope_key(c.kind, v_uid);

  -- 도배 제한은 값이 설정된 채널에서만 동작한다. 기본값 0 = 제한 없음.
  if c.slow_mode_seconds > 0 then
    select max(created_at) into v_last
      from public.chat_messages
     where channel_id = c.id and scope_key = v_scope and account_id = v_uid;
    if v_last is not null and v_last > now() - make_interval(secs => c.slow_mode_seconds) then
      raise exception 'chat_too_fast';
    end if;
  end if;

  select user_id into v_user from public.user_profiles  where account_id = v_uid;
  select display_name into v_name from public.display_names where account_id = v_uid;

  insert into public.chat_messages (channel_id, scope_key, account_id, user_id, display_name, content)
  values (c.id, v_scope, v_uid, coalesce(v_user, v_uid::text), coalesce(v_name, ''), v_text)
  returning id, created_at into v_id, v_at;

  return jsonb_build_object('id', v_id, 'created_at', v_at);
end;
$$;

comment on function public.ts_chat_send(text, text) is
  '채팅 발송. 채널 상태·길이·뮤트·도배를 검사한 뒤 저장한다. 범위는 서버가 계산한다.';

revoke all on function public.ts_chat_send(text, text) from public, anon;
grant execute on function public.ts_chat_send(text, text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_chat_fetch_many — 여러 채널을 한 번에 커서 조회.
--   {"shout": 120, "server": 87} 를 받아 {"shout": [...], "server": [...]} 로 돌려준다.
--   채널마다 따로 부르면 폴링 요청이 채널 수만큼 늘어나므로 한 번에 묶는다.
--   after_id 가 0 이면 그 채널의 최근 p_limit 개를 준다(첫 진입).
--   채널 설정(max_length 등)은 싣지 않는다 — 정적인 값이라 ts_chat_channels 로 한 번만 받는다.
--   삭제된 메시지는 내용 없이 deleted 로 표시해 내려보낸다 — 클라이언트가
--   이미 표시 중인 말풍선을 지울 수 있어야 하기 때문이다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_chat_fetch(text, bigint, int);
drop function if exists public.ts_chat_fetch_many(jsonb, int);

create or replace function public.ts_chat_fetch_many(p_cursors jsonb, p_limit int default 50)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid        uuid := auth.uid();
  v_limit      int;
  v_server     uuid;
  v_server_got boolean := false;
  v_out        jsonb := '{}'::jsonb;
  v_code       text;
  v_after      bigint;
  c            public.chat_channels%rowtype;
  v_scope      text;
  v_rows       jsonb;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_cursors is null or jsonb_typeof(p_cursors) <> 'object' then
    raise exception 'chat_cursors_invalid';
  end if;

  v_limit := least(greatest(coalesce(p_limit, 50), 1), 200);

  for v_code, v_after in
    select key, coalesce((value #>> '{}')::bigint, 0) from jsonb_each(p_cursors)
  loop
    select * into c from public.chat_channels where code = v_code;
    if not found then
      raise exception 'chat_channel_not_found: %', v_code;
    end if;

    -- 서버 범위는 한 번만 읽어 모든 서버 채널에 재사용한다.
    if c.kind = 'server' then
      if not v_server_got then
        select server_id into v_server from public.user_profiles where account_id = v_uid;
        v_server_got := true;
      end if;
      if v_server is null then
        raise exception 'chat_scope_unavailable';
      end if;
      v_scope := v_server::text;
    elsif c.kind = 'global' then
      v_scope := '';
    else
      raise exception 'chat_kind_unsupported: %', c.kind;
    end if;

    if v_after <= 0 then
      select coalesce(jsonb_agg(t order by t.id), '[]'::jsonb) into v_rows
      from (
        select m.id, m.account_id, m.user_id, m.display_name,
               case when m.deleted_at is null then m.content else null end as content,
               (m.deleted_at is not null) as deleted, m.created_at
          from public.chat_messages m
         where m.channel_id = c.id and m.scope_key = v_scope
         order by m.id desc limit v_limit
      ) t;
    else
      select coalesce(jsonb_agg(t order by t.id), '[]'::jsonb) into v_rows
      from (
        select m.id, m.account_id, m.user_id, m.display_name,
               case when m.deleted_at is null then m.content else null end as content,
               (m.deleted_at is not null) as deleted, m.created_at
          from public.chat_messages m
         where m.channel_id = c.id and m.scope_key = v_scope and m.id > v_after
         order by m.id limit v_limit
      ) t;
    end if;

    v_out := v_out || jsonb_build_object(v_code, v_rows);
  end loop;

  return v_out;
end;
$$;

comment on function public.ts_chat_fetch_many(jsonb, int) is
  '여러 채널을 한 번에 커서 조회. {"code": after_id} 를 받아 {"code": [메시지]} 로 반환.';

revoke all on function public.ts_chat_fetch_many(jsonb, int) from public, anon;
grant execute on function public.ts_chat_fetch_many(jsonb, int) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_chat_channels — 내가 쓸 수 있는 채널 목록.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_chat_channels();

create or replace function public.ts_chat_channels()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  return coalesce((
    select jsonb_agg(jsonb_build_object(
             'code', code, 'kind', kind, 'display_name', display_name,
             'max_length', max_length, 'slow_mode_seconds', slow_mode_seconds)
           order by code)
      from public.chat_channels
     where is_active and kind in ('global','server')
  ), '[]'::jsonb);
end;
$$;

comment on function public.ts_chat_channels() is '사용 가능한 채팅 채널 목록. 비활성 채널은 제외.';

revoke all on function public.ts_chat_channels() from public, anon;
grant execute on function public.ts_chat_channels() to authenticated;


-- =============================================================================
-- 운영자 RPC (service_role 전용)
-- =============================================================================

drop function if exists public.ts_admin_chat_delete_message(bigint, text);

create or replace function public.ts_admin_chat_delete_message(p_id bigint, p_by text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  update public.chat_messages
     set deleted_at = now(), deleted_by = p_by
   where id = p_id and deleted_at is null;
  if not found then
    raise exception 'chat_message_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_chat_delete_message(bigint, text) is
  '메시지 숨김(어드민). 행은 남기고 표시만 지운다 — 신고 처리 이력을 위해서다.';

revoke all on function public.ts_admin_chat_delete_message(bigint, text) from public, anon, authenticated;
grant execute on function public.ts_admin_chat_delete_message(bigint, text) to service_role;

drop function if exists public.ts_admin_chat_mute(uuid, uuid, int, text, text);

create or replace function public.ts_admin_chat_mute(
  p_account_id uuid,
  p_channel_id uuid default null,
  p_minutes    int  default 60,
  p_reason     text default '',
  p_by         text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if coalesce(p_minutes, 0) <= 0 then
    raise exception 'invalid_mute_minutes';
  end if;

  insert into public.chat_mutes (account_id, channel_id, until, reason, created_by)
  values (p_account_id, p_channel_id, now() + make_interval(mins => p_minutes), coalesce(p_reason, ''), p_by);
end;
$$;

comment on function public.ts_admin_chat_mute(uuid, uuid, int, text, text) is
  '채팅 차단(어드민). channel_id 가 null 이면 전 채널.';

revoke all on function public.ts_admin_chat_mute(uuid, uuid, int, text, text) from public, anon, authenticated;
grant execute on function public.ts_admin_chat_mute(uuid, uuid, int, text, text) to service_role;

-- ---------------------------------------------------------------------------
-- ts_chat_cleanup — 보존 기간이 지난 메시지 삭제. 크론이 부른다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_chat_cleanup();
drop function if exists public.ts_chat_cleanup(int, int);

create or replace function public.ts_chat_cleanup(p_batch int default 5000, p_max_batches int default 50)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  c       record;
  v_total int := 0;
  v_n     int;
  v_runs  int := 0;
begin
  -- 한 번에 다 지우면 대량일 때 긴 트랜잭션이 되어 락을 오래 잡고 WAL 이 튄다.
  -- 배치로 끊어 지우고, 한 실행에서 처리할 최대치를 둔다. 남은 것은 다음 실행이 가져간다.
  for c in select id, retention_days from public.chat_channels loop
    loop
      exit when v_runs >= p_max_batches;

      delete from public.chat_messages
       where ctid in (
         select ctid from public.chat_messages
          where channel_id = c.id
            and created_at < now() - make_interval(days => c.retention_days)
          limit p_batch
       );
      get diagnostics v_n = row_count;

      v_total := v_total + v_n;
      v_runs  := v_runs + 1;
      exit when v_n = 0;
    end loop;
  end loop;

  return v_total;
end;
$$;

comment on function public.ts_chat_cleanup(int, int) is
  '보존 기간이 지난 채팅 메시지를 배치로 삭제. 한 실행의 상한은 p_batch * p_max_batches 건. cron 이 매시간 호출한다.';

revoke all on function public.ts_chat_cleanup(int, int) from public, anon, authenticated;

do $$
begin
  if exists (select 1 from pg_extension where extname = 'pg_cron') then
    perform cron.unschedule('ts_chat_cleanup') where exists (select 1 from cron.job where jobname = 'ts_chat_cleanup');
    perform cron.schedule('ts_chat_cleanup', '17 * * * *', $cron$ select public.ts_chat_cleanup(); $cron$);
  end if;
end $$;

-- ---------------------------------------------------------------------------
-- 기본 채널 — 없을 때만 만든다. 운영자가 Retool 에서 이름·설정을 바꾼다.
-- ---------------------------------------------------------------------------
insert into public.chat_channels (kind, code, display_name, max_length, retention_days)
values ('global', 'shout',  '외치기',   100, 3),
       ('server', 'server', '서버 채팅', 200, 7)
on conflict (code) do nothing;

notify pgrst, 'reload schema';


-- #############################################################################
-- 19. 클라이언트 권한 최소화
-- #############################################################################

-- 클라이언트 롤(anon·authenticated)의 테이블·함수 권한을 실제로 필요한 범위까지 좁힙니다.
--
-- Supabase 기본 권한은 public 스키마의 모든 테이블에 ALL(TRUNCATE 포함), 모든 함수에 EXECUTE 를
-- anon·authenticated 까지 부여합니다. 테이블은 RLS 가 막아 주지만 TRUNCATE 는 RLS 를 우회하고,
-- 함수는 RLS 와 무관하므로 SECURITY DEFINER 관리 함수가 그대로 노출됩니다.
--
-- [주의] `revoke ... from public` 만으로는 부족합니다. PUBLIC 유사롤만 회수될 뿐,
--        기본 권한이 anon·authenticated 에 직접 부여한 권한은 남습니다.
--        반드시 `from public, anon, authenticated` 로 회수해야 합니다.
--
-- =============================================================================
-- 클라이언트 권한 최소화
-- 선행: 01~14 (모든 테이블·함수가 만들어진 뒤 마지막에 실행)
--
-- 재실행 안전합니다. 기본 권한 차단이 함께 적용되므로 이후 테이블·함수를 추가해도
-- 다시 실행할 필요는 없습니다. 새로 추가하는 객체는 권한이 없는 상태로 생성되며,
-- 클라이언트 접근이 필요하면 해당 객체의 SQL 파일에서 명시적으로 grant 하세요.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 1. 앞으로 만들어지는 객체에 클라이언트 권한이 자동 부여되지 않게 한다
-- ---------------------------------------------------------------------------
alter default privileges in schema public revoke all on tables    from anon, authenticated;
alter default privileges in schema public revoke all on functions from anon, authenticated;

-- ---------------------------------------------------------------------------
-- 2. 테이블 — 전부 회수 후 RLS 정책이 실제로 쓰는 것만 되돌려 준다
-- ---------------------------------------------------------------------------
revoke all on all tables in schema public from anon, authenticated;

-- 로그인 전에도 읽어야 하는 테이블
grant select on table public.game_servers  to anon, authenticated;
grant select on table public.remote_config to anon, authenticated;

-- 로그인 사용자 읽기 전용 — 쓰기는 전부 SECURITY DEFINER RPC 경유
grant select on table public.game_items          to authenticated;
grant select on table public.mails               to authenticated;
grant select on table public.purchases           to authenticated;
grant select on table public.ts_protected_fields to authenticated;

-- leaderboard_scores 는 클라이언트 grant 없음 — 접근은 전부 SECURITY DEFINER RPC(ts_leaderboard_*)를 통한다.
-- (생성기도 ts_leaderboard_columns_meta RPC 로 컬럼을 읽으므로 OpenAPI 노출이 필요 없다.)

-- 본인 행 읽기·쓰기 — RLS 정책이 account_id = auth.uid() 로 제한
grant select, insert, update         on table public.display_names to authenticated;
grant select, insert, update         on table public.user_profiles to authenticated;
grant select, insert, update, delete on table public.user_sessions to authenticated;
grant select, insert, update, delete on table public.user_data     to authenticated;

-- 클라이언트 권한을 남기지 않는 테이블(정책 0개 = RLS 가 이미 전면 차단):
--   account_closures, anonymous_recovery_tokens, mail_batches, mail_categories,
--   mail_schedules, user_ban_messages, user_data_logs, withdrawal_delete_queue

-- ---------------------------------------------------------------------------
-- 3. 함수 — 전부 회수 후 SDK 가 실제로 호출하는 RPC 만 되돌려 준다
--    트리거 함수는 트리거가 실행하므로 EXECUTE 권한이 필요 없어 제외 대상이다.
-- ---------------------------------------------------------------------------
do $$
declare r record;
begin
  for r in
    select p.oid::regprocedure as sig
      from pg_proc p join pg_namespace n on n.oid = p.pronamespace
     where n.nspname = 'public' and p.prorettype <> 'trigger'::regtype
  loop
    execute format('revoke all on function %s from public, anon, authenticated', r.sig);
  end loop;
end $$;

-- 로그인 전에도 호출해야 하는 함수
--   auth_user_server_id·ts_default_server_id 는 RLS 정책·컬럼 DEFAULT 평가에 쓰이므로 필수
grant execute on function public.auth_user_server_id()                                       to anon, authenticated;
grant execute on function public.ts_default_server_id()                                      to anon, authenticated;
grant execute on function public.ts_server_now()                                             to anon, authenticated;
grant execute on function public.ts_is_display_name_available(text, uuid)                    to anon, authenticated;
grant execute on function public.ts_anon_recovery_get_refresh_token(text, text)              to anon, authenticated;
grant execute on function public.ts_anon_recovery_delete_by_fingerprint(text, text)          to anon, authenticated;
grant execute on function public.ts_anon_recovery_upsert_refresh_token(text, text, uuid, text) to anon, authenticated;

-- 로그인 사용자 전용 RPC
grant execute on function public.ts_ensure_my_profile(text, text)          to authenticated;
grant execute on function public.ts_ensure_my_row(text, text)              to authenticated;
grant execute on function public.ts_my_server_id()                         to authenticated;
grant execute on function public.ts_my_withdrawal_status()                 to authenticated;
grant execute on function public.ts_request_withdrawal(integer)            to authenticated;
grant execute on function public.ts_transfer_my_server(text, text)         to authenticated;
grant execute on function public.ts_delete_my_anon_recovery_tokens()       to authenticated;
grant execute on function public.ts_view_mail_for_user(uuid)               to authenticated;
grant execute on function public.ts_claim_mail_items(uuid)                 to authenticated;
grant execute on function public.ts_claim_all_mail_items(text)             to authenticated;
grant execute on function public.ts_delete_mail_for_user(uuid)             to authenticated;
grant execute on function public.ts_delete_claimed_mails_for_user(text)    to authenticated;
grant execute on function public.ts_mail_inbox_counts()                    to authenticated;

-- 리더보드 (16_leaderboard.sql)
grant execute on function public.ts_leaderboard_tables()                              to authenticated;
grant execute on function public.ts_leaderboard_table(text)                           to authenticated;
grant execute on function public.ts_leaderboard_submit_score(text, numeric, jsonb)     to authenticated;
grant execute on function public.ts_leaderboard_range(text, int, int, int)            to authenticated;
grant execute on function public.ts_leaderboard_player(text, uuid, int)               to authenticated;
grant execute on function public.ts_leaderboard_set_player_data(text, jsonb, int)     to authenticated;
grant execute on function public.ts_leaderboard_delete_my_score(text, int)            to authenticated;
-- 클래스 생성기 전용(무인증). 등록 컬럼의 이름+타입만 노출(민감정보 아님) → anon 도 허용.
grant execute on function public.ts_leaderboard_columns_meta(text)                    to anon, authenticated;
grant execute on function public.ts_leaderboard_list_meta()                           to anon, authenticated;

-- 채팅
grant execute on function public.ts_chat_send(text, text)                             to authenticated;
grant execute on function public.ts_chat_fetch_many(jsonb, int)                        to authenticated;
grant execute on function public.ts_chat_channels()                                   to authenticated;

-- 클라이언트에 열지 않는 함수(운영·cron·트리거 전용). postgres·service_role 로만 호출합니다:
--   admin_add_user_data_column / admin_drop_user_data_column / admin_update_user_data_column
--   ts_protect_field / ts_unprotect_field  ← SECURITY DEFINER 로 CHECK 제약을 조작한다.
--                                            열려 있으면 플레이어가 재화 최소값 보호를 해제할 수 있다.
--   ts_run_due_mail_schedules / ts_withdrawal_cleanup_batch / ts_cleanup_expired_mails
--   ts_admin_* (우편 발송·카탈로그·리더보드), rls_auto_enable, ts_mail_schedule_next_run
--   ts_leaderboard_rotate_due / ts_leaderboard_next_rotation_at / ts_leaderboard_columns_of
--   ts_admin_leaderboard_*  ← 리더보드 정의·점수 정정·컬럼 DDL. 열리면 클라이언트가
--                             리더보드를 지우거나 점수를 조작할 수 있다.

-- ---------------------------------------------------------------------------
-- 검증
-- ---------------------------------------------------------------------------
-- 1) 남은 테이블 권한 (위 grant 목록과 일치해야 함)
-- select table_name, grantee, string_agg(privilege_type, ',' order by privilege_type) as privs
--   from information_schema.role_table_grants
--  where table_schema = 'public' and grantee in ('anon','authenticated')
--  group by table_name, grantee order by table_name, grantee;
--
-- 2) 클라이언트가 호출 가능한 함수 (위 grant 목록과 일치해야 함)
-- select p.proname, has_function_privilege('anon', p.oid, 'EXECUTE') as anon
--   from pg_proc p join pg_namespace n on n.oid = p.pronamespace
--  where n.nspname = 'public' and p.prorettype <> 'trigger'::regtype
--    and (has_function_privilege('anon', p.oid, 'EXECUTE')
--         or has_function_privilege('authenticated', p.oid, 'EXECUTE'))
--  order by anon desc, p.proname;
--
-- 3) 기본 권한에서 anon·authenticated 가 빠졌는지 (postgres 행에 둘이 없어야 함)
-- select defaclrole::regrole::text as grantor, defaclobjtype, defaclacl::text
--   from pg_default_acl where defaclnamespace::regnamespace::text = 'public';
