-- =============================================================================
-- 플레이어 스키마 — display_names
-- 선행: 02_profiles.sql
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

notify pgrst, 'reload schema';