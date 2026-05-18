-- 플레이어 공개 프로필(닉네임, 서버 ID, 탈퇴 예약일 등)을 관리합니다.
-- 로그인 시 자동으로 프로필 행을 생성하며, 모든 파생 테이블의 server_id 기준이 됩니다.
--
-- =============================================================================
-- 플레이어 스키마 — profiles + RLS + ensure-profile RPC
-- 선행: 01_game_servers.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- profiles
-- ---------------------------------------------------------------------------
create table if not exists public.user_profiles (
  id uuid primary key default gen_random_uuid(),
  user_id text not null,
  account_id uuid unique references auth.users (id) on delete set null,
  server_id uuid references public.game_servers (id) on delete restrict,
  withdrawn_at timestamptz null,
  last_activity_at timestamptz default now()
);

-- 기존 DB에 컬럼만 없을 때 보강(신규 생성 테이블에서는 IF NOT EXISTS 로 무시됨)
alter table public.user_profiles add column if not exists user_id text;
alter table public.user_profiles add column if not exists account_id uuid;
alter table public.user_profiles add column if not exists server_id uuid;
alter table public.user_profiles add column if not exists withdrawn_at timestamptz;
alter table public.user_profiles add column if not exists last_activity_at timestamptz default now();

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
create or replace function public.ts_ensure_my_profile(p_user_id text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid;
  v_stable text;
  v_server uuid;
begin
  v_uid := auth.uid();
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  v_stable := coalesce(nullif(trim(p_user_id), ''), v_uid::text);
  v_server := public.ts_default_server_id();

  insert into public.user_profiles (user_id, account_id, withdrawn_at, server_id)
  values (v_stable, v_uid, null, v_server)
  on conflict (account_id) do update set
    user_id = excluded.user_id,
    withdrawn_at = excluded.withdrawn_at,
    server_id = coalesce(user_profiles.server_id, excluded.server_id);
end;
$$;

comment on function public.ts_ensure_my_profile(text) is
  '로그인 직후 본인 profiles 행 보장(upsert). SECURITY DEFINER. SDK EnsureMyProfileRowAsync 가 호출.';

grant execute on function public.ts_ensure_my_profile(text) to authenticated;

-- nickname은 auth.user_metadata.displayName으로 이동했으므로 profiles에 두지 않습니다.