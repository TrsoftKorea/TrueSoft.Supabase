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
