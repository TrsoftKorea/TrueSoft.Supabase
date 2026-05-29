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
  platform     text null
);

-- 기존 DB에 컬럼만 없을 때 보강(신규 생성 테이블에서는 IF NOT EXISTS 로 무시됨)
alter table public.user_profiles add column if not exists user_id text;
alter table public.user_profiles add column if not exists account_id uuid;
alter table public.user_profiles add column if not exists server_id uuid;
alter table public.user_profiles add column if not exists withdrawn_at timestamptz;
alter table public.user_profiles add column if not exists last_activity_at timestamptz default now();
alter table public.user_profiles add column if not exists country_code text;
alter table public.user_profiles add column if not exists platform text;

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

  insert into public.user_profiles (user_id, account_id, withdrawn_at, server_id, country_code, platform)
  values (v_stable, v_uid, null, v_server, v_country, nullif(trim(coalesce(p_platform, '')), ''))
  on conflict (account_id) do update set
    user_id      = excluded.user_id,
    withdrawn_at = excluded.withdrawn_at,
    server_id    = coalesce(user_profiles.server_id, excluded.server_id),
    country_code = coalesce(user_profiles.country_code, excluded.country_code),
    platform     = excluded.platform;  -- 매 로그인마다 최신 값으로 갱신
end;
$$;

comment on function public.ts_ensure_my_profile(text) is
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

comment on table public.display_names is '닉네임 유니크/공개 조회용. 실제 표시 이름은 auth.user_metadata.displayName이 소스.';
comment on column public.display_names.account_id is 'auth.users.id (RLS: auth.uid()).';
comment on column public.display_names.user_id is '플레이어 안정 id (profiles.user_id와 동일 값).';
comment on column public.display_names.server_id is '표시 이름이 속한 서버 id.';
comment on column public.display_names.display_name is '표시용 닉네임(원문). 유니크 인덱스는 lower(trim(...)) 기준.';

create index if not exists display_names_user_id_idx on public.display_names (user_id);
create index if not exists display_names_server_id_idx on public.display_names (server_id);

alter table public.display_names enable row level security;

drop policy if exists "display_names_select_public" on public.display_names;
drop policy if exists "display_names_insert_own" on public.display_names;
drop policy if exists "display_names_update_own" on public.display_names;

create policy "display_names_select_public"
on public.display_names for select
using (
  auth.uid() is not null
  and server_id = public.auth_user_server_id()
);

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

create unique index if not exists display_names_display_name_unique
on public.display_names (server_id, lower(trim(display_name)))
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

-- ---------------------------------------------------------------------------
-- ts_is_display_name_available
-- RLS를 우회(SECURITY DEFINER)하여 호출자의 서버 내에서 닉네임 사용 가능 여부를 확인합니다.
-- display_names SELECT RLS 에 의존하지 않으므로 RLS 설정과 무관하게 정확한 결과를 반환합니다.
-- p_display_name  : 확인할 닉네임
-- p_ignore_account_id : 본인 이름 수정 시 자신의 account_id를 넘기면 중복에서 제외합니다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_is_display_name_available(
  p_display_name      text,
  p_ignore_account_id uuid default null
)
returns boolean
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_server_id uuid;
begin
  -- 호출자의 server_id를 직접 조회 (auth_user_server_id() 와 동일 로직, RLS 우회)
  select p.server_id into v_server_id
  from public.user_profiles p
  where p.account_id = auth.uid()
    and p.account_id is not null
  limit 1;

  if v_server_id is null then
    return false; -- 프로필 없음 → 설정 불가 상태이므로 불가로 반환
  end if;

  return not exists (
    select 1
    from public.display_names
    where server_id = v_server_id
      and lower(trim(display_name)) = lower(trim(p_display_name))
      and trim(display_name) <> ''
      and (p_ignore_account_id is null or account_id <> p_ignore_account_id)
  );
end;
$$;

comment on function public.ts_is_display_name_available(text, uuid) is
  '닉네임 사용 가능 여부 확인. SECURITY DEFINER로 RLS 우회, 호출자 서버 기준으로 대소문자 무시 비교.';

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

notify pgrst, 'reload schema';
