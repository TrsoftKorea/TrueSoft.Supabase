-- 플레이어의 현재 로그인 세션을 기록합니다.
-- 중복 로그인 감지와 기기별 세션 관리에 사용됩니다.
--
-- =============================================================================
-- 플레이어 스키마 — user_sessions
-- 선행: 02_profiles.sql
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