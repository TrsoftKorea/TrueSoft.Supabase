-- =============================================================================
-- 플레이어 스키마 — anonymous_recovery_tokens + RPC + auth 트리거
-- 선행: 01_game_servers.sql, 02_profiles.sql
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
grant execute on function public.ts_anon_recovery_upsert_refresh_token(text, text, uuid, text) to anon, authenticated;
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