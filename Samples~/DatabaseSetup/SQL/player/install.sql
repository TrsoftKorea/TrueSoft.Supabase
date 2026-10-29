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

-- ---------------------------------------------------------------------------
-- game_settings — 게임 시간대(1행). 운영 콘솔 "설정 › 시간대"에서 바꾼다.
-- 통계의 "하루"(일별 접속·채팅 수·동시접속 일별 요약), 대시보드 "오늘", 탈퇴 삭제 자정, 콘솔 날짜 검색,
-- 새로 만드는 우편 예약·리더보드 회차의 기본 시간대가 전부 이것을 따른다.
-- 날짜별 기록은 쌓을 때 날짜가 정해지므로, 바꾸면 그 뒤 기록부터 새 기준이다(지난 기록·이미 만든 예약은 그대로).
-- ---------------------------------------------------------------------------
create table if not exists public.game_settings (
  id         int primary key default 1 check (id = 1),
  time_zone  text not null default 'Asia/Seoul',
  updated_at timestamptz not null default now(),
  updated_by text
);
insert into public.game_settings (id) values (1) on conflict (id) do nothing;
alter table public.game_settings enable row level security;
-- [의도적] 정책 없음 — 아래 함수만 쓴다.
revoke all on table public.game_settings from anon, authenticated;

-- 예전 판의 탈퇴 전용 시간대(withdrawal_settings)를 옮기고 지운다 — 그 값이 게임 시간대가 됐다.
do $$
begin
  if to_regclass('public.withdrawal_settings') is not null then
    execute $m$
      update public.game_settings g
         set time_zone = w.time_zone, updated_at = w.updated_at, updated_by = w.updated_by
        from public.withdrawal_settings w
       where g.id = 1 and w.id = 1 and g.updated_by is null and w.updated_by is not null
    $m$;
    drop function if exists public.ts_admin_withdrawal_settings_get();
    drop function if exists public.ts_admin_withdrawal_settings_update(text, text);
    drop table public.withdrawal_settings;
  end if;
end $$;

create or replace function public.ts_game_time_zone()
returns text
language sql
stable
security definer
set search_path = public
as $$
  select coalesce((select time_zone from public.game_settings where id = 1), 'Asia/Seoul');
$$;

comment on function public.ts_game_time_zone() is '게임 시간대(game_settings). 날짜를 자르는 모든 곳이 이것을 쓴다.';
revoke all on function public.ts_game_time_zone() from public, anon, authenticated;

-- 게임 시간대의 오늘 날짜.
create or replace function public.ts_game_today()
returns date
language sql
stable
security definer
set search_path = public
as $$
  select (now() at time zone public.ts_game_time_zone())::date;
$$;

revoke all on function public.ts_game_today() from public, anon, authenticated;

-- 콘솔 "설정 › 시간대". example_deadline 은 5절 탈퇴 함수를 부르므로 plpgsql(실행할 때 해석)로 둔다.
create or replace function public.ts_admin_game_settings_get()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
begin
  return (select jsonb_build_object('time_zone', s.time_zone, 'updated_at', s.updated_at, 'updated_by', s.updated_by,
                                    'example_deadline', public.ts_withdrawal_deadline(7))
            from public.game_settings s where s.id = 1);
end;
$$;

create or replace function public.ts_admin_game_settings_update(p_time_zone text, p_by text default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_tz text := btrim(coalesce(p_time_zone, ''));
begin
  if not exists (select 1 from pg_timezone_names where name = v_tz) then
    raise exception 'game_time_zone_invalid: %', v_tz;
  end if;
  update public.game_settings
     set time_zone = v_tz, updated_at = now(), updated_by = p_by
   where id = 1;
  return public.ts_admin_game_settings_get();
end;
$$;

comment on function public.ts_admin_game_settings_get() is '게임 시간대 조회. service_role 전용.';
comment on function public.ts_admin_game_settings_update(text, text) is '게임 시간대 변경. 지난 날짜별 기록·예약된 탈퇴·이미 만든 예약은 그대로다. service_role 전용.';
revoke all on function public.ts_admin_game_settings_get() from public, anon, authenticated;
revoke all on function public.ts_admin_game_settings_update(text, text) from public, anon, authenticated;
grant execute on function public.ts_admin_game_settings_get() to service_role;
grant execute on function public.ts_admin_game_settings_update(text, text) to service_role;


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
  -- account_id·server_id 의 unique·FK 는 아래 가드 블록이 profiles_* 이름으로 붙인다.
  -- 여기서 인라인으로 걸면 가드가 이름으로 못 찾아 신규 설치에서 같은 제약이 두 벌 생긴다.
  account_id uuid,
  server_id uuid,
  withdrawn_at timestamptz null,
  last_activity_at timestamptz default now(),
  country_code text null,
  platform     text null,
  signup_platform text null,
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
alter table public.user_profiles add column if not exists signup_platform text;
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
comment on column public.user_profiles.signup_platform is '계정을 만든 첫 로그인의 플랫폼. 한 번 적으면 바뀌지 않는다. 이 칸이 생기기 전 계정은 남은 흔적으로 추정해 채웠다(7절). null = 플랫폼을 보내지 않은 로그인으로 만든 계정.';
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

-- 플레이어는 이 표에 직접 쓰지 않는다 — 만들기·고치기는 전부 SECURITY DEFINER 함수(ts_ensure_my_profile·
-- ts_after_sign_in·서버 이동·탈퇴)가 한다. 직접 쓰기를 열어 두면 가입 플랫폼·누적 결제액·서버·탈퇴 시각을
-- 플레이어가 마음대로 바꿀 수 있었다(2026-10-29). 위 drop 이 옛 insert·update 정책을 지운다.
-- 읽기도 게임·엣지 함수·다른 표의 정책이 실제로 쓰는 칸만 연다(누적 결제액·가입 플랫폼은 남에게 안 보인다):
--   SDK 공개 프로필 id·user_id·withdrawn_at(+거르기 account_id) · 엣지 함수 user_id·server_id·withdrawn_at ·
--   mails 읽기 정책 account_id·user_id·server_id · ts_my_withdrawal_status(호출자 권한) account_id·withdrawn_at
-- 칸을 새로 읽게 하면 여기와 23절 grant 에 함께 더한다 — 빠지면 그 요청이 권한 오류(42501)로 실패한다.
revoke all on public.user_profiles from anon, authenticated;
grant select (id, user_id, account_id, server_id, withdrawn_at) on public.user_profiles to authenticated;

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

  -- signup_platform 은 여기서 처음 만들 때만 적는다 — 아래 갱신 목록에 넣지 않는다(옛 계정에 지금 기기를 적지 않게).
  insert into public.user_profiles (user_id, account_id, withdrawn_at, server_id, country_code, platform, signup_platform, last_activity_at)
  values (v_stable, v_uid, null, v_server, v_country, nullif(trim(coalesce(p_platform, '')), ''),
          nullif(trim(coalesce(p_platform, '')), ''), now())
  -- withdrawn_at 은 갱신하지 않는다. 예전엔 null 로 지웠는데, SDK 의 탈퇴 확인이 통신 실패로 통과된 로그인이
  -- 여기 오면 이용자 모르게 탈퇴 예약이 사라졌다. 예약 취소는 취소 토큰(ts_withdrawal_cancel_redeem)으로만 한다.
  on conflict (account_id) do update set
    user_id          = excluded.user_id,
    server_id        = coalesce(user_profiles.server_id, excluded.server_id),
    country_code     = coalesce(user_profiles.country_code, excluded.country_code),
    platform         = excluded.platform,
    last_activity_at = now();  -- 로그인 시각도 활동으로 기록(데이터 저장 트리거와 별개)

  -- 게스트가 구글·애플을 연동하면 user_id 가 그 계정 번호로 바뀐다. 우편 보기·받기는 user_id 로 묶여 있어
  -- 함께 옮기지 않으면 연동 전에 받아 둔 우편이 안 보이고 받을 수도 없다(기간 우편은 같은 우편을 한 통 더 받는다).
  update public.mails set user_id = v_stable
   where account_id = v_uid and user_id is distinct from v_stable;
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

  -- 이름 충돌·금칙어(구글·애플 계정 이름에 섞인 경우) 시 account_id 기반 기본값으로 폴백
  if exists (
    select 1 from public.display_names d
    where lower(trim(d.display_name)) = lower(trim(v_name))
  ) or public.ts_display_name_forbidden(v_name) then
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
-- 금칙어 — 채팅은 가려서 보내고, 닉네임은 거절한다
-- ---------------------------------------------------------------------------
-- kind = 'word' : 금칙어. 채팅에 들어 있으면 그 부분을 * 로 가리고, 닉네임에 들어 있으면 거절한다.
-- kind = 'name' : 금지 닉네임. 닉네임 전체가 이것과 같을 때만 거절한다("운영자"·"GM" 등).
-- 비교는 소문자로 바꾸고 공백·기호를 뺀 글자로 한다 — "시 발"·"시.발" 같은 우회를 잡는다.
-- 그래서 word 는 그 꼴로 정규화해 저장한다(ts_filter_normalize). 운영자가 콘솔에서 관리한다(16절 운영 기록에 남는다).
-- 이미 쓰고 있는 닉네임은 소급하지 않는다 — 새로 정하거나 바꿀 때만 본다. 운영자의 닉네임 변경은 검사하지 않는다.
create table if not exists public.banned_words (
  kind       text        not null check (kind in ('word', 'name')),
  word       text        not null check (word <> ''),
  created_at timestamptz not null default now(),
  created_by text,
  primary key (kind, word)
);

alter table public.banned_words enable row level security;
-- [의도적] 정책 없음 — 클라이언트는 목록을 읽지 못한다(우회 단어를 찾는 데 쓰인다). 검사는 아래 함수가 서버에서 한다.
revoke all on table public.banned_words from anon, authenticated;

comment on table public.banned_words is
  '금칙어(word)·금지 닉네임(name). word 는 ts_filter_normalize 를 거친 값으로 저장한다.';

create or replace function public.ts_filter_normalize(p_text text)
returns text
language sql
immutable
set search_path = public
as $$
  select regexp_replace(lower(coalesce(p_text, '')), '[[:space:][:punct:]]+', '', 'g');
$$;

-- 금칙어가 걸린 글자를 * 로 바꾼다. 공백·기호를 건너뛰고 찾으므로 "시 발" 은 "* *" 가 된다(공백은 남긴다).
create or replace function public.ts_mask_banned_words(p_text text)
returns text
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_chars text[];
  v_map   int[] := '{}';
  v_norm  text  := '';
  v_mask  boolean[];
  v_word  text;
  v_from  int;
  v_pos   int;
  n       int;
  i       int;
begin
  if p_text is null or p_text = '' then
    return p_text;
  end if;
  if not exists (select 1 from public.banned_words where kind = 'word') then
    return p_text;
  end if;

  v_chars := regexp_split_to_array(p_text, '');
  n := array_length(v_chars, 1);
  -- 공백·기호를 뺀 글자열과, 그 글자가 원문 몇 번째였는지를 함께 만든다.
  for i in 1 .. n loop
    if v_chars[i] !~ '^[[:space:][:punct:]]$' then
      v_norm := v_norm || lower(v_chars[i]);
      v_map  := v_map || i;
    end if;
  end loop;
  v_mask := array_fill(false, array[n]);

  for v_word in select b.word from public.banned_words b where b.kind = 'word' loop
    v_from := 1;
    loop
      v_pos := strpos(substr(v_norm, v_from), v_word);
      exit when v_pos = 0;
      v_pos := v_from + v_pos - 1;
      for i in v_map[v_pos] .. v_map[v_pos + char_length(v_word) - 1] loop
        v_mask[i] := true;
      end loop;
      v_from := v_pos + 1;
    end loop;
  end loop;

  if not (true = any (v_mask)) then
    return p_text;
  end if;
  for i in 1 .. n loop
    if v_mask[i] and v_chars[i] !~ '^[[:space:]]$' then
      v_chars[i] := '*';
    end if;
  end loop;
  return array_to_string(v_chars, '');
end;
$$;

create or replace function public.ts_display_name_forbidden(p_display_name text)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
      from public.banned_words b
     where (b.kind = 'word' and strpos(public.ts_filter_normalize(p_display_name), b.word) > 0)
        or (b.kind = 'name' and b.word = public.ts_filter_normalize(p_display_name))
  );
$$;

comment on function public.ts_mask_banned_words(text) is '채팅 본문의 금칙어를 * 로 가린다. 채팅 발송 RPC 가 내부에서 부른다.';
comment on function public.ts_display_name_forbidden(text) is '닉네임이 금칙어를 포함하거나 금지 닉네임과 같은지.';

revoke all on function public.ts_filter_normalize(text)       from public, anon, authenticated;
revoke all on function public.ts_mask_banned_words(text)      from public, anon, authenticated;
revoke all on function public.ts_display_name_forbidden(text) from public, anon, authenticated;
-- 닉네임 금칙어 검사는 아래 표 트리거가 한다. displayname-set Edge Function 은 유저 토큰으로 저장하므로
-- 트리거에 걸린다 — 저장을 secret key 로 바꾸면 트리거가 통과시켜 검사가 빠지니 주의.
grant execute on function public.ts_display_name_forbidden(text) to service_role;

-- 닉네임 표에서 직접 막는다 — 유저는 RLS 로 display_names 에 직접 쓸 수 있어 Edge Function 검사만으로는 우회된다.
-- 유저 요청(JWT role authenticated·anon)일 때만 본다. 운영 콘솔(service_role)·SQL 편집기는 통과시킨다.
create or replace function public.ts_display_names_block_forbidden()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if coalesce(auth.role(), '') in ('authenticated', 'anon')
     and (tg_op = 'INSERT' or new.display_name is distinct from old.display_name)
     and public.ts_display_name_forbidden(new.display_name) then
    raise exception 'display_name_forbidden';
  end if;
  return new;
end;
$$;

revoke all on function public.ts_display_names_block_forbidden() from public, anon, authenticated;

drop trigger if exists trg_display_names_block_forbidden on public.display_names;
create trigger trg_display_names_block_forbidden
  before insert or update of display_name on public.display_names
  for each row execute function public.ts_display_names_block_forbidden();

-- 운영 콘솔 전용 — 목록·추가·삭제
create or replace function public.ts_admin_banned_words_list()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'kind', b.kind, 'word', b.word, 'created_at', b.created_at, 'created_by', b.created_by
         ) order by b.kind, b.word), '[]'::jsonb)
    from public.banned_words b;
$$;

-- 정규화해서 비는 값(공백·기호뿐)은 버린다. 이미 있는 단어는 건너뛴다. 새로 들어간 개수를 돌려준다.
create or replace function public.ts_admin_banned_words_add(p_kind text, p_words text[], p_by text default null)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  v_count int;
begin
  if p_kind not in ('word', 'name') then
    raise exception 'banned_word_kind_invalid';
  end if;
  insert into public.banned_words (kind, word, created_by)
  select distinct p_kind, public.ts_filter_normalize(w), p_by
    from unnest(coalesce(p_words, '{}'::text[])) w
   where public.ts_filter_normalize(w) <> ''
  on conflict (kind, word) do nothing;
  get diagnostics v_count = row_count;
  return v_count;
end;
$$;

create or replace function public.ts_admin_banned_words_delete(p_kind text, p_words text[])
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  v_count int;
begin
  delete from public.banned_words
   where kind = p_kind
     and word = any (coalesce(p_words, '{}'::text[]));
  get diagnostics v_count = row_count;
  return v_count;
end;
$$;

revoke all on function public.ts_admin_banned_words_list()                  from public, anon, authenticated;
revoke all on function public.ts_admin_banned_words_add(text, text[], text) from public, anon, authenticated;
revoke all on function public.ts_admin_banned_words_delete(text, text[])    from public, anon, authenticated;
grant execute on function public.ts_admin_banned_words_list()                  to service_role;
grant execute on function public.ts_admin_banned_words_add(text, text[], text) to service_role;
grant execute on function public.ts_admin_banned_words_delete(text, text[])    to service_role;

-- 기본 금칙어 — 표가 비어 있을 때만 넣는다(운영자가 지운 단어가 이 파일을 다시 돌려도 되살아나지 않게).
-- 출처: LDNOOBW "List of Dirty, Naughty, Obscene, and Otherwise Bad Words" ko·en
--       https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words (CC BY 4.0)
-- 검사가 "공백·기호를 뺀 글자 안에 들어 있으면" 걸리므로, 멀쩡한 말을 가리는 단어는 뺐다
-- (예: ass→class, butt→button, raping→scraping, smut→"is mutual", 보지→"보지 마", 미친→"이미 친구", 염병→전염병,
-- hardcore·domination 같은 게임 용어). 영어는 37만 단어 사전과 흔한 단어 두세 개를 붙인 꼴로 대조해 걸렀다.
do $$
begin
  if not exists (select 1 from public.banned_words) then
    insert into public.banned_words (kind, word, created_by)
    select 'word', public.ts_filter_normalize(w), '기본 목록'
      from unnest(array[
    '2g1c', '2girls1cup', 'acrotomophilia', 'alabamahotpocket', 'alaskanpipeline', 'anilingus', 'apeshit', 'arsehole', 'asshole', 'assmunch',
    'autoerotic', 'babeland', 'babybatter', 'babyjuice', 'ballgag', 'ballgravy', 'ballkicking', 'balllicking', 'ballsack', 'ballsucking',
    'bangbros', 'bangbus', 'barelylegal', 'bastard', 'bastardo', 'bbw', 'bdsm', 'beaner', 'beaners', 'beastiality',
    'beavercleaver', 'beaverlips', 'bestiality', 'bigbreasts', 'bigknockers', 'bigtits', 'bimbos', 'bitch', 'bitches', 'blackcock',
    'blondeaction', 'blondeonblondeaction', 'blowjob', 'blowyourload', 'bluewaffle', 'blumpkin', 'bollocks', 'boner', 'boob', 'boobs',
    'bootycall', 'brownshowers', 'brunetteaction', 'bukkake', 'bulldyke', 'bulletvibe', 'bullshit', 'bunghole', 'busty', 'buttcheeks',
    'butthole', 'cameltoe', 'camgirl', 'camslut', 'camwhore', 'carpetmuncher', 'chocolaterosebuds', 'circlejerk', 'clevelandsteamer', 'clit',
    'clitoris', 'cloverclamps', 'clusterfuck','coprolagnia', 'coprophilia', 'cornhole', 'creampie', 'cumming', 'cumshot',
    'cumshots', 'cunnilingus', 'cunt', 'darkie', 'daterape', 'deepthroat', 'dendrophilia','dildo', 'dingleberries',
    'dingleberry', 'dirtypillows', 'dirtysanchez', 'doggiestyle', 'doggystyle', 'dogstyle', 'dolcett', 'dominatrix', 'dommes', 'donkeypunch',
    'doubledong', 'doublepenetration', 'dpaction', 'dryhump', 'dvda', 'eatmyass', 'ecchi', 'ejaculation','erotism',
    'faggot', 'felch', 'fellatio', 'feltch', 'femalesquirting', 'femdom', 'figging', 'fingerbang', 'fisting', 'footfetish',
    'footjob', 'frotting', 'fuck', 'fuckbuttons', 'fuckin', 'fucking', 'fucktards', 'fudgepacker', 'futanari', 'gangbang',
    'gaysex', 'giantcock', 'girlontop', 'girlsgonewild', 'goatcx', 'goatse', 'goddamn', 'gokkun', 'goldenshower', 'googirl',
    'goregasm', 'gspot','handjob', 'hentai', 'homoerotic', 'honkey', 'hotcarl', 'hugefat',
    'jackoff', 'jailbait', 'jerkoff', 'jigaboo', 'jiggaboo', 'jiggerboo', 'jizz', 'juggs', 'kike', 'kinbaku',
    'kinkster', 'knobbing', 'leatherrestraint', 'leatherstraightjacket', 'lemonparty', 'livesex', 'makemecome', 'malesquirting', 'masturbate', 'masturbating',
    'masturbation', 'milf', 'missionaryposition', 'motherfucker', 'moundofvenus', 'mrhands', 'muffdiver', 'muffdiving', 'nambla', 'nawashi',
    'neonazi', 'nigga', 'nigger', 'nignog', 'nimphomania', 'nipple', 'nipples', 'nsfw', 'nsfwimages', 'nude',
    'nudity', 'nutten', 'nympho', 'nymphomania', 'omorashi', 'onecuptwogirls', 'oneguyonejar', 'orgasm','paedophile',
    'panties', 'pedobear', 'pedophile', 'pegging', 'phonesex', 'pieceofshit', 'pissing', 'pisspig', 'pleasurechest', 'polesmoker',
    'ponyplay', 'poontang', 'poopchute', 'porn', 'porno', 'pornography', 'princealbertpiercing', 'pthc', 'pubes', 'punany',
    'pussy', 'queaf', 'queef','raghead', 'ragingboner','reversecowgirl', 'rimjob',
    'rosypalmandher5sisters', 'rustytrombone', 'schlong', 'sexcam', 'sexo', 'shavedbeaver', 'shavedpussy', 'shemale', 'shibari', 'shitblimp',
    'shitty','slanteye', 'slut','sodomize', 'sodomy', 'splooge', 'sploogemoose', 'spooge',
    'spreadlegs','strapon', 'stripclub', 'styledoggy', 'suicidegirls', 'swastika', 'teabagging', 'throating', 'thumbzilla',
    'titties', 'titty', 'tosser', 'towelhead', 'tranny', 'tribadism', 'tubgirl', 'tushy', 'twogirlsonecup', 'upskirt',
    'urethraplay', 'urophilia', 'vagina', 'venusmound', 'viagra', 'vibrator', 'violetwand', 'vorarephilia', 'voyeur', 'voyeurweb',
    'voyuer', 'vulva','wetback', 'wetdream', 'worldsex', 'wrinkledstarfish', 'xxx', 'yaoi', 'yellowshowers',
    'yiffy', 'zoophilia', '개새끼', '개자식', '개좆', '개차반', '계집년', '니기미', '딸딸이', '때씹',
    '또라이', '뙤놈', '로리타', '몰카', '미친새끼', '바바리맨', '병신', '빠구리', '사까시', '섹스',
    '스와핑', '쌍놈', '씨발', '씨발놈', '씨팔', '씹물', '씹빨', '씹새끼', '씹알', '씹창',
    '씹팔', '암캐', '야애니', '엄창', '옘병', '육갑', '은꼴', '잡년', '종간나', '좆',
    '좆만', '죽일년', '쥐좆', '직촬', '짱깨', '쪽바리', '창녀', '포르노', '화냥년', '후레아들',
    '희쭈그리'
      ]) as w
    on conflict (kind, word) do nothing;
  end if;
end $$;

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

-- ts_display_name_check — 'available' · 'taken' · 'forbidden'.
-- 금지 닉네임을 중복과 구분해 알려준다. SDK 의 닉네임 확인이 이것을 쓴다(옛 SDK 는 위 함수를 계속 쓴다).
create or replace function public.ts_display_name_check(
  p_display_name      text,
  p_ignore_account_id uuid default null
)
returns text
language sql
stable
security definer
set search_path = public
as $$
  select case
           when public.ts_display_name_forbidden(p_display_name) then 'forbidden'
           when public.ts_is_display_name_available(p_display_name, p_ignore_account_id) then 'available'
           else 'taken'
         end;
$$;

grant execute on function public.ts_display_name_check(text, uuid) to authenticated;


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

-- 동시접속자 집계용 생존 신호. SDK 의 중복 로그인 확인이 주기마다 ts_session_heartbeat 로 찍는다.
alter table public.user_sessions add column if not exists last_seen_at      timestamptz;
alter table public.user_sessions add column if not exists heartbeat_seconds integer;
comment on column public.user_sessions.last_seen_at is '마지막 생존 신호 시각. 동시접속자 집계(ts_ccu_snapshot)가 본다.';
comment on column public.user_sessions.heartbeat_seconds is '그 기기의 생존 신호 주기(초). 게임마다 달라 접속 중 판정 창을 이것으로 잡는다.';
create index if not exists user_sessions_last_seen_at_idx on public.user_sessions (last_seen_at);
create index if not exists user_sessions_updated_at_idx on public.user_sessions (updated_at);

-- 접속 중 판정. 신호는 2분에 한 번만 기록되므로(아래 heartbeat) 기록은 최대 "2분 + 신호 주기" 묵어 있다.
-- 그보다 넉넉히, 최소 5분으로 잡는다 — 요청 하나가 늦는 일로 숫자가 출렁이지 않게. 대신 앱을 끈 사람은 2~5분 뒤에 빠진다.
-- 로그인 직후 첫 신호 전에는 로그인 때 찍힌 updated_at 을 쓴다.
-- 미래 시각은 버린다 — 아래 트리거 전에 기기 시계로 찍힌 옛 행이 앱을 꺼도 접속 중으로 남지 않게.
create or replace function public.ts_session_is_online(p_last_seen timestamptz, p_updated timestamptz, p_heartbeat integer)
returns boolean
language sql
stable
as $$
  select greatest(case when p_last_seen <= now() + interval '1 minute' then p_last_seen end,
                  case when p_updated   <= now() + interval '1 minute' then p_updated end)
         >= now() - make_interval(secs => greatest(300, coalesce(p_heartbeat, 60) * 2 + 150));
$$;

-- 게임이 표에 직접 쓸 때(로그인 시 토큰 저장): 시각은 기기 시계가 아니라 서버 시각으로, 생존 신호 칸은 못 바꾼다.
-- 생존 신호는 ts_session_heartbeat(security definer — current_user 가 소유자)만 쓴다.
create or replace function public.ts_user_sessions_guard()
returns trigger
language plpgsql
set search_path = public
as $$
begin
  if current_user in ('authenticated', 'anon') then
    new.updated_at := now();
    if tg_op = 'INSERT' then
      new.last_seen_at := null;
      new.heartbeat_seconds := null;
    else
      new.last_seen_at := old.last_seen_at;
      new.heartbeat_seconds := old.heartbeat_seconds;
    end if;
  end if;
  return new;
end;
$$;
revoke all on function public.ts_user_sessions_guard() from public, anon, authenticated;

drop trigger if exists trg_user_sessions_guard on public.user_sessions;
create trigger trg_user_sessions_guard
before insert or update on public.user_sessions
for each row execute function public.ts_user_sessions_guard();
revoke all on function public.ts_session_is_online(timestamptz, timestamptz, integer) from public, anon, authenticated;

-- 일별 접속 기록 — 계정마다 하루(게임 시간대 날짜) 한 줄. 일일 사용자·복귀 유저·리텐션을 여기서 센다. 지우지 않는다.
-- 계정이 지워져도 기록은 남긴다(외래 키 없음) — 지난 리텐션 숫자가 탈퇴로 바뀌면 안 된다.
-- is_new: 그날 가입한 계정. is_comeback: 직전 14일 동안 한 번도 접속하지 않았다가 들어온 계정.
create table if not exists public.user_daily_activity (
  day         date        not null,
  account_id  uuid        not null,
  signup_day  date,
  is_new      boolean     not null default false,
  is_comeback boolean     not null default false,
  server_id   uuid,
  platform    text,
  first_at    timestamptz not null default now(),
  primary key (day, account_id)
);
create index if not exists user_daily_activity_account_idx on public.user_daily_activity (account_id, day desc);
create index if not exists user_daily_activity_signup_idx on public.user_daily_activity (signup_day) where is_new;
alter table public.user_daily_activity enable row level security;
-- [의도적] 정책 없음 — 운영 콘솔이 ts_admin_activity_stats 로만 읽는다.
revoke all on table public.user_daily_activity from anon, authenticated;

-- 가입 플랫폼(user_profiles.signup_platform)을 줄마다 같이 둔다 — 계정이 탈퇴로 지워져 프로필이 없어도 지난 통계를
-- 가입 플랫폼별로 나눠 볼 수 있게. 통계 › 접속의 "가입 플랫폼" 거르기가 이 칸을 본다.
alter table public.user_daily_activity add column if not exists signup_platform text;

-- 이 칸이 생기기 전 계정: 가입한 날의 접속 기록이 있으면 그날 플랫폼이 가입 플랫폼이다(정확함). 그래도 남는 계정은
-- 7절(결제 표가 생긴 뒤)에서 남은 흔적으로 추정해 채운다.
update public.user_profiles p
   set signup_platform = a.platform
  from (select distinct on (account_id) account_id, platform
          from public.user_daily_activity
         where is_new and platform is not null
         order by account_id, day) a
 where p.account_id = a.account_id and p.signup_platform is null;
update public.user_daily_activity a
   set signup_platform = p.signup_platform
  from public.user_profiles p
 where p.account_id = a.account_id and a.signup_platform is null and p.signup_platform is not null;

-- 오늘 첫 접속이면 한 줄 남긴다. 이미 있으면 아무것도 하지 않는다(로그인·접속 신호가 여러 번 불러도 하루 한 줄).
-- 직전 접속일은 이 표에서 찾고, 표에 기록이 없던 시절의 계정은 프로필의 마지막 활동 시각으로 대신한다
-- — 그래서 ts_after_sign_in 은 프로필을 갱신하기 전에 이것부터 부른다.
create or replace function public.ts_record_daily_activity(p_account_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_day     date := (now() at time zone public.ts_game_time_zone())::date;
  v_signup  date;
  v_prev    date;
  v_profile public.user_profiles%rowtype;
begin
  if p_account_id is null then return; end if;
  if exists (select 1 from public.user_daily_activity where day = v_day and account_id = p_account_id) then
    return;
  end if;

  select (u.created_at at time zone public.ts_game_time_zone())::date into v_signup from auth.users u where u.id = p_account_id;
  select * into v_profile from public.user_profiles p where p.account_id = p_account_id;
  select max(day) into v_prev from public.user_daily_activity where account_id = p_account_id and day < v_day;
  if v_prev is null and v_profile.last_activity_at is not null then
    v_prev := (v_profile.last_activity_at at time zone public.ts_game_time_zone())::date;
    -- 프로필 시각이 이미 오늘이면(오늘 저장이 먼저 있었으면) 그 전 접속일을 알 수 없다 — 복귀로 세지 않는다.
    if v_prev >= v_day then v_prev := null; end if;
  end if;

  insert into public.user_daily_activity (day, account_id, signup_day, is_new, is_comeback, server_id, platform, signup_platform)
  values (v_day, p_account_id, v_signup, v_signup = v_day,
          v_signup is distinct from v_day and v_prev is not null and v_prev <= v_day - 15,
          v_profile.server_id, nullif(v_profile.platform, ''), nullif(v_profile.signup_platform, ''))
  on conflict (day, account_id) do nothing;
end;
$$;

comment on function public.ts_record_daily_activity(uuid) is
  '그 계정의 오늘(게임 시간대 날짜) 첫 접속을 일별 접속 기록에 남긴다. 로그인(ts_after_sign_in)·접속 신호(ts_session_heartbeat)가 부른다.';
revoke all on function public.ts_record_daily_activity(uuid) from public, anon, authenticated;

-- 생존 신호 + 현재 세션 토큰 조회를 요청 하나로. 행이 없으면(로그아웃 직후 등) null.
-- 집계는 5분 단위라 매 신호마다 쓸 필요가 없다 — 2분이 지났을 때만 쓰고 나머지는 읽기만 한다(쓰기·트리거 부담을 줄인다).
-- 날짜가 바뀐 뒤 첫 기록이면 일별 접속도 남긴다 — 로그인 없이 자정을 넘겨 계속 켜 둔 사람을 다음 날 사용자로 센다.
create or replace function public.ts_session_heartbeat(p_interval_seconds integer)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid      uuid := auth.uid();
  v_token    uuid;
  v_last     timestamptz;
  v_interval integer := least(600, greatest(5, coalesce(p_interval_seconds, 60)));
begin
  select session_token, last_seen_at into v_token, v_last from public.user_sessions where account_id = v_uid;
  if v_token is null then return null; end if;

  update public.user_sessions
     set last_seen_at = now(), heartbeat_seconds = v_interval
   where account_id = v_uid
     and (last_seen_at is null
          or last_seen_at < now() - interval '2 minutes'
          or heartbeat_seconds is distinct from v_interval);

  if v_last is null or (v_last at time zone public.ts_game_time_zone())::date < (now() at time zone public.ts_game_time_zone())::date then
    perform public.ts_record_daily_activity(v_uid);
  end if;

  return v_token;
end;
$$;

comment on function public.ts_session_heartbeat(integer) is
  '접속 중 신호를 남기고 이 계정의 현재 세션 토큰을 돌려준다(중복 로그인 확인 겸용). SDK 가 주기마다 부른다.';
revoke all on function public.ts_session_heartbeat(integer) from public, anon, authenticated;
grant execute on function public.ts_session_heartbeat(integer) to authenticated;

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

-- server_id FK 는 위 create table 인라인 정의라 기존 DB에는 붙지 않는다. 여기서 보강한다.
do $$
begin
  if not exists (
    select 1
    from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'anonymous_recovery_tokens'
      and c.conname = 'anonymous_recovery_tokens_server_id_fkey'
  ) then
    alter table public.anonymous_recovery_tokens
      add constraint anonymous_recovery_tokens_server_id_fkey
      foreign key (server_id) references public.game_servers (id) on delete restrict;
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
-- 탈퇴 취소 토큰 서명 비밀값 — withdrawal-cancel-issue·redeem 이 HMAC 서명에 쓴다.
-- 설치 때 무작위로 만들어 Vault 에 둔다 — 새 게임은 Supabase Secrets 에 따로 넣지 않는다.
-- 엣지 함수는 Secrets 의 CANCEL_TOKEN_SECRET 을 먼저 쓰고(이미 넣어 둔 게임은 그 값 그대로 — 바꾸면 발급된
-- 토큰이 무효가 된다) 없을 때만 이 값을 쓴다. 이미 있으면 다시 만들지 않는다.
-- ---------------------------------------------------------------------------
do $$
begin
  if not exists (select 1 from vault.secrets where name = 'withdrawal_cancel_secret') then
    perform vault.create_secret(encode(extensions.gen_random_bytes(32), 'hex'), 'withdrawal_cancel_secret',
      '탈퇴 취소 토큰 서명용. withdrawal-cancel-issue·redeem 전용.');
  end if;
end $$;

create or replace function public.ts_withdrawal_cancel_secret()
returns text
language sql
stable
security definer
set search_path = public
as $$
  select decrypted_secret from vault.decrypted_secrets where name = 'withdrawal_cancel_secret';
$$;

comment on function public.ts_withdrawal_cancel_secret() is
  '탈퇴 취소 토큰 서명 비밀값(복호화). 엣지 함수 전용 — service_role 만.';

revoke all on function public.ts_withdrawal_cancel_secret() from public, anon, authenticated;
grant execute on function public.ts_withdrawal_cancel_secret() to service_role;

-- ---------------------------------------------------------------------------
-- 탈퇴 삭제 시각 — 게임 시간대(1절 game_settings)의 자정에 맞춘다.
-- 9절 정리 작업이 자정 직후에 지우므로, 시각이 하루 중 아무 때나면
-- 그 사이에 로그인한 사람이 로그인 중에 계정이 지워져 새로 만들어지는 길을 탄다.
-- ---------------------------------------------------------------------------
-- 유예 일수 → 삭제 시각. 신청한 날(게임 시간대)로부터 N일이 지난 다음 자정이라 최소 N일이 보장된다.
-- 예: 10/27 15:00 에 7일 → 11/4 00:00. 유예는 최소 1일 — 바로 지우는 건 즉시 탈퇴(withdrawal-now)가 한다.
create or replace function public.ts_withdrawal_deadline(p_delay_days integer)
returns timestamptz
language sql
stable
security definer
set search_path = public
as $$
  select ((date_trunc('day', now() at time zone tz)
           + make_interval(days => greatest(1, coalesce(p_delay_days, 1)) + 1)) at time zone tz)
  from (select public.ts_game_time_zone() as tz) z;
$$;

revoke all on function public.ts_withdrawal_deadline(integer) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_request_withdrawal — 탈퇴 예약
-- ---------------------------------------------------------------------------
-- 삭제 시각(user_profiles.withdrawn_at)은 ts_withdrawal_deadline — 유예 N일 뒤 기준 시간대 자정.
-- 탈퇴 예약 시 익명 복구 토큰도 함께 삭제합니다.
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

  v_delay_days  := greatest(1, coalesce(p_delay_days, 1));
  v_account_id  := auth.uid();
  v_user_id     := auth.uid()::text;

  v_scheduled_at := public.ts_withdrawal_deadline(v_delay_days);

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
  '탈퇴 예약. 유예 p_delay_days일(최소 1) 뒤 기준 시간대 자정에 삭제. 익명 복구 토큰도 함께 삭제.';

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

-- ---------------------------------------------------------------------------
-- ts_after_sign_in — 로그인 직후 단계(탈퇴 예약 확인·프로필 보장·내 프로필·닉네임·서버 코드)를 한 번에
-- ---------------------------------------------------------------------------
-- 이 단계들을 따로 부르면 앞 단계 답을 기다리는 왕복이 서너 번 쌓이고, 서버가 쉬다 깨면 왕복마다 첫 연결
-- 지연이 붙어 로그인이 10초를 넘긴다. 탈퇴 예약(withdrawn_at)이 있으면 프로필을 건드리지 않고 그 값만
-- 돌려준다 — 그런 계정은 SDK 가 탈퇴 가드(유예 만료 삭제)·예약 게이트(로그인 차단)를 태워야 한다.
create or replace function public.ts_after_sign_in(
  p_user_id  text default null,
  p_platform text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid       uuid := auth.uid();
  v_withdrawn timestamptz;
  v_profile   public.user_profiles%rowtype;
  v_code      text;
  v_name      text;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select p.withdrawn_at into v_withdrawn from public.user_profiles p where p.account_id = v_uid;
  if v_withdrawn is not null then
    return jsonb_build_object('withdrawn_at', v_withdrawn, 'profile', null);
  end if;

  -- 프로필이 마지막 활동 시각을 오늘로 덮기 전에 남겨야 복귀 여부를 판단할 수 있다.
  perform public.ts_record_daily_activity(v_uid);
  perform public.ts_ensure_my_profile(p_user_id, p_platform);

  select * into v_profile from public.user_profiles p where p.account_id = v_uid;
  -- 첫 로그인이면 기록할 때 프로필이 없어 서버·플랫폼이 비었다 — 방금 만든 프로필로 채운다.
  update public.user_daily_activity a
     set server_id = v_profile.server_id, platform = nullif(v_profile.platform, ''),
         signup_platform = coalesce(a.signup_platform, nullif(v_profile.signup_platform, ''))
   where a.day = (now() at time zone public.ts_game_time_zone())::date and a.account_id = v_uid and a.platform is null;

  select gs.server_code into v_code from public.game_servers gs where gs.id = v_profile.server_id;
  select dn.display_name into v_name from public.display_names dn where dn.account_id = v_uid;

  return jsonb_build_object(
    'withdrawn_at', null,
    'profile', jsonb_build_object(
      'id',           v_profile.id,
      'user_id',      v_profile.user_id,
      'display_name', coalesce(v_name, ''),
      'server_code',  coalesce(v_code, '')));
end;
$$;

comment on function public.ts_after_sign_in(text, text) is
  '로그인 직후 탈퇴 예약 확인 + 본인 프로필 보장 + 프로필·닉네임·서버 코드 조회를 한 번에. 탈퇴 예약이 있으면 withdrawn_at 만 반환(프로필 미변경). SECURITY DEFINER.';

revoke all on function public.ts_after_sign_in(text, text) from public, anon;
grant execute on function public.ts_after_sign_in(text, text) to authenticated;

-- ---------------------------------------------------------------------------
-- apple_auth_tokens — Sign in with Apple 철회용 refresh token (클라이언트 직접 접근 없음)
-- ---------------------------------------------------------------------------
-- 애플은 계정을 지울 때 Sign in with Apple 토큰을 REST API 로 철회하라고 요구한다(심사 기준).
-- 철회에는 refresh token 이 필요한데 로그인 결과(ID 토큰)에는 들어 있지 않아, 로그인 때 받은
-- 일회용 authorization code 를 apple-token Edge Function 이 교환해 여기 보관한다.
-- 탈퇴 신청 때와 withdrawal-guard 의 최종 삭제 직전에 철회하고 행을 지운다.
-- 읽고 쓰는 쪽은 apple-token 함수(service_role)뿐이다.
create table if not exists public.apple_auth_tokens (
  account_id    uuid        primary key references auth.users(id) on delete cascade,
  client_id     text        not null,   -- 교환에 쓴 App ID(번들 ID). 철회도 같은 값으로 해야 한다
  refresh_token text        not null,
  updated_at    timestamptz not null default now()
);

comment on table public.apple_auth_tokens is
  'Sign in with Apple 철회용 refresh token. apple-token Edge Function(service_role) 전용.';

alter table public.apple_auth_tokens enable row level security;
-- [의도적] 정책 없음 — service_role 전용.
revoke all on public.apple_auth_tokens from public, anon, authenticated;


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

-- ---------------------------------------------------------------------------
-- mail_settings — 우편 설정. 행 하나만 두고 운영자가 값만 바꾼다(운영 콘솔 "우편 설정").
--   expired_retention_days: 기한이 지난 우편을 목록에 남겨 두는 일수. 그동안 게임은 "기한 만료"로
--   보여 주고(받기는 막힌다), 지나면 목록에서 빠지고 정리 크론이 지운다. 0 이면 만료 즉시 빠진다.
-- ---------------------------------------------------------------------------
create table if not exists public.mail_settings (
  id                     int primary key default 1 check (id = 1),
  expired_retention_days int not null default 3 check (expired_retention_days >= 0),
  updated_at             timestamptz not null default now(),
  updated_by             text
);

insert into public.mail_settings (id) values (1) on conflict (id) do nothing;

comment on table public.mail_settings is
  '우편 설정(행 하나). expired_retention_days=만료 우편을 목록에 남겨 두는 일수(0이면 즉시 숨김). 기본값은 여기 한 곳에만 둔다.';

alter table public.mail_settings enable row level security;

revoke all on table public.mail_settings from anon, authenticated;

-- 기본값을 여기 다시 적지 않는다(테이블 DEFAULT 와 두 벌이 된다). 행이 없으면 설치가 깨진 것이라 곧바로 실패시킨다.
create or replace function public.ts_mail_settings()
returns public.mail_settings
language plpgsql
stable
security definer
set search_path = public
as $$
declare v_cfg public.mail_settings%rowtype;
begin
  select * into v_cfg from public.mail_settings where id = 1;
  if not found then
    raise exception 'mail_settings_missing';
  end if;
  return v_cfg;
end;
$$;

comment on function public.ts_mail_settings() is
  '우편 설정 행 읽기. 내부·운영 전용 — 클라이언트는 ts_mail_client_settings 를 쓴다.';

revoke all on function public.ts_mail_settings() from public, anon, authenticated;

-- 클라이언트용 읽기. 게임 안내 문구("만료된 우편은 N일 후 자동 삭제")의 N 을 여기서 받는다 —
-- 게임에 박아 두면 운영 콘솔에서 바꾸는 순간 문구가 틀려진다. 아래 mails_select_own 정책도 이 값을 쓴다.
create or replace function public.ts_mail_client_settings()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object('expired_retention_days', c.expired_retention_days)
    from public.ts_mail_settings() c;
$$;

comment on function public.ts_mail_client_settings() is
  '우편 설정 읽기(클라이언트용). 만료 우편 보관 일수만 돌려준다. 수정은 운영 전용.';

revoke all on function public.ts_mail_client_settings() from public, anon;
grant execute on function public.ts_mail_client_settings() to authenticated;

alter table public.mails enable row level security;

drop policy if exists "mails_select_own" on public.mails;
drop policy if exists "mails_update_own" on public.mails;

-- 본인 + profiles.user_id 일치 + 현재 세션 서버 일치 + 숨김 아님 + 기한 지난 지 보관 일수 안
-- 목록만 만료 우편을 보관 기간 동안 더 보여 준다(게임이 "기한 만료"로 그린다). 배지 숫자(ts_mail_inbox_counts)·
-- 받기(ts_claim_mail_items·ts_claim_all_mail_items)는 지금처럼 expires_at > now() 로 만료 우편을 뺀다 —
-- 그래서 목록에 보이는 만료 우편은 배지에 안 잡히고, 받으면 mail_expired 로 막힌다. 게임은 Mail.IsExpired 로 가른다.
-- 보관 일수는 (select ...) 로 감싸 쿼리당 한 번만 읽는다.
create policy "mails_select_own"
on public.mails for select
using (
  account_id is not null
  and account_id = auth.uid()
  and deleted_at is null
  and expires_at > now() - make_interval(days => (select (public.ts_mail_client_settings()->>'expired_retention_days')::int))
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
  -- 기한이 지난 우편은 보관 기간 동안 목록에 보이지만 수령으로 바꾸지 않는다 — "만료 후엔 받을 수 없다"와 어긋난다.
  if no_attachment and m.items_claimed_at is null and m.expires_at > now() then
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

  -- 기한이 지난 우편은 받을 수 없으니 보상이 남아 있어도 지울 수 있다. 막으면 보관 기간 동안
  -- 받지도 지우지도 못하는 줄이 목록에 남는다.
  if has_reward and m.items_claimed_at is null and m.expires_at > now() then
    raise exception 'cannot_delete_unclaimed';
  end if;

  update public.mails set deleted_at = now() where id = p_mail_id;
end;
$$;

comment on function public.ts_delete_mail_for_user(uuid) is
  '우편함에서 숨김. 기한 안의 보상 미수령이면 거부(만료 우편은 허용). SECURITY DEFINER.';

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
      -- 받은 우편만. 기한이 지난 미수령 우편은 넣지 않는다 — 게임이 우편함을 열 때마다 이 함수를 부르면
      -- 보관 기간 동안 "기한 만료"로 보여야 할 줄이 열자마자 사라진다(2026-10-19 DefenceR 에서 실제로 그랬다).
      -- 만료 우편은 보관 기간이 끝나면 ts_cleanup_expired_mails 가 지운다.
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
  '수령한(items_claimed_at) 메일만 일괄 숨김. 미수령(만료 포함)은 제외. p_category=null이면 전체 분류. 반환: 처리 행 수. SECURITY DEFINER.';

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
  v_cutoff timestamptz;
begin
  if p_batch is null or p_batch < 1 then
    p_batch := 500;
  end if;
  if p_batch > 10000 then
    p_batch := 10000;
  end if;

  -- 보관 기간이 지난 것만 지운다. 크론이 하루 한 번이라 실제 삭제는 최대 하루 늦지만, 그 사이에도
  -- mails_select_own 이 같은 기준으로 목록에서 빼므로 게임에는 보관 일수대로 보인다.
  select now() - make_interval(days => c.expired_retention_days) into v_cutoff
    from public.ts_mail_settings() c;

  delete from public.mails
  where id in (
    select m.id
    from public.mails m
    where m.expires_at < v_cutoff
    limit p_batch
  );

  get diagnostics n = row_count;
  return n;
end;
$$;

comment on function public.ts_cleanup_expired_mails(int) is
  '만료 후 보관 기간(mail_settings.expired_retention_days)이 지난 메일을 배치 삭제. service_role 전용 호출 권장.';

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
  add column if not exists price_amount_krw bigint,     -- KRW 환산 금액(원, 정수). 매출 확인용 — 결제 날짜 환율(fx_rates). null = 금액·환율을 아직 모름
  add column if not exists granted_at       timestamptz, -- 게임이 실제로 아이템을 지급 완료한 시각. null = 아직 미지급
  -- 시험 결제. 애플 Sandbox(TestFlight·앱 심사 포함), 구글 라이선스 테스트 계정. null = 이 칸이 생기기 전 기록이라 모름(실제로 센다).
  -- 매출·대시보드·누적 결제액(total_paid_krw)에서 뺀다 — 진짜 돈이 아니다.
  add column if not exists is_test          boolean,
  -- 무료 결제 — 구글 프로모션 코드·광고 보상(purchaseType 1·2). 시험은 아니지만 돈을 낸 게 아니라 매출·누적 결제액에서 뺀다.
  add column if not exists is_free          boolean,
  -- 시험 여부를 구글에 마지막으로 물어본 시각(purchase-refund-sync-google). 답을 못 받은 건을 한동안 뒤로 미루는 데 쓴다.
  add column if not exists test_checked_at  timestamptz,
  -- 결제 금액을 애플에 마지막으로 물어본 시각(옛 방식 iOS 결제 — 영수증에 가격이 없다). purchase-price-sync 가
  -- 답을 못 받은 건을 한동안 뒤로 미루는 데 쓴다.
  add column if not exists price_checked_at timestamptz;

create index if not exists purchases_is_test_idx on public.purchases (verified_at desc) where is_test;
-- 시험 여부를 모르는 결제만 담은 색인 — 재확인이 다 끝나 0건이 된 뒤에도 매일 결제 표 전체를 훑지 않게.
create index if not exists purchases_test_unknown_idx on public.purchases (test_checked_at nulls first, verified_at desc) where is_test is null;
-- 금액이나 원화 값이 빈 결제만 담은 색인 — purchase-price-sync 가 매일 결제 표 전체를 훑지 않게.
-- 금액을 애플에 물어 채우는 건 애플 결제뿐이라 애플만 담는다. 구글 결제는 물을 곳이 없어 확인 시각이 늘 비어 있으므로,
-- 섞이면 색인 맨 앞에 쌓여 매일 건너뛰며 읽게 된다(옛 이름의 색인이 그랬다).
drop index if exists public.purchases_price_unknown_idx;
create index if not exists purchases_apple_price_unknown_idx on public.purchases (price_checked_at nulls first, verified_at desc)
  where price_amount is null and store = 'apple_app_store';
create index if not exists purchases_krw_unknown_idx on public.purchases (verified_at) where price_amount is not null and price_amount_krw is null;

-- 가입 플랫폼 추정(2절 user_profiles.signup_platform) — 결제 기록을 근거로 쓰므로 이 표가 생긴 뒤에 둔다.
-- 2절에서 가입한 날의 기록으로 채우고도 남은 옛 계정은 남아 있는 가장 이른 흔적으로 채운다: 첫 접속 기록의 플랫폼과
-- 첫 결제의 스토어 중 이른 쪽, 둘 다 없으면 지금 로그인 플랫폼. 가입 뒤 기기를 바꾼 계정은 틀릴 수 있지만
-- "알 수 없음"을 남기지 않기로 했다(2026-10-29 사용자 결정 — 처음엔 비워 두다가 같은 날 바꿈).
update public.user_profiles p
   set signup_platform = e.platform
  from (select p2.account_id,
               coalesce((select x.platform from (
                           select a.day::timestamptz as at, a.platform
                             from public.user_daily_activity a
                            where a.account_id = p2.account_id and a.platform is not null
                           union all
                           select pu.verified_at,
                                  case pu.store when 'google_play' then 'android' when 'apple_app_store' then 'ios' end
                             from public.purchases pu
                            where pu.account_id = p2.account_id) x
                         where x.platform is not null order by x.at limit 1),
                        nullif(p2.platform, '')) as platform
          from public.user_profiles p2
         where p2.signup_platform is null and p2.account_id is not null) e
 where p.account_id = e.account_id and e.platform is not null;
update public.user_daily_activity a
   set signup_platform = p.signup_platform
  from public.user_profiles p
 where p.account_id = a.account_id and a.signup_platform is null and p.signup_platform is not null;
-- 프로필이 없는(탈퇴로 지워진) 계정의 일별 기록은 그 계정의 첫 접속 플랫폼으로 채운다.
update public.user_daily_activity a
   set signup_platform = f.platform
  from (select distinct on (account_id) account_id, platform
          from public.user_daily_activity
         where platform is not null
         order by account_id, day) f
 where a.account_id = f.account_id and a.signup_platform is null;

-- 원화 환산용 환율표. purchase-price-sync 가 하루 한 번 Frankfurter(키 없음, 중앙은행·공식 출처 환율)에서
-- 원화 기준 전체 통화를 받아 둔다. 결제를 확인할 때는 결제 날짜 이전 4일 안의 가장 가까운 값을 쓰고, 표에 없는
-- 통화·날짜는 그때 물어 여기 남긴다. 통화마다 하루 한 줄이라 쌓여도 작다(1년 약 6만 줄) — 지우지 않는다.
create table if not exists public.fx_rates (
  currency     text    not null,          -- ISO 4217 (예: "USD")
  rate_date    date    not null,          -- 환율 출처가 발표한 날짜
  krw_per_unit numeric not null check (krw_per_unit > 0), -- 그 통화 1단위가 몇 원인지
  fetched_at   timestamptz not null default now(),
  primary key (currency, rate_date)
);

alter table public.fx_rates enable row level security;
revoke all on table public.fx_rates from anon, authenticated;

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
  -- 기록될 때 이미 환불된 결제(환불 알림이 먼저 온 경우)·시험·무료 결제는 누적 결제액에 넣지 않는다.
  -- 몫 계산은 기록된 뒤 바뀔 때(ts_after_purchase_paid_change)와 같은 함수를 쓴다.
  if public.ts_purchase_paid_share(new.price_amount_krw, new.refunded_at, new.is_test, new.is_free) > 0 then
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
--        AND pu.refunded_at IS NULL
--    ), 0);

-- =============================================================================
-- 지급 완료 기록 RPC
-- 선행: 없음 (purchases.granted_at 컬럼만 필요)
--
-- onGrant 콜백이 true를 반환하면 SDK가 내부적으로 이 RPC를 호출해 orderId를 지급 완료로
-- 표시합니다. 게임 코드가 직접 부르는 API가 아닙니다(SupabaseIAP 파사드 내부 전용).
-- 소모품은 "이미 가지고 있는지"로 중복 지급을 판단할 수 없어, 서버가 주문 단위로 지급
-- 여부를 기억해 두었다가 재처리 시 onGrant에 alreadyGranted로 알려줍니다.
-- =============================================================================

-- 반환 타입 변경(void → boolean) 시 CREATE OR REPLACE로는 안 바뀌므로 먼저 지운다.
drop function if exists public.ts_mark_purchase_granted(text);

create or replace function public.ts_mark_purchase_granted(p_order_id text)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
  v_row_count integer;
begin
  update public.purchases
     set granted_at = now()
   where order_id = p_order_id
     and account_id = auth.uid()
     and granted_at is null;

  -- 호출 계정과 주문 소유 계정이 다르면(계정 전환 중 호출 등) 0행 갱신 — 호출부가 구분할 수 있게 알린다.
  get diagnostics v_row_count = row_count;
  return v_row_count > 0;
end;
$$;

comment on function public.ts_mark_purchase_granted(text) is
  '주문 orderId를 지급 완료로 표시. onGrant가 true를 반환하면 SDK가 내부적으로 호출. SECURITY DEFINER, 본인 소유 주문만 갱신. 반환값은 실제 갱신 행 존재 여부.';

revoke all on function public.ts_mark_purchase_granted(text) from public, anon, authenticated;
grant execute on function public.ts_mark_purchase_granted(text) to authenticated;

-- =============================================================================
-- 환불 기록
--
-- 스토어가 환불한 결제를 표시한다. 애플은 purchase-notify-apple 이 환불 알림을 받아서,
-- 구글은 purchase-refund-sync-google 이 하루 한 번 환불 목록을 가져와서 ts_purchase_set_refund 를 부른다.
-- 기록만 한다 — 차단·아이템 회수는 운영자가 보고 정한다. 행을 지우지 않으므로 환불이 다시
-- 취소되면(애플 REFUND_REVERSED) refunded_at 만 비운다.
-- =============================================================================

alter table public.purchases
  add column if not exists refunded_at   timestamptz,  -- 스토어가 환불한 시각. null = 환불 안 됨
  add column if not exists refund_reason text;          -- 스토어가 준 사유 원문(애플 revocationReason · 구글 voidedReason)

create index if not exists purchases_refunded_idx on public.purchases (account_id) where refunded_at is not null;

-- 결제 한 건이 누적 결제액(total_paid_krw)에 들어가는 몫. 환불·시험·무료면 0.
-- 결제 기록·환불·시험 표시·무료 표시가 어떤 순서로, 한꺼번에 바뀌어도 "바뀐 뒤 몫 − 바뀌기 전 몫"만 더하면 맞는다.
create or replace function public.ts_purchase_paid_share(p_krw bigint, p_refunded_at timestamptz, p_is_test boolean, p_is_free boolean)
returns bigint
language sql
immutable
as $$
  select case when coalesce(p_krw, 0) > 0 and p_refunded_at is null and p_is_test is not true and p_is_free is not true
              then p_krw else 0 end;
$$;

revoke all on function public.ts_purchase_paid_share(bigint, timestamptz, boolean, boolean) from public, anon, authenticated;

-- 누적 결제액은 환불·시험·무료를 뺀 값이다. 기록된 뒤 그 칸들이 바뀌면 차이만큼 맞춘다.
-- 탈퇴로 계정 연결이 끊긴 결제(account_id null)는 남은 탈퇴 프로필을 user_id 로 찾되, 딱 하나일 때만 맞춘다 —
-- 같은 구글·애플 계정으로 탈퇴·재가입을 되풀이하면(QA 계정) 같은 user_id 의 탈퇴 프로필이 여럿 생겨 어느 쪽 결제인지 모른다.
create or replace function public.ts_after_purchase_paid_change()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_delta bigint;
  v_n     int;
begin
  v_delta := public.ts_purchase_paid_share(new.price_amount_krw, new.refunded_at, new.is_test, new.is_free)
           - public.ts_purchase_paid_share(old.price_amount_krw, old.refunded_at, old.is_test, old.is_free);
  if v_delta = 0 then
    return new;
  end if;
  if new.account_id is not null then
    update public.user_profiles
       set total_paid_krw = greatest(total_paid_krw + v_delta, 0)
     where account_id = new.account_id;
  elsif new.user_id is not null then
    select count(*) into v_n from public.user_profiles where account_id is null and user_id = new.user_id;
    if v_n = 1 then
      update public.user_profiles
         set total_paid_krw = greatest(total_paid_krw + v_delta, 0)
       where account_id is null and user_id = new.user_id;
    end if;
  end if;
  return new;
end;
$$;

revoke all on function public.ts_after_purchase_paid_change() from public, anon, authenticated;

-- 예전에 환불·시험 표시를 따로 보던 트리거 둘을 하나로 합쳤다(둘이 한꺼번에 바뀌면 서로 몰라 계산이 어긋났다).
drop trigger if exists tr_after_purchase_refund on public.purchases;
drop trigger if exists tr_after_purchase_test_change on public.purchases;
drop function if exists public.ts_after_purchase_refund();
drop function if exists public.ts_after_purchase_test_change();
drop trigger if exists tr_after_purchase_paid_change on public.purchases;
create trigger tr_after_purchase_paid_change
  after update of refunded_at, is_test, is_free, price_amount_krw on public.purchases
  for each row execute function public.ts_after_purchase_paid_change();

-- 스토어별 마지막 확인 상태. 애플은 알림을 받은 시각, 구글은 목록을 가져온 시각.
-- 'price_sync' 는 환율표 저장·빈 금액 채우기(purchase-price-sync)의 마지막 실행이다.
-- 실패도 남긴다 — 조용히 멈춘 것과 환불이 없는 것을 운영 콘솔에서 구분하려는 것.
create table if not exists public.purchase_refund_sync (
  store            text primary key,      -- 'google_play' | 'apple_app_store' | 'price_sync'
  last_started_at  timestamptz,
  last_finished_at timestamptz,
  last_ok          boolean,
  last_message     text,
  last_marked      int not null default 0  -- 이번에 새로 환불로 표시한 건수
);

alter table public.purchase_refund_sync enable row level security;
revoke all on table public.purchase_refund_sync from anon, authenticated;

-- 애플 환불 알림은 한 번만 온다(200 을 받으면 다시 안 보낸다). 유저가 결제하고 게임을 열기 전에 환불되면
-- 알림이 결제 기록보다 먼저 와서 바꿀 행이 없다. 그래서 알림의 마지막 상태를 여기 남겨 두고, 결제가
-- 기록될 때(tr_before_purchase_insert_refund) 붙인다. event_at 은 애플이 서명한 시각 — 재전송으로 순서가
-- 뒤바뀐 알림(환불 → 환불 취소 → 늦게 다시 온 환불)이 최종 상태를 거꾸로 만들지 않게 비교한다.
create table if not exists public.purchase_refund_events (
  store          text        not null,
  purchase_token text        not null,
  refunded_at    timestamptz,           -- null = 환불 취소
  refund_reason  text,
  event_at       timestamptz not null,
  updated_at     timestamptz not null default now(),
  primary key (store, purchase_token)
);

alter table public.purchase_refund_events enable row level security;
revoke all on table public.purchase_refund_events from anon, authenticated;

comment on table public.purchase_refund_events is
  '스토어 환불 알림의 거래별 마지막 상태. 알림이 결제 기록보다 먼저 와도 잃지 않게 한다. ts_purchase_set_refund 전용.';

create or replace function public.ts_before_purchase_insert_refund()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event record;
begin
  if new.refunded_at is null then
    select refunded_at, refund_reason into v_event
      from public.purchase_refund_events
     where store = new.store and purchase_token = new.purchase_token;
    if found and v_event.refunded_at is not null then
      new.refunded_at   := v_event.refunded_at;
      new.refund_reason := v_event.refund_reason;
    end if;
  end if;
  return new;
end;
$$;

revoke all on function public.ts_before_purchase_insert_refund() from public, anon, authenticated;

drop trigger if exists tr_before_purchase_insert_refund on public.purchases;
create trigger tr_before_purchase_insert_refund
  before insert on public.purchases
  for each row execute function public.ts_before_purchase_insert_refund();

-- 인자가 늘어 예전 4인자 판을 지운다(남겨 두면 이름으로 부를 때 어느 쪽인지 모호해진다).
drop function if exists public.ts_purchase_set_refund(text, text, timestamptz, text);

-- 반환: 실제로 바뀐 행이 있는지. 우리 기록에 없는 결제(다른 앱·기록 전 결제)면 false.
-- p_event_at(애플 알림의 서명 시각)을 넘기면 알림 상태를 남기고, 이미 남은 것보다 오래된 알림은 무시한다.
create or replace function public.ts_purchase_set_refund(
  p_store       text,
  p_token       text,
  p_refunded_at timestamptz,
  p_reason      text default null,
  p_event_at    timestamptz default null)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
  v_count int;
begin
  if p_event_at is not null then
    insert into public.purchase_refund_events (store, purchase_token, refunded_at, refund_reason, event_at)
    values (p_store, p_token, p_refunded_at, p_reason, p_event_at)
    on conflict (store, purchase_token) do update
      set refunded_at = excluded.refunded_at, refund_reason = excluded.refund_reason,
          event_at = excluded.event_at, updated_at = now()
      where purchase_refund_events.event_at < excluded.event_at;
    if not found then
      return false;   -- 더 새 알림이 이미 반영됐다
    end if;
  end if;

  if p_refunded_at is null then
    update public.purchases
       set refunded_at = null, refund_reason = null
     where store = p_store and purchase_token = p_token and refunded_at is not null;
  else
    -- 이미 표시된 건 다시 쓰지 않는다 — 구글 목록은 30일치를 매번 다시 받는다.
    update public.purchases
       set refunded_at = p_refunded_at, refund_reason = p_reason
     where store = p_store and purchase_token = p_token and refunded_at is null;
  end if;
  get diagnostics v_count = row_count;
  return v_count > 0;
end;
$$;

comment on function public.ts_purchase_set_refund(text, text, timestamptz, text, timestamptz) is
  '결제를 환불로 표시하거나(p_refunded_at) 환불 취소로 되돌린다(null). purchase-notify-apple·purchase-refund-sync-google 전용.';

revoke all on function public.ts_purchase_set_refund(text, text, timestamptz, text, timestamptz) from public, anon, authenticated;
grant execute on function public.ts_purchase_set_refund(text, text, timestamptz, text, timestamptz) to service_role;

-- 'start' 는 마지막 시작이 p_min_gap_seconds 안이면 기록하지 않고 false 를 돌려준다(겹친 호출 건너뛰기).
-- 'finish' 는 항상 기록하고 true.
create or replace function public.ts_purchase_refund_sync_mark(
  p_store           text,
  p_phase           text,              -- 'start' | 'finish'
  p_ok              boolean default null,
  p_message         text    default null,
  p_marked          int     default 0,
  p_min_gap_seconds int     default 0)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_phase = 'start' then
    insert into public.purchase_refund_sync as s (store, last_started_at)
    values (p_store, now())
    on conflict (store) do update set last_started_at = now()
      where s.last_started_at is null
         or s.last_started_at < now() - make_interval(secs => greatest(coalesce(p_min_gap_seconds, 0), 0));
    return found;
  end if;

  insert into public.purchase_refund_sync (store, last_finished_at, last_ok, last_message, last_marked)
  values (p_store, now(), p_ok, p_message, coalesce(p_marked, 0))
  on conflict (store) do update
    set last_finished_at = now(), last_ok = p_ok, last_message = p_message, last_marked = coalesce(p_marked, 0);
  return true;
end;
$$;

revoke all on function public.ts_purchase_refund_sync_mark(text, text, boolean, text, int, int) from public, anon, authenticated;
grant execute on function public.ts_purchase_refund_sync_mark(text, text, boolean, text, int, int) to service_role;

-- 환불을 물어볼 앱 목록. REST 로 읽으면 1000행에서 잘려 앱이 빠질 수 있어 DB 에서 고른다.
create or replace function public.ts_purchase_recent_packages(p_store text, p_days int)
returns text[]
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(array_agg(distinct package_name order by package_name), '{}')
    from public.purchases
   where store = p_store
     and verified_at >= now() - make_interval(days => greatest(coalesce(p_days, 0), 1));
$$;

revoke all on function public.ts_purchase_recent_packages(text, int) from public, anon, authenticated;
grant execute on function public.ts_purchase_recent_packages(text, int) to service_role;

-- =============================================================================
-- 서버 기능 연결값 — 스토어·애플 연동 비밀값을 운영 콘솔에서 넣는다
--
-- 운영자가 Supabase 대시보드에 들어가지 않고 콘솔 "설정"에서 값을 넣도록, 값을 Vault 에 암호화해 둔다
-- (이름 'ts_cfg:<키>', 설명 칸에 넣은 운영자). 엣지 함수는 ts_server_config_get 으로 읽고, 없는 키는
-- 예전처럼 Edge Function Secrets 에서 읽는다 — 콘솔 값이 이긴다.
-- 값을 다시 꺼내는 함수는 ts_server_config_get 하나이고 service_role 전용이다. 콘솔에는 넣은 시각·사람만 돌려준다.
-- =============================================================================

create or replace function public.ts_server_config_keys()
returns text[]
language sql
immutable
as $$
  select array['APPLE_BUNDLE_ID', 'APPLE_SHARED_SECRET', 'GOOGLE_PACKAGE_NAME', 'GOOGLE_SERVICE_ACCOUNT_JSON',
               'APPLE_TEAM_ID', 'APPLE_KEY_ID', 'APPLE_PRIVATE_KEY',
               'APPLE_IAP_ISSUER_ID', 'APPLE_IAP_KEY_ID', 'APPLE_IAP_PRIVATE_KEY'];
$$;

revoke all on function public.ts_server_config_keys() from public, anon, authenticated;

create or replace function public.ts_server_config_get()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_object_agg(substr(s.name, 8), s.decrypted_secret), '{}'::jsonb)
    from vault.decrypted_secrets s
   where s.name like 'ts\_cfg:%'
     and substr(s.name, 8) = any(public.ts_server_config_keys());
$$;

comment on function public.ts_server_config_get() is
  '운영 콘솔에서 넣은 서버 기능 연결값(복호화). 엣지 함수 전용 — service_role 만.';

revoke all on function public.ts_server_config_get() from public, anon, authenticated;
grant execute on function public.ts_server_config_get() to service_role;

create or replace function public.ts_admin_server_config_status()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'key',        substr(s.name, 8),
           'updated_at', s.updated_at,
           'updated_by', nullif(s.description, ''))
           order by s.name), '[]'::jsonb)
    from vault.secrets s
   where s.name like 'ts\_cfg:%'
     and substr(s.name, 8) = any(public.ts_server_config_keys());
$$;

comment on function public.ts_admin_server_config_status() is
  '운영 콘솔에서 넣은 연결값의 키·수정 시각·수정자. 값은 돌려주지 않는다. service_role 전용.';

revoke all on function public.ts_admin_server_config_status() from public, anon, authenticated;
grant execute on function public.ts_admin_server_config_status() to service_role;

-- 빈 값이면 콘솔 값을 지운다 — 그 뒤로는 Edge Function Secrets 값을 쓴다.
create or replace function public.ts_admin_server_config_set(p_key text, p_value text, p_by text)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_name text := 'ts_cfg:' || coalesce(p_key, '');
  v_id   uuid;
begin
  if p_key is null or not (p_key = any(public.ts_server_config_keys())) then
    raise exception 'server_config_unknown_key: %', p_key;
  end if;

  select id into v_id from vault.secrets where name = v_name;

  if p_value is null or btrim(p_value) = '' then
    if v_id is not null then
      delete from vault.secrets where id = v_id;
    end if;
    return;
  end if;

  if v_id is null then
    perform vault.create_secret(btrim(p_value), v_name, coalesce(p_by, ''));
  else
    perform vault.update_secret(v_id, btrim(p_value), v_name, coalesce(p_by, ''));
  end if;
end;
$$;

comment on function public.ts_admin_server_config_set(text, text, text) is
  '서버 기능 연결값을 넣거나 바꾼다(빈 값이면 지운다). 형식 검사는 admin-api 몫. service_role 전용.';

revoke all on function public.ts_admin_server_config_set(text, text, text) from public, anon, authenticated;
grant execute on function public.ts_admin_server_config_set(text, text, text) to service_role;

-- 이 게임이 쓰는 서비스(App Store·Google Play·Sign in with Apple) — 콘솔 "서비스 연결"의 켜기/끄기.
-- 화면 표시만 바꾼다(켠 서비스는 필수 값을 표시, 끈 서비스는 접음). 엣지 함수는 이 표를 보지 않는다 —
-- 잘못 꺼도 결제·탈퇴가 막히지 않게(2026-10-29 사용자 결정). 줄이 없는 서비스는 콘솔이 값 유무로 짐작한다.
create table if not exists public.server_config_services (
  service    text primary key check (service in ('appstore', 'googleplay', 'signin')),
  enabled    boolean not null,
  updated_at timestamptz not null default now(),
  updated_by text
);
alter table public.server_config_services enable row level security;
-- [의도적] 정책 없음 — 아래 함수만 쓴다.
revoke all on table public.server_config_services from anon, authenticated;

create or replace function public.ts_admin_server_config_services()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_object_agg(service, enabled), '{}'::jsonb) from public.server_config_services;
$$;

create or replace function public.ts_admin_server_config_service_set(p_service text, p_enabled boolean, p_by text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_service is null or p_service not in ('appstore', 'googleplay', 'signin') or p_enabled is null then
    raise exception 'server_config_service_invalid: %', p_service;
  end if;
  insert into public.server_config_services (service, enabled, updated_at, updated_by)
  values (p_service, p_enabled, now(), p_by)
  on conflict (service) do update set enabled = excluded.enabled, updated_at = now(), updated_by = excluded.updated_by;
  return public.ts_admin_server_config_services();
end;
$$;

comment on function public.ts_admin_server_config_services() is
  '서비스 연결 켜기/끄기 상태({서비스: 켜짐}). 콘솔 화면 표시용. service_role 전용.';
comment on function public.ts_admin_server_config_service_set(text, boolean, text) is
  '서비스 연결 켜기/끄기. 엣지 함수 동작은 바뀌지 않는다. service_role 전용.';

revoke all on function public.ts_admin_server_config_services() from public, anon, authenticated;
revoke all on function public.ts_admin_server_config_service_set(text, boolean, text) from public, anon, authenticated;
grant execute on function public.ts_admin_server_config_services() to service_role;
grant execute on function public.ts_admin_server_config_service_set(text, boolean, text) to service_role;


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
-- · 탈퇴 예약 만료 계정 삭제  30분마다   ts_withdrawal_sweep_kick() → 엣지 함수 withdrawal-sweep
-- · 그 결과 확인              매시 15분  ts_withdrawal_overdue_check()
-- · 실행 기록 정리            매일 03:40  cron.job_run_details 7일치만 남김
-- · 동시접속자 기록          5분마다     ts_ccu_snapshot() → ccu_snapshots(90일 보관) + ccu_daily(일별 요약, 영구)
-- (시각은 모두 UTC)
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
-- 탈퇴 유예가 지난 계정의 실제 삭제 — 엣지 함수 withdrawal-sweep 이 한다(애플 연결 끊기 → auth 계정 삭제).
-- 예전에는 그 계정이 다시 로그인할 때만(withdrawal-guard) 지워져, 다시 안 들어온 탈퇴자가 영영 남았다.
-- 옛 방식(삭제 대기 표에 넣기만 하는 ts_withdrawal_cleanup_batch)은 그 표를 읽는 곳이 없어 없앴다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_withdrawal_cleanup_batch(int);
drop table if exists public.withdrawal_delete_queue;

-- 유예가 지난 계정 목록. withdrawal-sweep 이 secret key 로 부른다.
create or replace function public.ts_withdrawal_due_accounts(p_limit int default 50)
returns table (account_id uuid, user_id text)
language sql
stable
security definer
set search_path = public
as $$
  select p.account_id, p.user_id
  from public.user_profiles p
  where p.withdrawn_at is not null
    and p.withdrawn_at <= now()
    and p.account_id is not null
  order by p.withdrawn_at
  limit greatest(1, least(coalesce(p_limit, 50), 200));
$$;

comment on function public.ts_withdrawal_due_accounts(int) is '탈퇴 유예가 지났는데 아직 지워지지 않은 계정. withdrawal-sweep 전용.';
revoke all on function public.ts_withdrawal_due_accounts(int) from public, anon, authenticated;
grant execute on function public.ts_withdrawal_due_accounts(int) to service_role;

-- withdrawal-sweep 은 인증 없이 열려 있다 — 마지막 시작 시각으로 짧은 간격의 재호출을 건너뛴다.
create table if not exists public.withdrawal_sweep_state (
  id              int primary key default 1 check (id = 1),
  last_started_at timestamptz
);
alter table public.withdrawal_sweep_state enable row level security;
-- [의도적] 정책 없음 — 아래 함수만 쓴다.
revoke all on table public.withdrawal_sweep_state from anon, authenticated;

create or replace function public.ts_withdrawal_sweep_try_start(p_min_gap_seconds int default 600)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
  v_ok boolean;
begin
  insert into public.withdrawal_sweep_state as s (id, last_started_at) values (1, now())
  on conflict (id) do update set last_started_at = now()
   where s.last_started_at is null
      or s.last_started_at < now() - make_interval(secs => greatest(coalesce(p_min_gap_seconds, 600), 0))
  returning true into v_ok;
  return coalesce(v_ok, false);
end;
$$;

comment on function public.ts_withdrawal_sweep_try_start(int) is '탈퇴 계정 정리 시작 기록. 간격 안의 재호출이면 false. withdrawal-sweep 전용.';
revoke all on function public.ts_withdrawal_sweep_try_start(int) from public, anon, authenticated;
grant execute on function public.ts_withdrawal_sweep_try_start(int) to service_role;


-- ---------------------------------------------------------------------------
-- 크론 잡 등록 (멱등: 기존 잡이 있으면 교체)
-- ---------------------------------------------------------------------------

-- 1. 메일 만료 정리 — 매일 UTC 3시(한국 정오) (batch 500). pg_cron 의 시각은 UTC 다(cron.timezone = GMT).
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

-- 2. 탈퇴 계정 정리는 pg_net 이 필요해 아래 4번에 있다.

-- 3. 구글 환불 확인 — 매일 UTC 4시(한국 13시). DB 에서 엣지 함수를 부르려면 pg_net 과 Vault 의 project_url 이 필요하다.
--    project_url 은 프로젝트마다 다르므로 이 파일에 넣지 않는다. 설치 후 한 번 등록한다:
--      select vault.create_secret('https://<project-ref>.supabase.co', 'project_url');
--    없으면 확인하지 않고 그 사실을 purchase_refund_sync 에 남긴다(운영 콘솔 구매 내역에 보인다).
create extension if not exists pg_net with schema extensions;

create or replace function public.ts_purchase_refund_sync_kick()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_url text;
begin
  select decrypted_secret into v_url from vault.decrypted_secrets where name = 'project_url';
  if v_url is null or btrim(v_url) = '' then
    perform public.ts_purchase_refund_sync_mark('google_play', 'finish', false, 'project_url_missing', 0, 0);
    return;
  end if;
  -- 함수 쪽이 10분 안의 재호출을 건너뛰므로 인증 없이 부른다(verify_jwt 끔).
  perform net.http_post(
    url                  := rtrim(btrim(v_url), '/') || '/functions/v1/purchase-refund-sync-google',
    body                 := '{}'::jsonb,
    headers              := '{"Content-Type": "application/json"}'::jsonb,
    timeout_milliseconds := 60000);
end;
$$;

comment on function public.ts_purchase_refund_sync_kick() is
  '구글 환불 확인 엣지 함수를 부른다. cron 이 매일 부른다.';

revoke all on function public.ts_purchase_refund_sync_kick() from public, anon, authenticated;

do $$
begin
  if exists (select 1 from cron.job where jobname = 'purchase-refund-sync-google') then
    perform cron.unschedule('purchase-refund-sync-google');
  end if;
end $$;
select cron.schedule(
  'purchase-refund-sync-google',
  '0 4 * * *',
  'select public.ts_purchase_refund_sync_kick()'
);

-- 3-1. 환율표 저장·빈 금액 채우기 — 매일 UTC 4시 30분(한국 13시 30분). 유럽중앙은행 등은 전날 UTC 15시 무렵 발표하므로
--      전날 발표분이 들어온다. 엣지 함수 purchase-price-sync 가 원화 기준 환율을 받아 두고,
--      금액이 없는 옛 방식 iOS 결제는 애플에 실제 결제 금액을 묻고, 원화 값이 빈 결제는 결제 날짜 환율로 채운다.
--      3번과 같이 project_url 이 필요하고, 없으면 그 사실을 purchase_refund_sync('price_sync')에 남긴다.
create or replace function public.ts_purchase_price_sync_kick()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_url text;
begin
  select decrypted_secret into v_url from vault.decrypted_secrets where name = 'project_url';
  if v_url is null or btrim(v_url) = '' then
    perform public.ts_purchase_refund_sync_mark('price_sync', 'finish', false, 'project_url_missing', 0, 0);
    return;
  end if;
  -- 함수 쪽이 10분 안의 재호출을 건너뛰므로 인증 없이 부른다(verify_jwt 끔).
  perform net.http_post(
    url                  := rtrim(btrim(v_url), '/') || '/functions/v1/purchase-price-sync',
    body                 := '{}'::jsonb,
    headers              := '{"Content-Type": "application/json"}'::jsonb,
    timeout_milliseconds := 120000);
end;
$$;

comment on function public.ts_purchase_price_sync_kick() is
  '환율표 저장·빈 결제 금액 채우기 엣지 함수를 부른다. cron 이 매일 부른다.';

revoke all on function public.ts_purchase_price_sync_kick() from public, anon, authenticated;

do $$
begin
  if exists (select 1 from cron.job where jobname = 'purchase-price-sync') then
    perform cron.unschedule('purchase-price-sync');
  end if;
end $$;
select cron.schedule(
  'purchase-price-sync',
  '30 4 * * *',
  'select public.ts_purchase_price_sync_kick()'
);

-- 4. 탈퇴 계정 정리 — 30분마다. 엣지 함수 withdrawal-sweep 이 유예 지난 계정을 지운다(3번과 같이 project_url 필요).
--    삭제 시각은 게임마다 정한 시간대의 자정(ts_withdrawal_deadline)이라 UTC 로는 게임마다 다르다 — 30분마다 돌면
--    어느 시간대든(30분 단위 시차까지) 자정 직후에 지운다. 지울 계정이 없으면 엣지 함수를 부르지 않는다.
--    project_url 이 없으면 실패로 끝내 "자동 작업" 화면에 보이게 한다 — 조용히 넘기면 탈퇴자가 안 지워진 채 쌓인다.
create or replace function public.ts_withdrawal_sweep_kick()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_url text;
begin
  -- 주소 확인을 먼저 한다 — 지울 계정이 생긴 뒤에야 실패하면 첫 탈퇴 마감 때까지 설정 누락이 안 보인다.
  select decrypted_secret into v_url from vault.decrypted_secrets where name = 'project_url';
  if v_url is null or btrim(v_url) = '' then
    raise exception 'project_url_missing';
  end if;
  if not exists (select 1 from public.user_profiles
                  where withdrawn_at is not null and withdrawn_at <= now() and account_id is not null) then
    return;
  end if;
  -- 함수 쪽이 유예 지난 계정만 지우므로 인증 없이 부른다(verify_jwt 끔).
  perform net.http_post(
    url                  := rtrim(btrim(v_url), '/') || '/functions/v1/withdrawal-sweep',
    body                 := '{}'::jsonb,
    headers              := '{"Content-Type": "application/json"}'::jsonb,
    timeout_milliseconds := 60000);
end;
$$;

comment on function public.ts_withdrawal_sweep_kick() is '탈퇴 계정 정리 엣지 함수를 부른다(지울 계정이 있을 때만). cron 이 30분마다 부른다.';
revoke all on function public.ts_withdrawal_sweep_kick() from public, anon, authenticated;

-- 위 호출은 요청을 넣기만 해서, 엣지 함수가 실패해도 이 작업은 성공으로 남는다. 그래서 결과를 따로 본다 —
-- 유예가 2시간 넘게 지났는데 남은 계정이 있으면(30분 정리가 네 번 넘게 못 지움) 실패로 끝내 "자동 작업" 화면에 빨갛게 보이게 한다.
create or replace function public.ts_withdrawal_overdue_check()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  v_n int;
begin
  select count(*) into v_n
  from public.user_profiles
  where withdrawn_at is not null
    and withdrawn_at < now() - interval '2 hours'
    and account_id is not null;
  if v_n > 0 then
    raise exception 'withdrawal_overdue: 탈퇴 유예가 2시간 넘게 지났는데 지워지지 않은 계정 %개 — withdrawal-sweep 배포·로그를 확인하세요', v_n;
  end if;
  return 0;
end;
$$;

comment on function public.ts_withdrawal_overdue_check() is '탈퇴 계정 정리가 실제로 됐는지 확인한다(마감 2시간이 지나도 남은 계정이 있으면 실패). cron 이 매시 15분에 부른다.';
revoke all on function public.ts_withdrawal_overdue_check() from public, anon, authenticated;

do $$
begin
  if exists (select 1 from cron.job where jobname = 'withdrawal-cleanup') then
    perform cron.unschedule('withdrawal-cleanup');
  end if;
  if exists (select 1 from cron.job where jobname = 'withdrawal-overdue-check') then
    perform cron.unschedule('withdrawal-overdue-check');
  end if;
end $$;
select cron.schedule(
  'withdrawal-cleanup',
  '0,30 * * * *',
  'select public.ts_withdrawal_sweep_kick()'
);
select cron.schedule(
  'withdrawal-overdue-check',
  '15 * * * *',
  'select public.ts_withdrawal_overdue_check()'
);

-- 5. 자동 작업 실행 기록 정리 — 매일 03:40(UTC). cron.job_run_details 는 저절로 지워지지 않아 매분 작업만으로 하루
--    수천 줄씩 쌓인다(Supabase 문서: 직접 정리해야 하고, 너무 크면 DB 업그레이드 때 부담). 7일치만 남긴다.
do $$
begin
  if exists (select 1 from cron.job where jobname = 'cleanup-cron-run-details') then
    perform cron.unschedule('cleanup-cron-run-details');
  end if;
end $$;
select cron.schedule(
  'cleanup-cron-run-details',
  '40 3 * * *',
  $$delete from cron.job_run_details where end_time < now() - interval '7 days'$$
);

-- 6. 동시접속자 기록 — 5분마다. 접속 중 판정은 2절 ts_session_is_online(SDK 생존 신호 기준).
--    5분에 한 줄(전체 + 서버별·플랫폼별 jsonb)이라 90일이면 약 2만 6천 줄이다. 그보다 오래된 줄은 같은 함수가 지운다.
create table if not exists public.ccu_snapshots (
  at          timestamptz primary key,
  total       integer not null,
  by_server   jsonb   not null default '{}'::jsonb,
  by_platform jsonb   not null default '{}'::jsonb
);
alter table public.ccu_snapshots enable row level security;
-- [의도적] 정책 없음 — 운영 콘솔이 ts_admin_ccu 로만 읽는다.
revoke all on table public.ccu_snapshots from anon, authenticated;

-- 지금 접속 중인 수. 기록(ts_ccu_snapshot)과 콘솔(ts_admin_ccu)이 같은 것을 쓴다.
create or replace function public.ts_ccu_now()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with online as (
    select s.server_id, coalesce(nullif(p.platform, ''), 'unknown') as platform
    from public.user_sessions s
    left join public.user_profiles p on p.account_id = s.account_id
    -- 판정 창은 최대 22분 30초(신호 주기 상한 600초) — 색인으로 먼저 좁힌다.
    where (s.last_seen_at >= now() - interval '23 minutes' or s.updated_at >= now() - interval '23 minutes')
      and public.ts_session_is_online(s.last_seen_at, s.updated_at, s.heartbeat_seconds)
  )
  select jsonb_build_object(
    'total', (select count(*) from online),
    'by_server', coalesce((select jsonb_object_agg(code, n) from (
                   select coalesce(gs.server_code, 'unknown') as code, count(*) as n
                   from online o left join public.game_servers gs on gs.id = o.server_id
                   group by 1) x), '{}'::jsonb),
    'by_platform', coalesce((select jsonb_object_agg(platform, n) from (
                   select platform, count(*) as n from online group by 1) y), '{}'::jsonb));
$$;

comment on function public.ts_ccu_now() is '지금 접속 중인 플레이어 수(전체·서버별·플랫폼별). service_role 전용.';
revoke all on function public.ts_ccu_now() from public, anon, authenticated;

-- 일별 요약 — 지우지 않고 영구 보관한다. 5분 기록은 90일 뒤 지워지므로 긴 기간 추이는 이것으로 본다.
-- 날짜는 게임 시간대 기준(대시보드의 다른 숫자와 같다). 최고치 순간의 서버별·플랫폼별도 함께 남긴다.
create table if not exists public.ccu_daily (
  day         date primary key,
  peak        integer       not null,
  peak_at     timestamptz   not null,
  avg_total   numeric(10,1) not null,
  samples     integer       not null,
  by_server   jsonb         not null default '{}'::jsonb,
  by_platform jsonb         not null default '{}'::jsonb
);
alter table public.ccu_daily enable row level security;
-- [의도적] 정책 없음 — 운영 콘솔이 ts_admin_ccu 로만 읽는다.
revoke all on table public.ccu_daily from anon, authenticated;

-- 그날의 5분 기록으로 요약을 다시 계산한다. 5분마다 그날 것만 다시 쓰므로 하루 마지막 기록까지 반영된다.
create or replace function public.ts_ccu_daily_refresh(p_day date)
returns void
language sql
security definer
set search_path = public
as $$
  with bounds as (
    select (p_day::timestamp at time zone public.ts_game_time_zone()) as s,
           ((p_day + 1)::timestamp at time zone public.ts_game_time_zone()) as e
  ), rows_of_day as (
    select c.* from public.ccu_snapshots c, bounds b where c.at >= b.s and c.at < b.e
  ), top as (
    select at, total, by_server, by_platform from rows_of_day order by total desc, at desc limit 1
  )
  insert into public.ccu_daily (day, peak, peak_at, avg_total, samples, by_server, by_platform)
  select p_day, t.total, t.at, (select round(avg(total), 1) from rows_of_day), (select count(*) from rows_of_day),
         t.by_server, t.by_platform
  from top t
  on conflict (day) do update
    set peak = excluded.peak, peak_at = excluded.peak_at, avg_total = excluded.avg_total,
        samples = excluded.samples, by_server = excluded.by_server, by_platform = excluded.by_platform;
$$;

comment on function public.ts_ccu_daily_refresh(date) is '그날(게임 시간대 날짜)의 동시접속자 일별 요약을 5분 기록으로 다시 계산한다. ts_ccu_snapshot 이 부른다.';
revoke all on function public.ts_ccu_daily_refresh(date) from public, anon, authenticated;

create or replace function public.ts_ccu_snapshot()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_now jsonb := public.ts_ccu_now();
  v_at  timestamptz := date_trunc('minute', now());
begin
  insert into public.ccu_snapshots (at, total, by_server, by_platform)
  values (v_at, (v_now->>'total')::int, v_now->'by_server', v_now->'by_platform')
  on conflict (at) do update
    set total = excluded.total, by_server = excluded.by_server, by_platform = excluded.by_platform;

  perform public.ts_ccu_daily_refresh((v_at at time zone public.ts_game_time_zone())::date);

  delete from public.ccu_snapshots where at < now() - interval '90 days';
end;
$$;

comment on function public.ts_ccu_snapshot() is '지금 접속 중인 플레이어 수를 남기고(전체·서버별·플랫폼별) 그날 일별 요약을 갱신한다. cron 이 5분마다 부른다.';
revoke all on function public.ts_ccu_snapshot() from public, anon, authenticated;

do $$
begin
  if exists (select 1 from cron.job where jobname = 'ccu-snapshot') then
    perform cron.unschedule('ccu-snapshot');
  end if;
end $$;
select cron.schedule(
  'ccu-snapshot',
  '*/5 * * * *',
  'select public.ts_ccu_snapshot()'
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

-- ---------------------------------------------------------------------------
-- 어드민 플레이어 조회 — TrueBase.Admin(Retool 대체 도구) 전용. service_role만.
-- auth.users는 PostgREST에 노출되지 않으므로 SECURITY DEFINER 함수로 감싼다.
-- ---------------------------------------------------------------------------

create or replace function public.ts_admin_search_players(
  p_search      text    default null,
  p_banned_only boolean default false,
  p_page        int     default 1,
  p_page_size   int     default 20
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_search text := nullif(btrim(coalesce(p_search, '')), '');
  v_page   int  := greatest(1, coalesce(p_page, 1));
  v_size   int  := least(100, greatest(1, coalesce(p_page_size, 20)));
  v_total  int;
  v_rows   jsonb;
begin
  -- count(*) over()를 페이지 쿼리 안에서 함께 구해 테이블을 두 번 스캔하지 않는다.
  select coalesce(jsonb_agg(to_jsonb(t) - 'total'), '[]'::jsonb),
         coalesce(max(t.total), 0)
    into v_rows, v_total
  from (
    select
      p.account_id,
      coalesce(d.display_name, p.user_id) as display_name,
      u.is_anonymous     as is_guest,
      p.platform,
      p.country_code,
      p.total_paid_krw,
      u.created_at,
      p.last_activity_at,
      count(*) over() as total
    from public.user_profiles p
    join auth.users u on u.id = p.account_id
    left join public.display_names d on d.account_id = p.account_id
    where p.withdrawn_at is null
      and (v_search is null or d.display_name ilike '%' || v_search || '%')
      and (not p_banned_only or (u.banned_until is not null and u.banned_until > now()))
    order by p.last_activity_at desc nulls last
    limit v_size offset (v_page - 1) * v_size
  ) t;

  return jsonb_build_object('rows', v_rows, 'total', v_total, 'pageSize', v_size);
end;
$$;

create or replace function public.ts_admin_player_profile(p_account_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_row jsonb;
begin
  select jsonb_build_object(
    'account_id',       p.account_id,
    'display_name',     coalesce(d.display_name, p.user_id),
    'is_guest',         u.is_anonymous,
    'platform',         p.platform,
    'signup_platform',  p.signup_platform,
    'country_code',     p.country_code,
    'total_paid_krw',   p.total_paid_krw,
    -- total_paid_krw 는 환불분을 이미 뺀 값이다.
    'refund_count',     (select count(*) from public.purchases r where r.account_id = p.account_id and r.refunded_at is not null and r.is_test is not true and r.is_free is not true),
    'refund_krw',       (select coalesce(sum(r.price_amount_krw), 0) from public.purchases r where r.account_id = p.account_id and r.refunded_at is not null and r.is_test is not true and r.is_free is not true),
    'created_at',       u.created_at,
    'last_activity_at', p.last_activity_at,
    'last_sign_in_at',  u.last_sign_in_at,
    -- 미래 시각이면 탈퇴 예약(그 시각에 삭제), 지났으면 삭제 대기.
    'withdrawn_at',     p.withdrawn_at,
    'server_code',      gs.server_code
  )
  into v_row
  from public.user_profiles p
  join auth.users u on u.id = p.account_id
  left join public.display_names d on d.account_id = p.account_id
  left join public.game_servers gs on gs.id = p.server_id
  where p.account_id = p_account_id;

  if v_row is null then
    raise exception 'player_not_found';
  end if;

  return v_row;
end;
$$;

create or replace function public.ts_admin_player_ban_info(p_account_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_banned_until timestamptz;
  v_message      text;
  v_is_banned    boolean;
begin
  select banned_until into v_banned_until from auth.users where id = p_account_id;
  v_is_banned := v_banned_until is not null and v_banned_until > now();

  if v_is_banned then
    select ban_message into v_message from public.user_ban_messages where account_id = p_account_id;
  end if;

  return jsonb_build_object(
    'isBanned',    v_is_banned,
    'bannedUntil', case when v_is_banned then v_banned_until else null end,
    'banMessage',  v_message
  );
end;
$$;

comment on function public.ts_admin_search_players(text, boolean, int, int) is '플레이어 검색·목록. service_role 전용.';
comment on function public.ts_admin_player_profile(uuid) is '플레이어 1명 프로필(auth.users 포함). service_role 전용.';
comment on function public.ts_admin_player_ban_info(uuid) is '플레이어 1명 차단 정보. service_role 전용.';

revoke all on function public.ts_admin_search_players(text, boolean, int, int) from public, anon, authenticated;
revoke all on function public.ts_admin_player_profile(uuid) from public, anon, authenticated;
revoke all on function public.ts_admin_player_ban_info(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_search_players(text, boolean, int, int) to service_role;
grant execute on function public.ts_admin_player_profile(uuid) to service_role;
grant execute on function public.ts_admin_player_ban_info(uuid) to service_role;

-- ---------------------------------------------------------------------------
-- 게임 서버 관리 — TrueBase.Admin 전용
-- ---------------------------------------------------------------------------
-- allow_new_signups 는 다루지 않는다 — 가입 경로 어디에서도 읽지 않아 바꿔도 아무 일이 없다.
-- 서버 코드는 추가할 때만 정한다. 게임이 코드로 서버를 고르므로 나중에 바꾸면 이전 요청이 깨진다.
-- 삭제도 두지 않는다 — 프로필·세이브가 server_id 를 restrict 로 물고 있다.
create or replace function public.ts_admin_game_servers_list()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'id',              gs.id,
           'server_code',     gs.server_code,
           'display_name',    gs.display_name,
           'allow_transfers', gs.allow_transfers,
           'is_default',      gs.id = public.ts_default_server_id(),
           'player_count',    (select count(*) from public.user_profiles p where p.server_id = gs.id),
           'created_at',      gs.created_at
         ) order by gs.created_at, gs.id), '[]'::jsonb)
    from public.game_servers gs;
$$;

create or replace function public.ts_admin_game_server_upsert(
  p_id              uuid,
  p_server_code     text,
  p_display_name    text,
  p_allow_transfers boolean
)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_code text := btrim(coalesce(p_server_code, ''));
  v_name text := btrim(coalesce(p_display_name, ''));
  v_id   uuid;
begin
  if v_name = '' then
    raise exception 'server_display_name_empty';
  end if;

  if p_id is null then
    if v_code !~ '^[A-Za-z0-9_-]{1,32}$' then
      raise exception 'server_code_invalid';
    end if;
    if exists (select 1 from public.game_servers where server_code = v_code) then
      raise exception 'server_code_taken';
    end if;
    insert into public.game_servers (server_code, display_name, allow_transfers)
    values (v_code, v_name, coalesce(p_allow_transfers, true))
    returning id into v_id;
    return v_id;
  end if;

  update public.game_servers
     set display_name    = v_name,
         allow_transfers = coalesce(p_allow_transfers, allow_transfers)
   where id = p_id
  returning id into v_id;

  if v_id is null then
    raise exception 'server_not_found';
  end if;
  return v_id;
end;
$$;

comment on function public.ts_admin_game_servers_list() is '게임 서버 목록(플레이어 수·기본 서버 표시 포함). service_role 전용.';
comment on function public.ts_admin_game_server_upsert(uuid,text,text,boolean) is
  '게임 서버 추가(p_id null)·이름·이전 허용 수정. 코드는 추가 때만 정한다. service_role 전용.';

revoke all on function public.ts_admin_game_servers_list()                         from public, anon, authenticated;
revoke all on function public.ts_admin_game_server_upsert(uuid,text,text,boolean)  from public, anon, authenticated;
grant execute on function public.ts_admin_game_servers_list()                      to service_role;
grant execute on function public.ts_admin_game_server_upsert(uuid,text,text,boolean) to service_role;

-- ---------------------------------------------------------------------------
-- 요청 제한 — 짧은 시간에 요청을 지나치게 많이 보내는 계정을 잠시 막는다. 운영 콘솔 "설정 › 요청 제한".
--
-- 게임 앱의 데이터 요청(PostgREST)마다 ts_request_limit_check 가 먼저 돈다(pgrst.db_pre_request, Supabase 문서
-- "Securing your API" 의 방식). 로그인한 계정 단위로 세고, 로그인 전 요청은 세지 않는다(2026-10-29 사용자 결정).
-- 세는 것은 쓰기·함수 호출(POST·PATCH·DELETE)뿐이다 — 읽기(GET·HEAD)는 읽기 전용으로 실행돼 기록을 남길 수 없다.
-- 읽기 전용 함수(stable·immutable)도 POST 로 불러도 읽기 전용으로 실행되므로 세지 않는다(2026-10-29 실측).
-- 예외로 끝난 요청도 세지 않는다 — 트랜잭션째 되돌려져 센 숫자까지 사라진다. 대입 공격 대상인 쿠폰(17절)만
-- 실패를 값으로 돌려줘 센다.
-- 제한 중이면 읽기까지 전부 429 로 거절한다. 엣지 함수는 공통 구간 `request-limit` 이 ts_request_limit_ping 을
-- 사용자 로그인으로 불러 같은 검사를 태운다. 운영 콘솔·서버 함수(service_role)의 요청은 세지 않는다.
--
-- [주의] 이 검사는 모든 요청 앞에 끼므로, 실패하면 게임 전체가 멈춘다. 그래서 ① anon·authenticated·service_role
--        모두 실행 권한이 있어야 하고(23절 허용 목록에도 넣는다) ② 안에서 난 오류는 삼키고 통과시킨다 —
--        제한 기능이 고장 나도 게임은 돈다.
-- [주의] 기준을 넘은 바로 그 요청은 통과시키고 제한 기록만 남긴다 — 그 요청에서 거절하면 같은 트랜잭션의
--        제한 기록까지 되돌려진다. 다음 요청부터 거절한다.
-- ---------------------------------------------------------------------------
create table if not exists public.request_limit_settings (
  id             int         primary key default 1 check (id = 1),
  enabled        boolean     not null default false,
  window_seconds int         not null default 10  check (window_seconds between 1 and 600),
  max_requests   int         not null default 100 check (max_requests between 10 and 10000),
  block_minutes  int         not null default 10  check (block_minutes between 1 and 1440),
  updated_at     timestamptz not null default now(),
  updated_by     text
);
insert into public.request_limit_settings (id) values (1) on conflict (id) do nothing;
alter table public.request_limit_settings enable row level security;
-- [의도적] 정책 없음 — 아래 함수만 쓴다.
revoke all on table public.request_limit_settings from anon, authenticated;

-- 계정·DB 연결(pid)마다 지금 구간의 요청 수 한 줄. 서버가 다시 켜지면 비어도 되는 숫자라 기록을 남기지 않는 표로 둔다.
-- 계정당 한 줄이면 같은 계정의 동시 요청이 그 줄 잠금에서 줄을 서고, 오래 기다린 요청은 세지 않고 통과해
-- 동시에 몰아 보내는 쪽이 오히려 덜 세졌다(2026-10-29 리뷰). 한 연결은 한 번에 한 요청만 처리하므로 연결마다 나누면
-- 잠금 다툼이 없고, 계정의 요청 수는 그 계정 줄들을 더해 본다.
create unlogged table if not exists public.request_limit_counters (
  key          text        not null,
  pid          int         not null default 0,
  window_start timestamptz not null,
  hits         int         not null,
  primary key (key, pid)
);
-- 옛 판(계정당 한 줄)이면 바꾼다 — 잠깐 쓰는 숫자뿐이라 비워도 된다.
do $$
begin
  if not exists (select 1 from information_schema.columns
                  where table_schema = 'public' and table_name = 'request_limit_counters' and column_name = 'pid') then
    truncate public.request_limit_counters;
    alter table public.request_limit_counters add column pid int not null default 0;
    alter table public.request_limit_counters drop constraint request_limit_counters_pkey;
    alter table public.request_limit_counters add primary key (key, pid);
  end if;
end $$;
alter table public.request_limit_counters enable row level security;
revoke all on table public.request_limit_counters from anon, authenticated;

-- 제한 중인(또는 막 풀린) 계정. 콘솔이 "제한 중" 목록을 이 표에서 읽고, 해제는 줄을 지운다.
create table if not exists public.request_limit_blocks (
  key           text        primary key,
  account_id    uuid,
  ip            text,
  hits          int         not null,
  blocked_at    timestamptz not null default now(),
  blocked_until timestamptz not null
);
alter table public.request_limit_blocks enable row level security;
revoke all on table public.request_limit_blocks from anon, authenticated;

create or replace function public.ts_request_limit_check()
returns void
language plpgsql
security definer
set search_path = public
-- 기준을 넘는 순간 같은 계정의 여러 요청이 제한 줄을 함께 쓰면 줄을 선다. 오래 기다리면 그 요청은 세기를 건너뛴다 —
-- 시간 초과 취소는 아래 exception 이 못 잡아 요청째 실패하지만, 잠금 대기 실패(55P03)는 잡힌다.
set lock_timeout = '100ms'
as $$
declare
  v_settings public.request_limit_settings%rowtype;
  v_role     text;
  v_uid      uuid;
  v_key      text;
  v_hits     int;
  v_until    timestamptz;
  v_bucket   timestamptz;
begin
  begin
    select * into v_settings from public.request_limit_settings where id = 1;
    if not found or not v_settings.enabled then return; end if;

    -- security definer 안에서는 current_user 가 함수 주인이다 — PostgREST 가 바꿔 둔 역할은 role 설정에 남는다.
    v_role := coalesce(nullif(current_setting('role', true), 'none'), '');
    if v_role not in ('anon', 'authenticated') then return; end if;

    -- 로그인 전 요청은 세지 않는다(2026-10-29 사용자 결정) — IP 하나를 여럿이 나눠 쓰면 한 사람 때문에 모두 막히고,
    -- 게스트 복구가 막히면 SDK 가 빈 새 계정으로 들어가 진행이 사라진 것처럼 보인다. 로그인 전 함수는 게스트 복구 3종뿐이다.
    -- 다시 IP 로 세려면 x-forwarded-for 앞쪽은 앱이 적어 보낸 값이 남으니 쓰지 말고 cf-connecting-ip 를 쓴다(같은 날 실측).
    v_uid := auth.uid();
    if v_uid is null then return; end if;
    v_key := v_uid::text;

    -- 결제 지급 완료 기록은 세지도 막지도 않는다 — 막히면 지급은 됐는데 기록이 빠져, 같은 결제가 다시 오면
    -- 한 번 더 지급될 수 있다(2026-10-29 리뷰). 결제 확인 엣지 함수도 같은 이유로 제한에서 뺐다.
    if current_setting('request.path', true) = '/rpc/ts_mark_purchase_granted' then return; end if;

    select b.blocked_until into v_until from public.request_limit_blocks b where b.key = v_key;

    if (v_until is null or v_until <= now())
       and coalesce(current_setting('request.method', true), 'GET') not in ('GET', 'HEAD') then
      -- 구간은 시계에 맞춘 칸(기준 시간 단위로 자른 시각)이다. 연결마다 "첫 요청 시각"에서 구간을 따로 시작하면
      -- 줄마다 비우는 때가 어긋나 합계가 실제의 절반 근처로 줄었다(2026-10-29 리뷰) — 모든 줄이 같은 칸을 쓴다.
      v_bucket := to_timestamp(floor(extract(epoch from now()) / v_settings.window_seconds) * v_settings.window_seconds);
      insert into public.request_limit_counters as c (key, pid, window_start, hits)
      values (v_key, pg_backend_pid(), v_bucket, 1)
      on conflict (key, pid) do update set
        hits         = case when c.window_start < v_bucket then 1 else c.hits + 1 end,
        window_start = v_bucket;

      -- 계정의 요청 수 = 이 계정의 연결별 줄 중 지금 칸의 합. 남의 줄은 읽기만 하므로 기다리지 않는다.
      select coalesce(sum(c.hits), 0) into v_hits
        from public.request_limit_counters c
       where c.key = v_key and c.window_start = v_bucket;

      if v_hits > v_settings.max_requests then
        insert into public.request_limit_blocks (key, account_id, hits, blocked_at, blocked_until)
        values (v_key, v_uid, v_hits, now(), now() + make_interval(mins => v_settings.block_minutes))
        on conflict (key) do update set
          hits = excluded.hits, blocked_at = excluded.blocked_at, blocked_until = excluded.blocked_until;
        -- 이 계정의 줄을 비운다 — 남겨 두면 풀리자마자 지난 숫자로 다시 막힌다. 지금 다른 요청이 잡고 있는 줄은
        -- 기다리지 않고 건너뛴다(기다리면 잠금 대기 실패로 위 제한 기록까지 되돌려진다).
        delete from public.request_limit_counters
         where ctid in (select c.ctid from public.request_limit_counters c where c.key = v_key for update skip locked);
      end if;
      v_until := null;  -- 기준을 넘은 이 요청은 통과시킨다(위 [주의]).
    end if;
  exception when others then
    return;  -- 제한 기능이 고장 나도 게임은 막지 않는다.
  end;

  if v_until is not null and v_until > now() then
    raise sqlstate 'PGRST' using
      message = json_build_object(
        'code', 'request_limited',
        'message', 'request_limited: 요청이 너무 많아 잠시 제한되었습니다.',
        -- details·hint 는 글자만 된다 — 묶음을 넣으면 PostgREST 가 해석을 못 해 429 대신 500 을 낸다(2026-10-29 실측).
        'details', 'until ' || to_char(v_until at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
        'hint', null)::text,
      detail = json_build_object(
        'status', 429,
        'headers', json_build_object('Retry-After', greatest(1, ceil(extract(epoch from v_until - now())))::int::text))::text;
  end if;
end;
$$;

-- 엣지 함수가 사용자 로그인으로 불러 위 검사를 태우는 빈 함수(공통 구간 request-limit).
create or replace function public.ts_request_limit_ping()
returns void
language plpgsql
as $$
begin
end;
$$;

comment on function public.ts_request_limit_check() is
  '요청 제한 사전 검사(pgrst.db_pre_request). 꺼져 있으면 아무것도 안 한다. anon·authenticated·service_role 실행 권한 필수.';
comment on function public.ts_request_limit_ping() is
  '엣지 함수가 요청 제한 검사를 태우려고 부르는 빈 함수.';

revoke all on function public.ts_request_limit_check() from public, anon, authenticated;
revoke all on function public.ts_request_limit_ping()  from public, anon, authenticated;
grant execute on function public.ts_request_limit_check() to anon, authenticated, service_role;
grant execute on function public.ts_request_limit_ping()  to authenticated;

create or replace function public.ts_request_limit_cleanup()
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.request_limit_counters where window_start < now() - interval '1 hour';
  delete from public.request_limit_blocks where blocked_until < now() - interval '1 day';
$$;

comment on function public.ts_request_limit_cleanup() is '지난 요청 수·풀린 지 하루 지난 제한 기록을 지운다. cron 이 10분마다 부른다.';
revoke all on function public.ts_request_limit_cleanup() from public, anon, authenticated;

do $$
begin
  if exists (select 1 from pg_extension where extname = 'pg_cron') then
    perform cron.unschedule('ts_request_limit_cleanup') where exists (select 1 from cron.job where jobname = 'ts_request_limit_cleanup');
    perform cron.schedule('ts_request_limit_cleanup', '*/10 * * * *', $cron$ select public.ts_request_limit_cleanup(); $cron$);
  end if;
end $$;

-- 콘솔 "설정 › 요청 제한"
create or replace function public.ts_admin_request_limit_settings_get()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object('enabled', s.enabled, 'window_seconds', s.window_seconds, 'max_requests', s.max_requests,
                            'block_minutes', s.block_minutes, 'updated_at', s.updated_at, 'updated_by', s.updated_by)
    from public.request_limit_settings s where s.id = 1;
$$;

-- 범위는 표의 check 와 admin-api 가 같은 값으로 막는다.
create or replace function public.ts_admin_request_limit_settings_update(
  p_enabled boolean, p_window_seconds int, p_max_requests int, p_block_minutes int, p_by text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
begin
  update public.request_limit_settings
     set enabled = coalesce(p_enabled, enabled), window_seconds = p_window_seconds, max_requests = p_max_requests,
         block_minutes = p_block_minutes, updated_at = now(), updated_by = p_by
   where id = 1;
  -- 끄면 걸려 있던 제한도 함께 푼다 — 꺼 둔 동안 남은 기록이 다시 켤 때 갑자기 걸리지 않게.
  -- where true: 콘솔 서버의 DB 연결은 조건 없는 delete 를 막는다(safeupdate).
  if p_enabled is false then
    delete from public.request_limit_blocks where true;
    delete from public.request_limit_counters where true;
  end if;
  return public.ts_admin_request_limit_settings_get();
end;
$$;

-- 지금 제한 중인 계정과 닉네임.
create or replace function public.ts_admin_request_limit_blocks()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'key', b.key, 'account_id', b.account_id, 'ip', b.ip, 'display_name', d.display_name,
           'hits', b.hits, 'blocked_at', b.blocked_at, 'blocked_until', b.blocked_until)
           order by b.blocked_at desc), '[]'::jsonb)
    from (select * from public.request_limit_blocks where blocked_until > now() order by blocked_at desc limit 200) b
    left join public.display_names d on d.account_id = b.account_id;
$$;

create or replace function public.ts_admin_request_limit_unblock(p_key text)
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.request_limit_blocks where key = p_key;
  delete from public.request_limit_counters where key = p_key;
$$;

comment on function public.ts_admin_request_limit_settings_get() is '요청 제한 설정 조회. service_role 전용.';
comment on function public.ts_admin_request_limit_settings_update(boolean, int, int, int, text) is
  '요청 제한 설정 변경. 끄면 걸려 있던 제한도 푼다. service_role 전용.';
comment on function public.ts_admin_request_limit_blocks() is '지금 요청 제한 중인 계정(최근 200). service_role 전용.';
comment on function public.ts_admin_request_limit_unblock(text) is '요청 제한 해제. service_role 전용.';

revoke all on function public.ts_admin_request_limit_settings_get()                          from public, anon, authenticated;
revoke all on function public.ts_admin_request_limit_settings_update(boolean, int, int, int, text) from public, anon, authenticated;
revoke all on function public.ts_admin_request_limit_blocks()                                from public, anon, authenticated;
revoke all on function public.ts_admin_request_limit_unblock(text)                           from public, anon, authenticated;
grant execute on function public.ts_admin_request_limit_settings_get()                          to service_role;
grant execute on function public.ts_admin_request_limit_settings_update(boolean, int, int, int, text) to service_role;
grant execute on function public.ts_admin_request_limit_blocks()                                to service_role;
grant execute on function public.ts_admin_request_limit_unblock(text)                           to service_role;

-- 모든 데이터 요청 앞에 검사를 끼운다. 꺼 두면(기본) 설정 한 줄만 읽고 지나간다.
-- 비상시(검사 함수가 망가져 모든 요청이 실패할 때) 떼어 내기:
--   alter role authenticator reset pgrst.db_pre_request; notify pgrst, 'reload config';
alter role authenticator set pgrst.db_pre_request = 'public.ts_request_limit_check';
notify pgrst, 'reload config';

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

-- 세이브 행이 지워질 때 지워지기 직전의 모든 값을 남긴다. 없으면 콘솔의 "이 변경 전으로"가 삭제를 사이에 둔 기록에서
-- 삭제 뒤 안 바뀐 칸을 새 행의 기본값으로 둔 채, 실제로 있었던 적 없는 섞인 상태를 만든다.
CREATE OR REPLACE FUNCTION public.ts_log_user_data_delete()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  -- 계정 삭제가 세이브 행을 함께 지울 때(ON DELETE CASCADE)는 남기지 않는다 — 지워지는 계정을 가리키는 기록을 넣으면
  -- 외래 키 위반으로 계정 삭제 자체가 실패한다. 그때는 부모 행이 이미 지워져 있어 아래 확인에 걸린다.
  IF OLD.account_id IS NOT NULL AND EXISTS (SELECT 1 FROM auth.users u WHERE u.id = OLD.account_id) THEN
    INSERT INTO public.user_data_logs (account_id, diff, source)
    VALUES (OLD.account_id,
            to_jsonb(OLD) - 'id' - 'account_id' - 'user_id' - 'server_id' - 'created_at' - 'updated_at',
            coalesce(nullif(current_setting('app.log_source', true), ''), 'row_deleted'));
  END IF;
  RETURN OLD;
END;
$$;

REVOKE ALL ON FUNCTION public.ts_log_user_data_delete() FROM public, anon, authenticated;

DROP TRIGGER IF EXISTS trg_user_data_log_delete ON public.user_data;
CREATE TRIGGER trg_user_data_log_delete
AFTER DELETE ON public.user_data
FOR EACH ROW EXECUTE FUNCTION public.ts_log_user_data_delete();

-- ── 7일 초과 로그 자동 정리 (pg_cron 필요) ───────────────────────────────
SELECT cron.schedule(
  'cleanup-user-data-logs',
  '0 3 * * *',
  $$DELETE FROM public.user_data_logs WHERE created_at < now() - interval '7 days'$$
);

-- ── 운영 콘솔 — 플레이어 유저 데이터 보기·고치기 (service_role 전용) ───────────
-- 게임이 컬럼을 늘리면 콘솔이 따라오도록 컬럼 목록은 표에서 읽는다. 아래 다섯 칸은 SDK 가
-- 관리하는 칸이라 콘솔에서 고치지 않는다.
create or replace function public.ts_admin_user_data_columns()
returns jsonb
language sql
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
    'column_name', c.column_name, 'data_type', c.data_type,
    'is_nullable', c.is_nullable, 'column_default', c.column_default
  ) order by c.ordinal_position), '[]'::jsonb)
  from information_schema.columns c
  where c.table_schema = 'public' and c.table_name = 'user_data'
    and c.column_name not in ('id','account_id','user_id','server_id','updated_at');
$$;

create or replace function public.ts_admin_user_data_get(p_account_id uuid)
returns jsonb
language sql
security definer
set search_path = public
as $$
  select to_jsonb(d) from public.user_data d where d.account_id = p_account_id;
$$;

create or replace function public.ts_admin_user_data_logs(p_account_id uuid, p_limit integer default 20)
returns jsonb
language sql
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
    'id', l.id, 'diff', l.diff, 'source', l.source, 'created_at', l.created_at
  ) order by l.created_at desc), '[]'::jsonb)
  from (
    select * from public.user_data_logs
    where account_id = p_account_id
    order by created_at desc
    limit p_limit
  ) l;
$$;

-- 보낸 칸만 바꾼다. 값의 형 변환은 jsonb_populate_record 가 표의 컬럼 형에 맞춰 한다.
-- 변경 로그에는 'admin:<운영자>' 로 남는다.
create or replace function public.ts_admin_user_data_update(p_account_id uuid, p_patch jsonb, p_operator text default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_cols text[];
  v_key  text;
  v_set  text := '';
  v_n    int;
  v_row  jsonb;
begin
  if p_patch is null or jsonb_typeof(p_patch) <> 'object' or p_patch = '{}'::jsonb then
    raise exception 'patch_required';
  end if;

  select array_agg(c.column_name) into v_cols
  from information_schema.columns c
  where c.table_schema = 'public' and c.table_name = 'user_data'
    and c.column_name not in ('id','account_id','user_id','server_id','updated_at');

  for v_key in select jsonb_object_keys(p_patch) loop
    if not (v_key = any(v_cols)) then
      raise exception 'user_data_column_not_allowed: %', v_key;
    end if;
    v_set := v_set || format(', %1$I = d.%1$I', v_key);
  end loop;

  perform set_config(
    'app.log_source',
    'admin' || case when p_operator is not null and p_operator <> '' then ':' || p_operator else '' end,
    true
  );

  begin
    execute format($f$
      update public.user_data u
      set updated_at = now() %s
      from jsonb_populate_record(null::public.user_data, $1) d
      where u.account_id = $2
    $f$, v_set)
    using p_patch, p_account_id;
  exception when check_violation then
    raise exception 'protected_field_violation: %', sqlerrm;
  end;

  get diagnostics v_n = row_count;
  if v_n = 0 then
    raise exception 'user_data_not_found';
  end if;

  select to_jsonb(d) into v_row from public.user_data d where d.account_id = p_account_id;
  return v_row;
end;
$$;

-- 데이터 관리 화면의 플레이어 찾기. 유저 데이터 행이 있는 플레이어만, 최근 저장 순 20명.
create or replace function public.ts_admin_search_user_data_players(p_search text default null, p_mode text default 'nickname')
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_search text := coalesce(p_search, '');
  v_rows jsonb;
begin
  if p_mode = 'id' then
    select coalesce(jsonb_agg(to_jsonb(t)), '[]'::jsonb) into v_rows
    from (
      select ud.account_id,
             coalesce(dn.display_name, 'Player_' || left(ud.account_id::text, 8)) as display_name,
             ud.updated_at
      from public.user_data ud
      left join public.display_names dn on dn.account_id = ud.account_id
      where v_search = '' or ud.account_id::text ilike v_search || '%'
      order by ud.updated_at desc
      limit 20
    ) t;
  else
    select coalesce(jsonb_agg(to_jsonb(t)), '[]'::jsonb) into v_rows
    from (
      select ud.account_id,
             coalesce(dn.display_name, 'Player_' || left(ud.account_id::text, 8)) as display_name,
             ud.updated_at
      from public.user_data ud
      left join public.display_names dn on dn.account_id = ud.account_id
      where v_search = '' or dn.display_name ilike '%' || v_search || '%'
      order by ud.updated_at desc
      limit 20
    ) t;
  end if;
  return v_rows;
end;
$$;

revoke all on function public.ts_admin_user_data_columns()                       from public, anon, authenticated;
revoke all on function public.ts_admin_user_data_get(uuid)                       from public, anon, authenticated;
revoke all on function public.ts_admin_user_data_logs(uuid, integer)             from public, anon, authenticated;
revoke all on function public.ts_admin_user_data_update(uuid, jsonb, text)       from public, anon, authenticated;
revoke all on function public.ts_admin_search_user_data_players(text, text)      from public, anon, authenticated;
grant execute on function public.ts_admin_user_data_columns()                    to service_role;
grant execute on function public.ts_admin_user_data_get(uuid)                    to service_role;
grant execute on function public.ts_admin_user_data_logs(uuid, integer)          to service_role;
grant execute on function public.ts_admin_user_data_update(uuid, jsonb, text)    to service_role;
grant execute on function public.ts_admin_search_user_data_players(text, text)   to service_role;

-- ── 세이브 되돌리기 — 어떤 변경 직전 상태로 ────────────────────────────────
-- 로그는 바뀌기 "전" 값만 남긴다. 지금 값에서 최신 로그부터 그 로그까지 거꾸로 덧씌우면 그 변경 직전 상태가 된다.
-- 로그는 7일만 남으므로(cleanup-user-data-logs) 되돌릴 수 있는 범위도 7일이다.
create or replace function public.ts_user_data_state_before_log(p_account_id uuid, p_log_id bigint)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_state jsonb;
  v_diff  jsonb;
begin
  if not exists (select 1 from public.user_data_logs where id = p_log_id and account_id = p_account_id) then
    raise exception 'user_data_log_not_found';
  end if;
  select to_jsonb(d) into v_state from public.user_data d where d.account_id = p_account_id;
  if v_state is null then
    raise exception 'user_data_not_found';
  end if;
  for v_diff in
    select l.diff from public.user_data_logs l
    where l.account_id = p_account_id and l.id >= p_log_id
    order by l.id desc
  loop
    v_state := v_state || v_diff;
  end loop;
  return v_state;
end;
$$;

-- 되돌리면 바뀌는 칸만: [{column, current, restored}]
create or replace function public.ts_admin_user_data_restore_preview(p_account_id uuid, p_log_id bigint)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_cur      jsonb;
  v_restored jsonb := public.ts_user_data_state_before_log(p_account_id, p_log_id);
begin
  select to_jsonb(d) into v_cur from public.user_data d where d.account_id = p_account_id;
  return (
    select coalesce(jsonb_agg(jsonb_build_object(
             'column', c.column_name, 'data_type', c.data_type,
             'current', v_cur -> c.column_name, 'restored', v_restored -> c.column_name
           ) order by c.ordinal_position), '[]'::jsonb)
    from information_schema.columns c
    where c.table_schema = 'public' and c.table_name = 'user_data'
      and c.column_name not in ('id','account_id','user_id','server_id','updated_at')
      and (v_cur -> c.column_name) is distinct from (v_restored -> c.column_name)
  );
end;
$$;

-- 고른 칸만 그 변경 직전 값으로 쓴다. 칸 검사·로그 출처(admin:<운영자>)는 ts_admin_user_data_update 와 같다.
create or replace function public.ts_admin_user_data_restore(
  p_account_id uuid,
  p_log_id     bigint,
  p_columns    text[],
  p_operator   text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_restored jsonb := public.ts_user_data_state_before_log(p_account_id, p_log_id);
  v_patch    jsonb;
begin
  select jsonb_object_agg(c, v_restored -> c) into v_patch
  from unnest(coalesce(p_columns, '{}'::text[])) c
  where v_restored ? c;
  if v_patch is null then
    raise exception 'patch_required';
  end if;
  return public.ts_admin_user_data_update(p_account_id, v_patch, p_operator);
end;
$$;

revoke all on function public.ts_user_data_state_before_log(uuid, bigint)                  from public, anon, authenticated;
revoke all on function public.ts_admin_user_data_restore_preview(uuid, bigint)             from public, anon, authenticated;
revoke all on function public.ts_admin_user_data_restore(uuid, bigint, text[], text)       from public, anon, authenticated;
grant execute on function public.ts_admin_user_data_restore_preview(uuid, bigint)          to service_role;
grant execute on function public.ts_admin_user_data_restore(uuid, bigint, text[], text)    to service_role;


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

-- 콘솔의 아이템 고르기 칸은 표시 이름만 보인다 — 이름이 같은 아이템이 둘이면 우편·쿠폰·매치 보상에서 엉뚱한 것을
-- 고를 수 있어 막는다. 대소문자·앞뒤 공백은 같은 이름으로 본다. 빈 이름은 화면이 키를 대신 보이므로 겹쳐도 된다.
create unique index if not exists game_items_display_name_uniq
  on public.game_items (lower(btrim(display_name))) where btrim(display_name) <> '';

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
-- 받는 플랫폼(user_profiles.platform 값). null 이면 플랫폼을 가리지 않는다.
alter table public.mail_batches add column if not exists platform        text;
-- 이 시각까지 나중에 대상이 된 플레이어에게도 보낸다(ts_deliver_open_mail_batches). null 이면 보낸 순간의 대상만.
alter table public.mail_batches add column if not exists open_until      timestamptz;
-- 운영자가 안 받은 우편을 거둔 시각·거둔 통수(ts_admin_mail_batch_recall).
alter table public.mail_batches add column if not exists recalled_at     timestamptz;
alter table public.mail_batches add column if not exists recalled_count  int not null default 0;

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
-- (batch_id, user_id) — 발송별 조회와 ts_deliver_open_mail_batches 의 "이미 받았나" 확인을 함께 받친다.
drop index if exists public.mails_batch_id_idx;
create index if not exists mails_batch_user_idx on public.mails (batch_id, user_id) where batch_id is not null;

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
drop function if exists public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb);
drop function if exists public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb,text);

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
  p_localized            jsonb   default null,
  p_platform             text    default null,
  p_open_for_new         boolean default false
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
  -- 플레이어를 직접 고른 발송에는 플랫폼을 걸지 않는다 — 고른 사람이 곧 대상이다.
  v_platform    text := case when p_target_mode = 'players' then null else nullif(btrim(p_platform), '') end;
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
    (target_mode, server_id, title, content, items, expires_at, created_by, category, localized, platform, open_until)
  values
    (p_target_mode,
     case when p_target_mode = 'server' then p_server_id else null end,
     p_title, coalesce(p_content, ''),
     p_items, p_expires_at, p_created_by, v_category, p_localized, v_platform,
     -- 고른 플레이어에게 보내는 우편은 나중에 들어올 사람이 없다.
     case when p_open_for_new and p_target_mode <> 'players' then p_expires_at end)
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
    )
    and (v_platform is null or p.platform = v_platform);

  get diagnostics v_count = row_count;
  update public.mail_batches set recipient_count = v_count where id = v_batch_id;

  return jsonb_build_object('batch_id', v_batch_id, 'recipient_count', v_count);
end;
$$;

comment on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb,text,boolean) is
  '어드민 우편 발송. 대상 all/server/players(account_id jsonb 배열) 해석(탈퇴 제외) → 수신자별 mails INSERT + mail_batches 스냅샷. items는 game_items 검증(우회 플래그). p_category 비었거나 null이면 default. p_localized는 언어별 제목·본문 오버라이드(없으면 base fallback). p_platform 은 all/server 대상을 그 플랫폼으로 좁힌다(players 대상에는 무시). p_open_for_new 면 수령 기한까지 나중에 대상이 된 플레이어에게도 ts_deliver_open_mail_batches 가 보낸다(players 대상에는 무시).';

-- ---------------------------------------------------------------------------
-- ts_deliver_open_mail_batches — "나중에 들어온 플레이어도 받기"로 보낸 우편을 아직 없는 대상에게 넣는다(크론 매분).
-- 받았는지는 그 사람에게 이 발송의 우편 행이 있는지로 본다. 플레이어가 지운 우편도 숨김일 뿐 행은 남고,
-- 행이 지워지는 건 수령 기한(= open_until)이 지나 보관 기간까지 끝난 뒤라 다시 보내지 않는다.
-- 탈퇴 후 재가입: 게스트는 새 user_id 라 다시 받는다(새로 가입한 사람과 구분할 수 없다). 구글·애플은 user_id 가
-- 그 계정 번호라 그대로여서, 옛 우편 행이 남아 있는 동안은 다시 받지 않는다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_deliver_open_mail_batches()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  b       record;
  v_n     int;
  v_total int := 0;
begin
  -- 거두기(ts_admin_mail_batch_recall)가 잡고 있는 발송은 이번 회차에 건너뛴다 — 겹치면 방금 넣은 우편이 거둬지지 않고 남는다.
  for b in
    select * from public.mail_batches
    where open_until is not null and open_until > now()
    for update skip locked
  loop
    insert into public.mails
      (account_id, user_id, sender_type, title, content,
       expires_at, created_at, items, batch_id, category, localized)
    select p.account_id, p.user_id, 'system',
           b.title, b.content, b.expires_at, now(), b.items, b.id, b.category, b.localized
    from public.user_profiles p
    where p.account_id is not null
      and p.withdrawn_at is null
      and (b.target_mode = 'all' or (b.target_mode = 'server' and p.server_id = b.server_id))
      and (b.platform is null or p.platform = b.platform)
      -- user_id 로 본다 — 재인증으로 account_id 가 바뀌어도 그대로고, 연동으로 바뀌면 ts_ensure_my_profile 이
      -- 그 계정의 우편도 함께 옮긴다. (batch_id, user_id) 색인으로 받친다.
      and not exists (
        select 1 from public.mails m where m.batch_id = b.id and m.user_id = p.user_id
      );
    get diagnostics v_n = row_count;
    if v_n > 0 then
      update public.mail_batches set recipient_count = recipient_count + v_n where id = b.id;
      v_total := v_total + v_n;
    end if;
  end loop;
  return v_total;
end;
$$;

comment on function public.ts_deliver_open_mail_batches() is
  '수령 기한이 남은 "나중에 들어온 플레이어도 받기" 우편을 아직 받지 않은 대상에게 넣는다. 크론 전용.';
revoke all on function public.ts_deliver_open_mail_batches() from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- 발송 실수 되돌리기 — 운영 콘솔 "우편 내역" 전용(service_role)
--   close  : "나중에 들어온 플레이어도 받기" 배달만 멈춘다. 이미 간 우편은 그대로.
--   recall : 아직 받지 않은 우편을 지우고 배달도 멈춘다. 받은 우편·지급된 아이템은 되돌리지 않는다.
--            배달을 함께 멈추지 않으면 지운 사람에게 1분 뒤 다시 간다(받았는지를 우편 행으로 보므로).
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_mail_batch_close(p_batch_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not exists (select 1 from public.mail_batches where id = p_batch_id) then
    raise exception 'mail_batch_not_found';
  end if;
  -- least(null, now()) 는 now() 라, 기간 우편이 아니던 발송까지 기간 우편으로 표시된다 — 열린 것만 닫는다.
  update public.mail_batches
     set open_until = least(open_until, now())
   where id = p_batch_id and open_until is not null;
end;
$$;

create or replace function public.ts_admin_mail_batch_recall(p_batch_id uuid)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  v_n int;
begin
  -- 배달 크론과 겹치지 않게 발송 행을 먼저 잡는다.
  perform 1 from public.mail_batches where id = p_batch_id for update;
  if not found then
    raise exception 'mail_batch_not_found';
  end if;

  update public.mail_batches set open_until = least(open_until, now()) where id = p_batch_id and open_until is not null;

  delete from public.mails
   where batch_id = p_batch_id
     and items_claimed_at is null;
  get diagnostics v_n = row_count;

  update public.mail_batches
     set recalled_at = now(),
         recalled_count = recalled_count + v_n
   where id = p_batch_id;
  return v_n;
end;
$$;

comment on function public.ts_admin_mail_batch_close(uuid) is '"나중에 들어온 플레이어도 받기" 배달을 멈춘다. service_role 전용.';
comment on function public.ts_admin_mail_batch_recall(uuid) is '발송의 안 받은 우편을 지우고 배달을 멈춘다. 지운 통수를 돌려준다. service_role 전용.';
revoke all on function public.ts_admin_mail_batch_close(uuid)  from public, anon, authenticated;
revoke all on function public.ts_admin_mail_batch_recall(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_mail_batch_close(uuid)  to service_role;
grant execute on function public.ts_admin_mail_batch_recall(uuid) to service_role;

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
  if btrim(coalesce(p_display_name, '')) <> '' and exists (
       select 1 from public.game_items g
        where lower(btrim(g.display_name)) = lower(btrim(p_display_name)) and g.key <> btrim(p_key)) then
    raise exception 'item_name_taken';
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
-- ts_admin_list_game_items — 카탈로그 조회
-- ---------------------------------------------------------------------------
-- game_items 는 쓰기·읽기 모두 RPC 경유다. 운영 도구가 owner 로 접속하면 직접 SELECT 가
-- 되지만, secret key(service_role)로 붙는 도구는 이 테이블에 SELECT 가 없어 조회가 막힌다.
create or replace function public.ts_admin_list_game_items(p_search text default '')
returns table (key text, display_name text, created_at timestamptz)
language sql
stable
security definer
set search_path = public
as $$
  select g.key, g.display_name, g.created_at
    from public.game_items g
   where coalesce(p_search, '') = ''
      or g.key ilike '%' || p_search || '%'
      or g.display_name ilike '%' || p_search || '%'
   order by g.created_at desc
$$;

comment on function public.ts_admin_list_game_items(text) is
  '아이템 카탈로그 조회(어드민 전용). 검색어가 비면 전체.';

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
-- 어드민 우편 조회 — TrueBase.Admin(Retool 대체 도구) 전용. mails·mail_batches는
-- service_role에도 테이블 grant가 없으므로(19_20절 회수) RPC로 감싼다.
-- ---------------------------------------------------------------------------

create or replace function public.ts_admin_list_mail_batches(
  p_search    text default null,
  p_category  text default null,
  p_page      int  default 1,
  p_page_size int  default 20
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_search   text := nullif(btrim(coalesce(p_search, '')), '');
  v_category text := nullif(btrim(coalesce(p_category, '')), '');
  v_page     int  := greatest(1, coalesce(p_page, 1));
  v_size     int  := least(100, greatest(1, coalesce(p_page_size, 20)));
  v_total    int;
  v_rows     jsonb;
begin
  -- count(*) over()를 페이지 쿼리 안에서 함께 구해 테이블을 두 번 스캔하지 않는다.
  select coalesce(jsonb_agg(to_jsonb(t) - 'total'), '[]'::jsonb),
         coalesce(max(t.total), 0)
    into v_rows, v_total
  from (
    select
      b.id, b.target_mode, gs.server_code, b.platform, b.open_until, b.recalled_at, b.title, b.items,
      b.recipient_count, b.category, b.expires_at, b.created_by, b.created_at,
      count(*) over() as total
    from public.mail_batches b
    left join public.game_servers gs on gs.id = b.server_id
    where (v_search is null or b.title ilike '%' || v_search || '%')
      and (v_category is null or b.category = v_category)
    order by b.created_at desc
    limit v_size offset (v_page - 1) * v_size
  ) t;

  return jsonb_build_object('rows', v_rows, 'total', v_total, 'pageSize', v_size);
end;
$$;

create or replace function public.ts_admin_mail_batch_detail(p_batch_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_batch jsonb;
  v_stats jsonb;
begin
  select jsonb_build_object(
    'id', b.id, 'target_mode', b.target_mode, 'server_code', gs.server_code,
    'title', b.title, 'items', b.items, 'recipient_count', b.recipient_count,
    'category', b.category, 'expires_at', b.expires_at, 'created_by', b.created_by, 'created_at', b.created_at,
    'open_until', b.open_until, 'recalled_at', b.recalled_at, 'recalled_count', b.recalled_count
  )
  into v_batch
  from public.mail_batches b
  left join public.game_servers gs on gs.id = b.server_id
  where b.id = p_batch_id;

  if v_batch is null then
    raise exception 'mail_batch_not_found';
  end if;

  select jsonb_build_object(
    'claimed_count', count(*) filter (where m.items_claimed_at is not null),
    'deleted_count', count(*) filter (where m.deleted_at is not null),
    'unclaimed_count', count(*) filter (where m.items_claimed_at is null)
  )
  into v_stats
  from public.mails m
  where m.batch_id = p_batch_id;

  return jsonb_build_object('batch', v_batch, 'stats', v_stats);
end;
$$;

create or replace function public.ts_admin_search_mails(
  p_search     text        default null,
  p_status     text        default null,
  p_category   text        default null,
  p_start_date date        default null,
  p_end_date   date        default null,
  p_batch_id   uuid        default null,
  p_page       int         default 1,
  p_page_size  int         default 20
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_search   text := nullif(btrim(coalesce(p_search, '')), '');
  v_category text := nullif(btrim(coalesce(p_category, '')), '');
  v_page     int  := greatest(1, coalesce(p_page, 1));
  v_size     int  := least(100, greatest(1, coalesce(p_page_size, 20)));
  v_total    int;
  v_rows     jsonb;
begin
  -- count(*) over()를 페이지 쿼리 안에서 함께 구해 mails 테이블을 두 번 스캔하지 않는다.
  select coalesce(jsonb_agg(to_jsonb(t) - 'total'), '[]'::jsonb),
         coalesce(max(t.total), 0)
    into v_rows, v_total
  from (
    select
      m.id, m.account_id, m.user_id, d.display_name, m.category, m.title, m.content,
      m.items, m.items_claimed_at, m.deleted_at, m.expires_at, m.created_at, gs.server_code,
      count(*) over() as total
    from public.mails m
    left join public.display_names d on d.account_id = m.account_id
    left join public.user_profiles p on p.account_id = m.account_id
    left join public.game_servers gs on gs.id = p.server_id
    where (v_search is null or d.display_name ilike '%' || v_search || '%' or m.account_id::text ilike '%' || v_search || '%')
      and (v_category is null or m.category = v_category)
      and (p_status is null or p_status = '전체'
           or (p_status = '미수령' and m.deleted_at is null and m.items_claimed_at is null)
           or (p_status = '수령'   and m.deleted_at is null and m.items_claimed_at is not null)
           or (p_status = '삭제'   and m.deleted_at is not null))
      -- 콘솔의 날짜는 게임 시간대 날짜다. ::timestamptz 는 세션 시간대(UTC) 자정이라 그 나라 새벽 기록이 전날로 빠진다.
      and (p_start_date is null or m.created_at >= (p_start_date::timestamp at time zone public.ts_game_time_zone()))
      and (p_end_date   is null or m.created_at <  ((p_end_date + 1)::timestamp at time zone public.ts_game_time_zone()))
      and (p_batch_id is null or m.batch_id = p_batch_id)
    order by m.created_at desc
    limit v_size offset (v_page - 1) * v_size
  ) t;

  return jsonb_build_object('rows', v_rows, 'total', v_total, 'pageSize', v_size);
end;
$$;

comment on function public.ts_admin_list_mail_batches(text,text,int,int) is '발송 단위(배치) 목록. service_role 전용.';
comment on function public.ts_admin_mail_batch_detail(uuid) is '배치 1건 + 수령·삭제 통계. service_role 전용.';
comment on function public.ts_admin_search_mails(text,text,text,date,date,uuid,int,int) is '개별 우편 검색·목록. service_role 전용.';

-- ---------------------------------------------------------------------------
-- 어드민 RPC EXECUTE — service_role 전용(클라이언트 금지)
-- ---------------------------------------------------------------------------
revoke all on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb,text,boolean) from public, anon, authenticated;
grant execute on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,jsonb,text,boolean,text,jsonb,text,boolean) to service_role;
revoke all on function public.ts_admin_upsert_game_item(text,text) from public, anon, authenticated;
grant execute on function public.ts_admin_upsert_game_item(text,text) to service_role;
revoke all on function public.ts_admin_delete_game_item(text) from public, anon, authenticated;
grant execute on function public.ts_admin_delete_game_item(text) to service_role;
revoke all on function public.ts_admin_list_game_items(text) from public, anon, authenticated;
grant execute on function public.ts_admin_list_game_items(text) to service_role;
revoke all on function public.ts_admin_count_recipients(text,uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_count_recipients(text,uuid) to service_role;
revoke all on function public.ts_admin_list_mail_batches(text,text,int,int) from public, anon, authenticated;
grant execute on function public.ts_admin_list_mail_batches(text,text,int,int) to service_role;
revoke all on function public.ts_admin_mail_batch_detail(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_mail_batch_detail(uuid) to service_role;
revoke all on function public.ts_admin_search_mails(text,text,text,date,date,uuid,int,int) from public, anon, authenticated;
grant execute on function public.ts_admin_search_mails(text,text,text,date,date,uuid,int,int) to service_role;

-- ---------------------------------------------------------------------------
-- 우편 설정(6절 mail_settings) — 운영 콘솔 "우편 설정" 전용 조회·수정
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_mail_settings_get()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
           'expired_retention_days', c.expired_retention_days,
           'updated_at',             c.updated_at,
           'updated_by',             c.updated_by)
    from public.ts_mail_settings() c;
$$;

comment on function public.ts_admin_mail_settings_get() is
  '우편 설정 조회. service_role 전용.';

revoke all on function public.ts_admin_mail_settings_get() from public, anon, authenticated;
grant execute on function public.ts_admin_mail_settings_get() to service_role;

create or replace function public.ts_admin_mail_settings_update(
  p_expired_retention_days int,
  p_by                     text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare v_row public.mail_settings%rowtype;
begin
  -- 테이블 CHECK 는 하한만 본다. 상한은 여기서 막는다 — 보관 일수를 늘리면 그만큼 지워지지 않은 우편이
  -- 쌓이고 목록 조회가 무거워진다. 0~30일(0은 만료 즉시 숨김). 더 키워야 하면 이 줄과 콘솔·엣지 함수를 함께 고친다.
  if p_expired_retention_days is null or p_expired_retention_days not between 0 and 30 then
    raise exception 'mail_settings_out_of_range';
  end if;

  insert into public.mail_settings (id, expired_retention_days, updated_at, updated_by)
  values (1, p_expired_retention_days, now(), p_by)
  on conflict (id) do update
    set expired_retention_days = excluded.expired_retention_days,
        updated_at             = now(),
        updated_by             = excluded.updated_by
  returning * into v_row;

  return jsonb_build_object(
           'expired_retention_days', v_row.expired_retention_days,
           'updated_at',             v_row.updated_at,
           'updated_by',             v_row.updated_by);
end;
$$;

comment on function public.ts_admin_mail_settings_update(int, text) is
  '우편 설정 수정(만료 우편 보관 일수 0~30). 범위를 벗어나면 mail_settings_out_of_range. service_role 전용.';

revoke all on function public.ts_admin_mail_settings_update(int, text) from public, anon, authenticated;
grant execute on function public.ts_admin_mail_settings_update(int, text) to service_role;

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
  repeat_tz     text not null default public.ts_game_time_zone(),
  repeat_unit   text not null default 'day' check (repeat_unit in ('day','week','month')),
  repeat_dow    int null,                               -- week: 0=일 ~ 6=토
  repeat_dom    int null,                               -- month: 1~31 (말일 초과 시 말일)
  platform      text null,                              -- 받는 플랫폼. null 이면 가리지 않는다
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
alter table public.mail_schedules add column if not exists repeat_tz    text not null default public.ts_game_time_zone();
alter table public.mail_schedules alter column repeat_tz set default public.ts_game_time_zone();
alter table public.mail_schedules add column if not exists repeat_unit  text not null default 'day';
alter table public.mail_schedules add column if not exists repeat_dow   int;
alter table public.mail_schedules add column if not exists repeat_dom   int;
alter table public.mail_schedules add column if not exists last_run_at  timestamptz;
alter table public.mail_schedules add column if not exists run_count    int not null default 0;
alter table public.mail_schedules add column if not exists created_by   text;
alter table public.mail_schedules add column if not exists platform     text;
alter table public.mail_schedules add column if not exists open_for_new boolean not null default false;

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
  p_repeat_tz    text default null,
  p_repeat_unit  text default 'day',
  p_repeat_dow   int  default null,
  p_repeat_dom   int  default null
)
returns timestamptz
language plpgsql
stable
as $$
declare
  v_tz        text := coalesce(p_repeat_tz, public.ts_game_time_zone());
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
        s.localized,
        s.platform,
        s.open_for_new and s.schedule_type <> 'repeat'
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
-- 어드민 예약 관리 — TrueBase.Admin(Retool 대체 도구) 전용. mail_schedules는
-- service_role에도 테이블 grant가 없으므로(19_20절 회수) RPC로 감싼다.
-- ---------------------------------------------------------------------------

drop function if exists public.ts_admin_create_mail_schedule(text,text,text,int,jsonb,uuid,text,jsonb,jsonb,text,timestamptz,time,text,int,int,text);
drop function if exists public.ts_admin_create_mail_schedule(text,text,text,int,jsonb,uuid,text,jsonb,jsonb,text,timestamptz,time,text,int,int,text,text);

create or replace function public.ts_admin_create_mail_schedule(
  p_schedule_type text,
  p_target_mode   text,
  p_title         text,
  p_expires_days  int,
  p_account_ids   jsonb       default null,
  p_server_id     uuid        default null,
  p_content       text        default '',
  p_items         jsonb       default null,
  p_localized     jsonb       default null,
  p_category      text        default 'default',
  p_scheduled_at  timestamptz default null,
  p_repeat_time   time        default null,
  p_repeat_unit   text        default 'day',
  p_repeat_dow    int         default null,
  p_repeat_dom    int         default null,
  p_created_by    text        default null,
  p_platform      text        default null,
  p_open_for_new  boolean     default false
)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id       uuid;
  v_next     timestamptz;
  v_category text;
  v_item     jsonb;
  v_key      text;
begin
  if p_schedule_type not in ('scheduled', 'repeat') then
    raise exception 'invalid_schedule_type: %', p_schedule_type;
  end if;
  if p_target_mode not in ('all', 'server', 'players') then
    raise exception 'invalid_target_mode: %', p_target_mode;
  end if;
  if p_title is null or btrim(p_title) = '' then
    raise exception 'title_empty';
  end if;
  if p_target_mode = 'server' and p_server_id is null then
    raise exception 'server_id_required';
  end if;
  if p_target_mode = 'players' and (p_account_ids is null or jsonb_array_length(p_account_ids) = 0) then
    raise exception 'account_ids_required';
  end if;

  v_category := coalesce(nullif(btrim(p_category), ''), 'default');

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
      if not exists (select 1 from public.game_items gi where gi.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  v_next := public.ts_mail_schedule_next_run(
    p_schedule_type, p_scheduled_at, p_repeat_time, public.ts_game_time_zone(), p_repeat_unit, p_repeat_dow, p_repeat_dom);

  -- 반복 시각은 만들 때의 게임 시간대로 고정한다 — 나중에 게임 시간대를 바꿔도 이미 만든 예약은 그대로 돈다.
  insert into public.mail_schedules
    (schedule_type, target_mode, server_id, account_ids, title, content, items, localized,
     category, expires_days, scheduled_at, repeat_time, repeat_tz, repeat_unit, repeat_dow, repeat_dom,
     next_run_at, created_by, platform, open_for_new)
  values
    (p_schedule_type, p_target_mode, p_server_id, p_account_ids, p_title, coalesce(p_content, ''), p_items, p_localized,
     v_category, coalesce(p_expires_days, 7), p_scheduled_at, p_repeat_time, public.ts_game_time_zone(), coalesce(p_repeat_unit, 'day'), p_repeat_dow, p_repeat_dom,
     v_next, p_created_by,
     case when p_target_mode = 'players' then null else nullif(btrim(p_platform), '') end,
     -- 반복 발송은 회차마다 열려 있어, 나중에 가입한 사람이 수령 기한 안의 지난 회차를 한꺼번에 받게 된다.
     coalesce(p_open_for_new, false) and p_target_mode <> 'players' and p_schedule_type <> 'repeat')
  returning id into v_id;

  return v_id;
end;
$$;

create or replace function public.ts_admin_list_mail_schedules()
returns jsonb
language sql
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(row_to_json(t) order by t.next_run_at asc), '[]'::jsonb)
  from (
    select
      s.id, s.schedule_type, s.target_mode, gs.server_code, s.title, s.items, s.category,
      s.scheduled_at, s.repeat_time, s.repeat_tz, s.repeat_unit, s.repeat_dow, s.repeat_dom,
      s.next_run_at, s.is_active, s.run_count, s.created_by, s.platform, s.open_for_new
    from public.mail_schedules s
    left join public.game_servers gs on gs.id = s.server_id
  ) t;
$$;

create or replace function public.ts_admin_set_mail_schedule_active(p_id uuid, p_is_active boolean)
returns void
language sql
security definer
set search_path = public
as $$
  update public.mail_schedules set is_active = p_is_active where id = p_id;
$$;

create or replace function public.ts_admin_delete_mail_schedule(p_id uuid)
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.mail_schedules where id = p_id;
$$;

comment on function public.ts_admin_create_mail_schedule(text,text,text,int,jsonb,uuid,text,jsonb,jsonb,text,timestamptz,time,text,int,int,text,text,boolean) is '우편 예약·반복 발송 등록. service_role 전용.';
comment on function public.ts_admin_list_mail_schedules() is '우편 예약 목록. service_role 전용.';
comment on function public.ts_admin_set_mail_schedule_active(uuid,boolean) is '우편 예약 활성/비활성 전환. service_role 전용.';
comment on function public.ts_admin_delete_mail_schedule(uuid) is '우편 예약 삭제. service_role 전용.';

revoke all on function public.ts_admin_create_mail_schedule(text,text,text,int,jsonb,uuid,text,jsonb,jsonb,text,timestamptz,time,text,int,int,text,text,boolean) from public, anon, authenticated;
grant execute on function public.ts_admin_create_mail_schedule(text,text,text,int,jsonb,uuid,text,jsonb,jsonb,text,timestamptz,time,text,int,int,text,text,boolean) to service_role;
revoke all on function public.ts_admin_list_mail_schedules() from public, anon, authenticated;
grant execute on function public.ts_admin_list_mail_schedules() to service_role;
revoke all on function public.ts_admin_set_mail_schedule_active(uuid,boolean) from public, anon, authenticated;
grant execute on function public.ts_admin_set_mail_schedule_active(uuid,boolean) to service_role;
revoke all on function public.ts_admin_delete_mail_schedule(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_delete_mail_schedule(uuid) to service_role;

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

-- "나중에 들어온 플레이어도 받기" 우편 배달(12절 ts_deliver_open_mail_batches) — 매분
do $$
begin
  if exists (select 1 from cron.job where jobname = 'deliver-open-mail-batches') then
    perform cron.unschedule('deliver-open-mail-batches');
  end if;
end $$;
select cron.schedule(
  'deliver-open-mail-batches',
  '* * * * *',
  'select public.ts_deliver_open_mail_batches()'
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
  rotation_tz             text        not null default public.ts_game_time_zone(),
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
alter table public.leaderboard_tables alter column rotation_tz set default public.ts_game_time_zone();

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
  p_tz             text        default null,
  p_from           timestamptz default now()
)
returns timestamptz
language plpgsql
stable
as $$
declare
  v_tz     text        := coalesce(nullif(btrim(p_tz), ''), public.ts_game_time_zone());
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
  p_tz              text        default null,
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
     v_anchor, coalesce(nullif(btrim(p_tz), ''), public.ts_game_time_zone()), v_next, p_ends_at, coalesce(p_is_active, true))
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

-- ---------------------------------------------------------------------------
-- 어드민 조회 — TrueBase.Admin(Retool 대체 도구) 전용. information_schema는
-- PostgREST에 노출되지 않고, 순위·통계는 동적 컬럼 계산이 필요해 RPC로 감싼다.
-- ---------------------------------------------------------------------------

create or replace function public.ts_admin_list_leaderboards()
returns jsonb
language sql
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(row_to_json(t) order by t.code), '[]'::jsonb)
  from (
    select
      lt.code, lt.display_name, lt.scope, lt.record_type, lt.sort_type, lt.rotation,
      lt.rotation_period_seconds, lt.rotation_anchor_at, lt.rotation_tz, lt.rotation_count,
      lt.next_rotation_at, lt.ends_at, lt.is_active,
      (lt.ends_at is not null and now() >= lt.ends_at) as is_ended,
      coalesce((
        select count(*) from public.leaderboard_scores s
        where s.table_code = lt.code and s.rotation_count = lt.rotation_count
      ), 0) as current_entries,
      coalesce((
        select string_agg(c.column_name, ',' order by c.sort_order)
        from public.leaderboard_table_columns c where c.table_code = lt.code
      ), '') as columns
    from public.leaderboard_tables lt
  ) t;
$$;

create or replace function public.ts_admin_leaderboard_columns(p_code text)
returns jsonb
language sql
security definer
set search_path = public
as $$
  select jsonb_build_object(
    'all', coalesce((
      select jsonb_agg(jsonb_build_object(
        'column_name', c.column_name, 'data_type', c.data_type,
        'is_nullable', c.is_nullable, 'column_default', c.column_default
      ) order by c.column_name)
      from information_schema.columns c
      where c.table_schema = 'public' and c.table_name = 'leaderboard_scores'
        and c.column_name not in (
          'id','table_code','rotation_count','account_id','user_id','server_id',
          'score','score_achieved_at','first_recorded_at','score_count','updated_at')
    ), '[]'::jsonb),
    'attached', coalesce((
      select jsonb_agg(jsonb_build_object('column_name', lc.column_name, 'sort_order', lc.sort_order) order by lc.sort_order)
      from public.leaderboard_table_columns lc
      where lc.table_code = p_code
    ), '[]'::jsonb)
  );
$$;

create or replace function public.ts_admin_leaderboard_scores(
  p_code           text,
  p_rotation_count int  default null,
  p_server_id      uuid default null,
  p_search         text default null,
  p_page           int  default 1,
  p_page_size      int  default 20
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  t         public.leaderboard_tables%rowtype;
  v_rot     int;
  v_max_rot int;
  v_desc    boolean;
  v_search  text := nullif(btrim(coalesce(p_search, '')), '');
  v_page    int  := greatest(1, coalesce(p_page, 1));
  v_size    int  := least(100, greatest(1, coalesce(p_page_size, 20)));
  v_cols    text[];
  v_data    text := '''{}''::jsonb';
  v_col     text;
  v_rows    jsonb;
begin
  select * into t from public.leaderboard_tables where code = p_code;
  if not found then
    raise exception 'leaderboard_table_not_found';
  end if;

  v_rot  := coalesce(p_rotation_count, t.rotation_count);
  v_desc := (t.sort_type = 'desc');
  select max(rotation_count) into v_max_rot from public.leaderboard_scores where table_code = t.code;

  v_cols := public.ts_leaderboard_columns_of(t.code);
  if array_length(v_cols, 1) is not null then
    v_data := 'jsonb_build_object(';
    foreach v_col in array v_cols loop
      v_data := v_data || format('%L, s.%I,', v_col, v_col);
    end loop;
    v_data := left(v_data, length(v_data) - 1) || ')';
  end if;

  execute format($f$
    with ranked as (
      select s.account_id, s.user_id, s.score, s.score_achieved_at, s.score_count, gs.server_code,
             dn.display_name, %s as data,
             rank() over (order by (case when $6 then -s.score else s.score end) asc, s.score_achieved_at asc) as rnk
      from public.leaderboard_scores s
      left join public.display_names dn on dn.account_id = s.account_id
      left join public.game_servers gs on gs.id = s.server_id
      where s.table_code = $1
        and s.rotation_count = $2
        and ($3 is null or s.server_id = $3)
        and ($4 is null or dn.display_name ilike '%%' || $4 || '%%' or s.account_id::text ilike '%%' || $4 || '%%')
    )
    select jsonb_build_object(
      'total', (select count(*) from ranked),
      'rows', coalesce((
        select jsonb_agg(
          (jsonb_build_object(
            'rank', r.rnk, 'account_id', r.account_id, 'user_id', r.user_id,
            'display_name', r.display_name, 'score', r.score,
            'score_achieved_at', r.score_achieved_at, 'score_count', r.score_count,
            'server_code', r.server_code
          ) || r.data)
          order by r.rnk)
        from (select * from ranked order by rnk limit $5 offset (($7 - 1) * $5)) r
      ), '[]'::jsonb)
    )
  $f$, v_data)
  into v_rows
  using t.code, v_rot, p_server_id, v_search, v_size, v_desc, v_page;

  return v_rows || jsonb_build_object(
    'pageSize', v_size, 'rotationCount', v_rot, 'maxRotation', v_max_rot,
    'scope', t.scope, 'dataColumns', to_jsonb(v_cols));
end;
$$;

comment on function public.ts_admin_list_leaderboards() is '리더보드 정의 전체 목록(운영용, 종료·비활성 포함). service_role 전용.';
comment on function public.ts_admin_leaderboard_columns(text) is '리더보드 플레이어 데이터 필드 전체 카탈로그 + 이 리더보드에 등록된 것. service_role 전용.';
comment on function public.ts_admin_leaderboard_scores(text,int,uuid,text,int,int) is '리더보드 순위·기록 검색(운영용, 종료된 리더보드도 조회 가능). service_role 전용.';

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
revoke all on function public.ts_admin_list_leaderboards() from public, anon, authenticated;
grant execute on function public.ts_admin_list_leaderboards() to service_role;
revoke all on function public.ts_admin_leaderboard_columns(text) from public, anon, authenticated;
grant execute on function public.ts_admin_leaderboard_columns(text) to service_role;
revoke all on function public.ts_admin_leaderboard_scores(text,int,uuid,text,int,int) from public, anon, authenticated;
grant execute on function public.ts_admin_leaderboard_scores(text,int,uuid,text,int,int) to service_role;

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

  -- 이미 있는 코드로 "만들기"를 받으면 게시 때 기존 리더보드 설정을 덮어쓴다. 삭제 대기 중인 코드만 다시 만들 수 있다.
  if p_feature = 'leaderboard_table' and p_action = 'create' then
    if exists (select 1 from public.ts_schema_draft
                where status = 'pending' and feature = 'leaderboard_table'
                  and action in ('create','update') and object_name = p_object_name)
       or (exists (select 1 from public.leaderboard_tables where code = p_object_name)
           and not exists (select 1 from public.ts_schema_draft
                            where status = 'pending' and feature = 'leaderboard_table'
                              and action = 'delete' and object_name = p_object_name)) then
      raise exception 'leaderboard_code_exists: %', p_object_name;
    end if;
  end if;

  -- 필드 추가는 게시 때 같은 이름의 칸이 있으면 실패하고, 그러면 함께 대기 중인 다른 변경까지 게시가 막힌다.
  -- 게시 때 검사(admin_add_user_data_column·ts_admin_leaderboard_add_column)와 같은 기준으로 담을 때 거절한다.
  if p_action = 'add' and p_feature in ('leaderboard_field', 'user_data_field') then
    if p_object_name !~ '^[A-Za-z_][A-Za-z0-9_]*$' then
      raise exception 'field_name_invalid: %', p_object_name;
    end if;
    if p_object_name = any (case p_feature
         when 'leaderboard_field' then array['id','table_code','rotation_count','account_id','user_id','server_id',
                                             'score','score_achieved_at','first_recorded_at','score_count','updated_at']
         else array['id','user_id','account_id','server_id','updated_at'] end) then
      raise exception 'field_name_reserved: %', p_object_name;
    end if;
    if exists (select 1 from public.ts_schema_draft
                where status = 'pending' and feature = p_feature and action = 'add' and object_name = p_object_name)
       or (exists (select 1 from information_schema.columns
                    where table_schema = 'public'
                      and table_name = case p_feature when 'leaderboard_field' then 'leaderboard_scores' else 'user_data' end
                      and column_name = p_object_name)
           and not exists (select 1 from public.ts_schema_draft
                            where status = 'pending' and feature = p_feature
                              and action = 'drop' and object_name = p_object_name)) then
      raise exception 'field_name_exists: %', p_object_name;
    end if;
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
      -- 대기 중에 다른 경로로 같은 코드가 생겼을 수 있다. 같은 묶음의 삭제는 정렬상 먼저 돌아 이미 지워져 있다.
      if exists (select 1 from public.leaderboard_tables where code = d.params->>'code') then
        raise exception 'leaderboard_code_exists: %', d.params->>'code';
      end if;
      perform public.ts_admin_leaderboard_upsert_table(
        d.params->>'code', d.params->>'display_name',
        coalesce(d.params->>'scope','global'), coalesce(d.params->>'record_type','highest'),
        coalesce(d.params->>'sort_type','desc'), coalesce(d.params->>'rotation','none'),
        (d.params->>'period_seconds')::int, (d.params->>'anchor_at')::timestamptz,
        coalesce(d.params->>'tz', public.ts_game_time_zone()), (d.params->>'ends_at')::timestamptz,
        coalesce((d.params->>'is_active')::boolean, true));

    elsif v_key = 'leaderboard_table.update' then
      select to_jsonb(t) into v_before from public.leaderboard_tables t where code = d.params->>'code';
      perform public.ts_admin_leaderboard_upsert_table(
        d.params->>'code', d.params->>'display_name',
        coalesce(d.params->>'scope','global'), coalesce(d.params->>'record_type','highest'),
        coalesce(d.params->>'sort_type','desc'), coalesce(d.params->>'rotation','none'),
        (d.params->>'period_seconds')::int, (d.params->>'anchor_at')::timestamptz,
        -- 고칠 때 시간대를 안 보냈으면 그 리더보드의 시간대를 지킨다 — 게임 시간대로 바뀌면 회차 경계가 옮겨 간다.
        coalesce(d.params->>'tz', v_before->>'rotation_tz', public.ts_game_time_zone()), (d.params->>'ends_at')::timestamptz,
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
declare v_pending_action text;
begin
  -- 이미 있는 키를 "새로 만들기"로 받으면 그 값이 빈 값으로 덮인다. 삭제 대기 중인 키만 새로 만들 수 있다.
  select action into v_pending_action from public.ts_schema_draft
   where status='pending' and feature='remote_config' and object_name=p_key order by id limit 1;
  if v_pending_action in ('add','update')
     or (v_pending_action is null and exists(select 1 from public.remote_config where key=p_key)) then
    raise exception 'remote_config_key_exists: %', p_key;
  end if;
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

-- 기존 C# 타입 힌트가 새 값에도 맞는지. 종류만 보면 int 힌트에 1.5 가, 날짜 힌트에 아무 글자나 들어와도 힌트가 남아
-- 클래스 생성기가 틀린 타입을 만든다.
create or replace function public._ts_config_hint_fits(p_value jsonb, p_hint text)
returns boolean language plpgsql stable set search_path = public as $$
declare
  v_text text := p_value #>> '{}';
  v_num  numeric;
begin
  case jsonb_typeof(p_value)
    when 'number' then
      if p_hint in ('float', 'double') then return true; end if;
      v_num := v_text::numeric;
      if v_num <> trunc(v_num) then return false; end if;
      return case p_hint
        when 'short' then v_num between -32768 and 32767
        when 'int'   then v_num between -2147483648 and 2147483647
        when 'long'  then v_num between -9223372036854775808 and 9223372036854775807
        when 'ulong' then v_num between 0 and 18446744073709551615
        else false end;
    when 'boolean' then
      return p_hint = 'bool';
    when 'string' then
      if p_hint = 'string' then return true; end if;
      -- 'now'·'today' 같은 Postgres 특수 글자나 '1/2/2024' 같은 지역 형식은 날짜로 읽히지만 C# 이 다르게 읽거나 못 읽는다.
      -- ISO 모양(날짜 yyyy-MM-dd, 시각 HH:mm)으로 시작하는 것만 본다.
      begin
        case p_hint
          when 'DateTimeOffset', 'DateTime', 'DateOnly' then
            if v_text !~ '^\d{4}-\d{2}-\d{2}' then return false; end if;
            if p_hint = 'DateOnly' then perform v_text::date; else perform v_text::timestamptz; end if;
          when 'TimeOnly' then
            if v_text !~ '^([01]\d|2[0-3]):\d{2}' then return false; end if;
            perform v_text::time;
          else return false;
        end case;
        return true;
      exception when others then
        return false;
      end;
    else
      return false;
  end case;
end; $$;

revoke all on function public._ts_config_hint_fits(jsonb, text) from public, anon, authenticated;

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
    -- 콘솔은 값의 종류(숫자·문자·참거짓)까지만 알고 int·long 같은 C# 타입은 모른다. 값을 고칠 때 힌트를
    -- 지우면 클래스 생성기가 long 을 다시 "애매함"으로 본다 — 새 값과 종류가 맞으면 기존 힌트를 남긴다.
    meta := coalesce(vj->'__meta','{}'::jsonb);
    if not coalesce(public._ts_config_hint_fits(p_item_value, meta->>p_item_key), false) then
      meta := meta - p_item_key;
    end if;
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

-- ---------------------------------------------------------------------------
-- 운영자 계정 — 운영 도구에 들어올 수 있는 사람 목록
-- ---------------------------------------------------------------------------
-- 게임 플레이어(auth.users)와 별개다. 운영자는 구글로 신원만 증명하고, 들어올 수 있는지는
-- 이 표가 정한다. 마스터 1명은 운영 도구의 환경변수에 있어 여기서 지울 수 없다 — 표가
-- 비거나 잘못 지워져도 잠기지 않게 하려는 비상구다.
-- 삭제 대신 disabled_at 으로 끄는 이유는 누가 언제 빠졌는지가 남아야 하기 때문이다.
create table if not exists public.ts_admin_operators (
  email        text primary key,
  display_name text not null default '',
  disabled_at  timestamptz,
  created_at   timestamptz not null default now(),
  created_by   text
);

alter table public.ts_admin_operators enable row level security;
-- [의도적] 정책 없음 — service_role 전용. 클라이언트는 이 표에 닿지 않는다.

comment on table public.ts_admin_operators is
  '운영 도구 접근이 허용된 계정. 이메일은 소문자로 저장한다. 마스터는 여기 없다(도구 환경변수).';

create or replace function public.ts_admin_list_operators()
returns table (email text, display_name text, disabled_at timestamptz, created_at timestamptz, created_by text)
language sql
stable
security definer
set search_path = public
as $$
  select o.email, o.display_name, o.disabled_at, o.created_at, o.created_by
    from public.ts_admin_operators o
   order by o.disabled_at nulls first, o.created_at
$$;

create or replace function public.ts_admin_upsert_operator(
  p_email        text,
  p_display_name text default '',
  p_by           text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_email text := lower(btrim(coalesce(p_email, '')));
begin
  -- 형식이 틀린 값이 들어오면 영영 로그인 못 하는 유령 항목이 남는다.
  if v_email !~ '^[^@[:space:]]+@[^@[:space:]]+\.[^@[:space:]]+$' then
    raise exception 'operator_email_invalid';
  end if;

  insert into public.ts_admin_operators (email, display_name, created_by)
  values (v_email, coalesce(p_display_name, ''), p_by)
  on conflict (email) do update
    set display_name = excluded.display_name,
        disabled_at  = null;   -- 다시 추가하면 되살아난다
end;
$$;

create or replace function public.ts_admin_set_operator_disabled(
  p_email    text,
  p_disabled boolean,
  p_by       text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_email text := lower(btrim(coalesce(p_email, '')));
begin
  update public.ts_admin_operators
     set disabled_at = case when p_disabled then now() else null end,
         created_by  = coalesce(created_by, p_by)
   where email = v_email;

  if not found then
    raise exception 'operator_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_list_operators() is '운영자 목록(어드민 전용).';
comment on function public.ts_admin_upsert_operator(text,text,text) is
  '운영자 추가·이름 수정. 비활성 상태였으면 다시 활성화된다.';
comment on function public.ts_admin_set_operator_disabled(text,boolean,text) is
  '운영자 비활성화·복구. 행은 남기고 disabled_at 만 바꾼다.';

revoke all on function public.ts_admin_list_operators()                             from public, anon, authenticated;
revoke all on function public.ts_admin_upsert_operator(text,text,text)              from public, anon, authenticated;
revoke all on function public.ts_admin_set_operator_disabled(text,boolean,text)     from public, anon, authenticated;
grant execute on function public.ts_admin_list_operators()                          to service_role;
grant execute on function public.ts_admin_upsert_operator(text,text,text)           to service_role;
grant execute on function public.ts_admin_set_operator_disabled(text,boolean,text)  to service_role;

-- 비밀번호로 들어오는 운영자의 비밀번호. 해시는 admin-api 가 만들고 검사한다 — DB 는 보관만 한다.
-- 구글로만 들어오는 운영자는 행이 없다.
create table if not exists public.ts_admin_passwords (
  email         text        primary key,
  password_hash text        not null,
  updated_at    timestamptz not null default now(),
  updated_by    text
);

alter table public.ts_admin_passwords enable row level security;
-- [의도적] 정책 없음 — service_role 전용.
revoke all on table public.ts_admin_passwords from anon, authenticated;

comment on table public.ts_admin_passwords is
  '운영자 비밀번호 해시. admin-api 전용. 운영자를 지우면 함께 지워진다(ts_admin_delete_operator).';

-- 예전 판의 "이메일로 비밀번호 재설정" 코드 표. 재설정을 마스터가 직접 정하는 방식으로 바꾸며 읽는 곳이 없어졌다.
drop table if exists public.ts_admin_reset_codes;

-- 비활성화와 달리 기록을 남기지 않고 지운다 — 잘못 추가한 이메일을 치울 때 쓴다.
create or replace function public.ts_admin_delete_operator(p_email text)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_email text := lower(btrim(coalesce(p_email, '')));
begin
  -- 되돌릴 수 없으니 먼저 비활성화한 운영자만 지운다. 화면도 막지만 서버가 다시 막는다.
  if not exists (select 1 from public.ts_admin_operators where email = v_email) then
    raise exception 'operator_not_found';
  end if;
  if exists (select 1 from public.ts_admin_operators where email = v_email and disabled_at is null) then
    raise exception 'operator_not_disabled';
  end if;

  delete from public.ts_admin_passwords where email = v_email;
  delete from public.ts_admin_operators where email = v_email;
end;
$$;

comment on function public.ts_admin_delete_operator(text) is '운영자 삭제(비밀번호 포함). service_role 전용.';

revoke all on function public.ts_admin_delete_operator(text) from public, anon, authenticated;
grant execute on function public.ts_admin_delete_operator(text) to service_role;

-- 콘솔 첫 화면의 숫자. 매출은 환불을 뺀다 — 플레이어별 누적 결제액(total_paid_krw)과 같은 기준이다.
create or replace function public.ts_admin_dashboard_stats()
returns jsonb
language sql
security definer
set search_path = public
as $$
  with bounds as (
    select
      (date_trunc('day', now() at time zone public.ts_game_time_zone()) at time zone public.ts_game_time_zone()) as today_start,
      now() - interval '7 days' as seven_days_ago
  )
  select jsonb_build_object(
    'total_players', (select count(*) from auth.users),
    -- 오늘 신규·활동은 통계 화면과 같은 기록(일별 접속)에서 센다 — 두 화면의 "오늘" 숫자가 어긋나지 않게.
    'new_today', (select count(*) from public.user_daily_activity where day = (now() at time zone public.ts_game_time_zone())::date and is_new),
    'new_7d', (select count(*) from auth.users, bounds where created_at >= bounds.seven_days_ago),
    'active_today', (select count(*) from public.user_daily_activity where day = (now() at time zone public.ts_game_time_zone())::date),
    'banned_now', (select count(*) from auth.users where banned_until is not null and banned_until > now()),
    'purchases_today', (select count(*) from public.purchases, bounds where verified_at >= bounds.today_start and refunded_at is null and is_test is not true and is_free is not true),
    'revenue_today_krw', (select coalesce(sum(price_amount_krw), 0) from public.purchases, bounds where verified_at >= bounds.today_start and refunded_at is null and is_test is not true and is_free is not true),
    'revenue_7d_krw', (select coalesce(sum(price_amount_krw), 0) from public.purchases, bounds where verified_at >= bounds.seven_days_ago and refunded_at is null and is_test is not true and is_free is not true),
    'revenue_total_krw', (select coalesce(sum(price_amount_krw), 0) from public.purchases where refunded_at is null and is_test is not true and is_free is not true)
  );
$$;

comment on function public.ts_admin_dashboard_stats() is '콘솔 첫 화면 숫자(가입·활동·차단·매출). 날짜는 게임 시간대 기준. service_role 전용.';

revoke all on function public.ts_admin_dashboard_stats() from public, anon, authenticated;
grant execute on function public.ts_admin_dashboard_stats() to service_role;

-- 콘솔 첫 화면의 동시접속자. now 는 기록을 기다리지 않고 지금 센다. 그래프 점은 기간에 따라 묶어(묶음 안 최댓값)
-- 24시간 288개·7일 168개·30일 180개 안팎으로 맞춘다 — 기록 그대로면 30일이 8천 점이 넘어 그래프가 뭉개진다.
-- 90일이 넘는 기간은 5분 기록이 없으므로 일별 요약(ccu_daily)의 하루 최고치로 그린다.
create or replace function public.ts_admin_ccu(p_hours integer)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with params as (
    select least(24 * 3650, greatest(1, coalesce(p_hours, 24))) as hours
  ),
  bucket as (
    select hours,
           case when hours <= 24 then 5 when hours <= 24 * 7 then 60 when hours <= 24 * 90 then 240 else 1440 end as minutes
    from params
  ),
  daily as (
    select (d.day::timestamp at time zone public.ts_game_time_zone()) as at, d.peak as total, d.peak_at
    from public.ccu_daily d, bucket b
    where b.minutes = 1440
      and d.day >= ((now() - make_interval(hours => b.hours)) at time zone public.ts_game_time_zone())::date
  ),
  series as (
    select date_bin(make_interval(mins => b.minutes), c.at, timestamptz '2000-01-01') as at, max(c.total) as total
    from public.ccu_snapshots c, bucket b
    where b.minutes < 1440 and c.at >= now() - make_interval(hours => b.hours)
    group by 1
    union all
    select at, total from daily
  ),
  peak as (
    select at, total from (
      select c.at, c.total
      from public.ccu_snapshots c, bucket b
      where b.minutes < 1440 and c.at >= now() - make_interval(hours => b.hours)
      union all
      select peak_at, total from daily
    ) x
    order by total desc, at desc
    limit 1
  )
  select jsonb_build_object(
    'now', public.ts_ccu_now(),
    'bucket_minutes', (select minutes from bucket),
    'series', coalesce((select jsonb_agg(jsonb_build_object('at', at, 'total', total) order by at) from series), '[]'::jsonb),
    'peak', (select jsonb_build_object('at', at, 'total', total) from peak),
    'first_at', least((select min(at) from public.ccu_snapshots),
                      (select min(day)::timestamp at time zone public.ts_game_time_zone() from public.ccu_daily))
  );
$$;

-- 콘솔 "통계" 화면 — 최근 p_days 일의 일일 사용자(전체·신규·재방문·복귀)와 가입일별 리텐션.
-- 재방문 = 그날 신규가 아닌 접속자. 리텐션은 가입한 날(D0)로부터 N일째에 다시 접속한 비율이고, 아직 오지 않은 날은 null.
-- p_signup_platform: 그 플랫폼으로 가입한 계정만 센다. null 이면 전부, 'unknown' 이면 가입 플랫폼을 모르는 계정.
drop function if exists public.ts_admin_activity_stats(integer);
create or replace function public.ts_admin_activity_stats(p_days integer, p_signup_platform text default null)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with params as (
    select least(365, greatest(7, coalesce(p_days, 30))) as days, (now() at time zone public.ts_game_time_zone())::date as today
  ),
  act as (
    select a.* from public.user_daily_activity a
     where p_signup_platform is null
        or (p_signup_platform = 'unknown' and a.signup_platform is null)
        or a.signup_platform = p_signup_platform
  ),
  days as (
    select generate_series(p.today - p.days + 1, p.today, interval '1 day')::date as day from params p
  ),
  daily as (
    select d.day,
           count(a.account_id) as total,
           count(a.account_id) filter (where a.is_new) as new_users,
           count(a.account_id) filter (where a.is_comeback) as comeback
    from days d left join act a on a.day = d.day
    group by d.day
  ),
  offsets as (
    select unnest(array[1,2,3,4,5,6,7,8,9,10,11,12,13,14,21,30,45,60]) as n
  ),
  cohort_members as (
    select a.day, a.account_id
    from act a, params p
    where a.is_new and a.day >= p.today - p.days + 1
  ),
  cohorts as (
    select day, count(*) as size from cohort_members group by day
  ),
  kept as (
    select c.day, o.n,
           -- 오늘 칸은 아직 하루가 끝나지 않아 반나절 숫자다 — 확정값처럼 보이지 않게 비운다.
           case when c.day + o.n >= p.today then null
                else (select count(*) from cohort_members m
                      join public.user_daily_activity a on a.account_id = m.account_id and a.day = c.day + o.n
                      where m.day = c.day) end as kept
    from cohorts c cross join offsets o cross join params p
  )
  select jsonb_build_object(
    'today', (select today from params),
    'daily', coalesce((select jsonb_agg(jsonb_build_object(
                 'day', day, 'total', total, 'new', new_users, 'returning', total - new_users, 'comeback', comeback) order by day)
               from daily), '[]'::jsonb),
    'offsets', (select jsonb_agg(n order by n) from offsets),
    'retention', coalesce((select jsonb_agg(jsonb_build_object(
                     'day', c.day, 'size', c.size,
                     'kept', (select jsonb_agg(k.kept order by k.n) from kept k where k.day = c.day)) order by c.day desc)
                   from cohorts c), '[]'::jsonb),
    'first_day', (select min(day) from public.user_daily_activity),
    -- 이 기간에 기록이 있는 가입 플랫폼(거르기 전 기준) — 콘솔이 이것만 버튼으로 보인다. 모르는 계정은 'unknown'.
    'signup_platforms', (select coalesce(jsonb_agg(distinct coalesce(a.signup_platform, 'unknown')), '[]'::jsonb)
                           from public.user_daily_activity a, params p where a.day >= p.today - p.days + 1)
  );
$$;


-- 콘솔 "통계 › 매출" — 최근 p_days 일(게임 시간대 날짜) 결제를 가입 국가별로 더한다. 환불은 뺀다(대시보드와 같은 기준).
-- 국가는 가입할 때 IP 로 정한 user_profiles.country_code. 탈퇴로 계정이 지워진 결제(account_id 없음)는 user_id 로 잇는다.
create or replace function public.ts_admin_revenue_by_country(p_days integer)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with params as (
    select least(365, greatest(1, coalesce(p_days, 30))) as days,
           (((now() at time zone public.ts_game_time_zone())::date - (least(365, greatest(1, coalesce(p_days, 30))) - 1))::timestamp
             at time zone public.ts_game_time_zone()) as since
  ),
  paid as (
    select coalesce(nullif(prof.country_code, ''), 'unknown') as country,
           coalesce(pu.price_amount_krw, 0) as krw,
           pu.price_amount_krw is null as amount_unknown,
           coalesce(pu.account_id::text, pu.user_id) as payer
    from public.purchases pu
    cross join params p
    left join lateral (
      select pr.country_code from public.user_profiles pr
      where (pu.account_id is not null and pr.account_id = pu.account_id)
         or (pu.account_id is null and pr.user_id = pu.user_id)
      limit 1
    ) prof on true
    where pu.verified_at >= p.since and pu.refunded_at is null and pu.is_test is not true and pu.is_free is not true
  ),
  by_country as (
    select country, sum(krw)::bigint as revenue_krw, count(*) as purchases, count(distinct payer) as payers
    from paid group by country
  )
  select jsonb_build_object(
    'days', (select days from params),
    'total_krw', coalesce((select sum(krw) from paid), 0)::bigint,
    'purchases', (select count(*) from paid),
    'payers', (select count(distinct payer) from paid),
    -- 금액을 모르는 결제(원화 값이 빈 것). 합계에 0원으로 들어가 있어 화면이 따로 알린다.
    'unknown_amount', (select count(*) from paid where amount_unknown),
    'countries', coalesce((select jsonb_agg(jsonb_build_object(
                     'country', country, 'revenue_krw', revenue_krw, 'purchases', purchases, 'payers', payers)
                   order by revenue_krw desc, country) from by_country), '[]'::jsonb)
  );
$$;

comment on function public.ts_admin_revenue_by_country(integer) is '콘솔 통계 — 최근 p_days 일(게임 시간대 날짜) 결제를 가입 국가별로(금액 KRW·건수·결제자 수). 환불·시험 결제 제외. service_role 전용.';
revoke all on function public.ts_admin_revenue_by_country(integer) from public, anon, authenticated;
grant execute on function public.ts_admin_revenue_by_country(integer) to service_role;

-- 옛 "통계 › 테스트 결제" 탭의 함수 — 테스트 결제는 구매 내역의 "테스트 결제" 보기로 옮겼다(2026-10-29).
drop function if exists public.ts_admin_test_purchase_stats(integer);

comment on function public.ts_admin_activity_stats(integer, text) is '콘솔 통계 — 일일 사용자(전체·신규·재방문·복귀)와 가입일별 리텐션(D1~D14·21·30·45·60). 게임 시간대 날짜. 가입 플랫폼으로 거를 수 있다. service_role 전용.';
revoke all on function public.ts_admin_activity_stats(integer, text) from public, anon, authenticated;
grant execute on function public.ts_admin_activity_stats(integer, text) to service_role;

comment on function public.ts_admin_ccu(integer) is '콘솔 동시접속자 — 지금 접속 중(전체·서버별·플랫폼별)과 최근 p_hours 시간 추이·최고치. 90일 넘는 기간은 일별 요약으로 그린다. service_role 전용.';
revoke all on function public.ts_admin_ccu(integer) from public, anon, authenticated;
grant execute on function public.ts_admin_ccu(integer) to service_role;

-- 콘솔 "자동 작업" 화면 — pg_cron 작업마다 마지막 실행·마지막 성공·최근 24시간 실패 수.
-- cron.job_run_details 는 저절로 지워지지 않아 수십만 줄이 쌓이고 runid 말고는 색인이 없다.
-- 그래서 최근 실행 5만 건(매분 작업 셋 기준 약 11일치) 안에서만 본다 — 그보다 오래 성공이 없으면 null.
create or replace function public.ts_admin_cron_jobs()
returns jsonb
language sql
stable
security definer
set search_path = public, cron
as $$
  -- 최근 구간을 한 번만 훑어 작업별로 모은다(작업마다 다시 훑으면 작업 수만큼 느려진다).
  with agg as (
    select r.jobid,
           max(r.runid) as last_runid,
           max(r.end_time) filter (where r.status = 'succeeded') as last_ok,
           count(*) filter (where r.start_time > now() - interval '24 hours') as total,
           count(*) filter (where r.start_time > now() - interval '24 hours' and r.status = 'failed') as failed,
           (array_agg(r.return_message order by r.runid desc)
              filter (where r.start_time > now() - interval '24 hours' and r.status = 'failed'))[1] as last_failed_message
    from cron.job_run_details r
    where r.runid > (select coalesce(max(runid), 0) - 50000 from cron.job_run_details)
    group by r.jobid
  )
  select coalesce(jsonb_agg(jsonb_build_object(
           'jobname',      j.jobname,
           'schedule',     j.schedule,
           'active',       j.active,
           'last_start',   l.start_time,
           'last_end',     l.end_time,
           'last_status',  l.status,
           'last_message', l.return_message,
           'last_success', a.last_ok,
           'runs_24h',     coalesce(a.total, 0),
           'failures_24h', coalesce(a.failed, 0),
           'last_failure_message', a.last_failed_message
         ) order by j.jobname), '[]'::jsonb)
  from cron.job j
  left join agg a on a.jobid = j.jobid
  left join cron.job_run_details l on l.runid = a.last_runid;
$$;

comment on function public.ts_admin_cron_jobs() is '자동 작업 상태(마지막 실행·마지막 성공·24시간 실패 수). 운영 콘솔 "자동 작업" 화면 전용. service_role 전용.';
revoke all on function public.ts_admin_cron_jobs() from public, anon, authenticated;
grant execute on function public.ts_admin_cron_jobs() to service_role;

-- ---------------------------------------------------------------------------
-- 운영 기록 — 운영 도구에서 누가 언제 무엇을 바꿨는지
-- ---------------------------------------------------------------------------
-- 운영 도구 백엔드(admin-api)가 조회가 아닌 요청이 성공할 때마다 한 줄씩 남긴다.
-- 지우거나 고치는 함수는 두지 않는다 — 고칠 수 있는 기록은 근거가 못 된다.
create table if not exists public.ts_admin_audit_logs (
  id         bigserial   primary key,
  email      text        not null,
  action     text        not null,
  params     jsonb       not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);

create index if not exists ts_admin_audit_logs_created_idx on public.ts_admin_audit_logs (created_at desc);

alter table public.ts_admin_audit_logs enable row level security;
-- [의도적] 정책 없음 — service_role 전용. 클라이언트는 이 표에 닿지 않는다.
revoke all on table public.ts_admin_audit_logs from anon, authenticated;

comment on table public.ts_admin_audit_logs is
  '운영 도구의 변경 요청 기록. admin-api 가 성공한 변경마다 남긴다. 비밀번호 값은 남기지 않는다.';

create or replace function public.ts_admin_audit_write(
  p_email  text,
  p_action text,
  p_params jsonb default '{}'::jsonb
)
returns void
language sql
security definer
set search_path = public
as $$
  insert into public.ts_admin_audit_logs (email, action, params)
  values (lower(coalesce(p_email, '')), coalesce(p_action, ''), coalesce(p_params, '{}'::jsonb));
$$;

create or replace function public.ts_admin_audit_list(
  p_search     text default null,
  p_category   text default null,
  p_start_date date default null,
  p_end_date   date default null,
  p_page       int  default 1,
  p_page_size  int  default 50
)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_search   text := nullif(btrim(coalesce(p_search, '')), '');
  v_category text := nullif(btrim(coalesce(p_category, '')), '');
  v_page     int  := greatest(1, coalesce(p_page, 1));
  v_size     int  := least(200, greatest(1, coalesce(p_page_size, 50)));
  v_total    int;
  v_rows     jsonb;
begin
  select coalesce(jsonb_agg(to_jsonb(t) - 'total' order by t.created_at desc, t.id desc), '[]'::jsonb),
         coalesce(max(t.total), 0)
    into v_rows, v_total
  from (
    select l.id, l.email, l.action, l.params, l.created_at, count(*) over() as total
      from public.ts_admin_audit_logs l
     where (v_search is null
            or l.email ilike '%' || v_search || '%'
            or l.params::text ilike '%' || v_search || '%')
       and (v_category is null or l.action like v_category || '.%')
       -- 콘솔의 날짜는 게임 시간대 날짜다(ts_admin_search_mails 와 같은 이유).
       and (p_start_date is null or l.created_at >= (p_start_date::timestamp at time zone public.ts_game_time_zone()))
       and (p_end_date   is null or l.created_at <  ((p_end_date + 1)::timestamp at time zone public.ts_game_time_zone()))
     order by l.created_at desc, l.id desc
     limit v_size offset (v_page - 1) * v_size
  ) t;

  return jsonb_build_object('rows', v_rows, 'total', v_total, 'pageSize', v_size);
end;
$$;

comment on function public.ts_admin_audit_write(text,text,jsonb) is '운영 기록 한 줄 추가. admin-api 전용.';
comment on function public.ts_admin_audit_list(text,text,date,date,int,int) is
  '운영 기록 검색. p_category 는 action 앞부분(mails·players 등). service_role 전용.';

revoke all on function public.ts_admin_audit_write(text,text,jsonb)               from public, anon, authenticated;
revoke all on function public.ts_admin_audit_list(text,text,date,date,int,int)    from public, anon, authenticated;
grant execute on function public.ts_admin_audit_write(text,text,jsonb)            to service_role;
grant execute on function public.ts_admin_audit_list(text,text,date,date,int,int) to service_role;

-- ---------------------------------------------------------------------------
-- 콘솔 표시 — 운영 콘솔에서 이 게임을 나타내는 색·이름·아이콘
-- ---------------------------------------------------------------------------
-- 운영자가 엉뚱한 게임에서 작업하지 않도록 메뉴·상단 띠·확인 창에 게임마다 다른 색을 쓴다.
-- 셋 다 null 이면 콘솔 코드에 적힌 기본값을 쓴다(새 게임은 따로 정하지 않아도 된다).
-- icon 은 콘솔이 128px 로 줄여 보낸 그림(data URL)이다. 로그인 전 화면에서도 읽으므로 파일 저장소를 두지 않았다.
create table if not exists public.ts_admin_console_settings (
  id           int primary key default 1 check (id = 1),
  color        text check (color ~ '^#[0-9a-f]{6}$'),
  display_name text check (char_length(display_name) between 1 and 40),
  icon         text check (icon ~ '^data:image/(png|jpeg|webp);base64,' and length(icon) <= 120000),
  updated_at   timestamptz not null default now(),
  updated_by   text
);

-- 색만 있던 시절에 만든 표에 칸을 더한다.
alter table public.ts_admin_console_settings
  add column if not exists display_name text check (char_length(display_name) between 1 and 40),
  add column if not exists icon         text check (icon ~ '^data:image/(png|jpeg|webp);base64,' and length(icon) <= 120000);

insert into public.ts_admin_console_settings (id) values (1) on conflict (id) do nothing;

alter table public.ts_admin_console_settings enable row level security;
-- [의도적] 정책 없음 — service_role 전용.

comment on table public.ts_admin_console_settings is
  '운영 콘솔 표시 설정(행 하나). color=콘솔 색(#rrggbb 소문자), display_name=콘솔에 보이는 게임 이름, icon=게임 아이콘(data URL). null 이면 콘솔 기본값.';

create or replace function public.ts_admin_console_settings_get()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(
           (select jsonb_build_object('color', c.color, 'display_name', c.display_name, 'icon', c.icon,
                                      'updated_at', c.updated_at, 'updated_by', c.updated_by)
              from public.ts_admin_console_settings c where c.id = 1),
           jsonb_build_object('color', null, 'display_name', null, 'icon', null, 'updated_at', null, 'updated_by', null));
$$;

create or replace function public.ts_admin_console_identity_update(
  p_display_name text,
  p_icon         text,
  p_by           text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_name text := nullif(btrim(coalesce(p_display_name, '')), '');
  v_icon text := nullif(btrim(coalesce(p_icon, '')), '');
begin
  if v_name is not null and char_length(v_name) > 40 then
    raise exception 'console_name_too_long';
  end if;
  if v_icon is not null and (v_icon !~ '^data:image/(png|jpeg|webp);base64,' or length(v_icon) > 120000) then
    raise exception 'console_icon_invalid';
  end if;

  insert into public.ts_admin_console_settings (id, display_name, icon, updated_at, updated_by)
  values (1, v_name, v_icon, now(), p_by)
  on conflict (id) do update
    set display_name = excluded.display_name,
        icon         = excluded.icon,
        updated_at   = now(),
        updated_by   = excluded.updated_by;

  return public.ts_admin_console_settings_get();
end;
$$;

create or replace function public.ts_admin_console_settings_update(
  p_color text,
  p_by    text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare v_color text := nullif(lower(btrim(coalesce(p_color, ''))), '');
begin
  if v_color is not null and v_color !~ '^#[0-9a-f]{6}$' then
    raise exception 'console_color_invalid';
  end if;

  insert into public.ts_admin_console_settings (id, color, updated_at, updated_by)
  values (1, v_color, now(), p_by)
  on conflict (id) do update
    set color      = excluded.color,
        updated_at = now(),
        updated_by = excluded.updated_by;

  return public.ts_admin_console_settings_get();
end;
$$;

comment on function public.ts_admin_console_settings_get() is
  '콘솔 표시 설정 조회. admin-api 가 로그인 전 화면에도 색을 보여 주려고 인증 없이 부른다. service_role 전용.';
comment on function public.ts_admin_console_settings_update(text, text) is
  '콘솔 색 변경(#rrggbb, 빈 값이면 기본색으로). 형식이 틀리면 console_color_invalid. service_role 전용.';
comment on function public.ts_admin_console_identity_update(text, text, text) is
  '콘솔에 보이는 게임 이름·아이콘 변경(빈 값이면 기본값으로). 이름 40자 초과는 console_name_too_long, 그림 형식·크기가 틀리면 console_icon_invalid. service_role 전용.';

revoke all on table    public.ts_admin_console_settings from anon, authenticated;
revoke all on function public.ts_admin_console_settings_get()                       from public, anon, authenticated;
revoke all on function public.ts_admin_console_settings_update(text, text)          from public, anon, authenticated;
revoke all on function public.ts_admin_console_identity_update(text, text, text)    from public, anon, authenticated;
grant execute on function public.ts_admin_console_settings_get()                    to service_role;
grant execute on function public.ts_admin_console_settings_update(text, text)       to service_role;
grant execute on function public.ts_admin_console_identity_update(text, text, text) to service_role;

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
--   [주의] 위 사유는 예외로 끝내지 않고 PostgREST 오류와 같은 꼴의 본문 + 상태 400 을 "값으로" 돌려준다.
--          예외로 끝내면 트랜잭션째 되돌려져 요청 제한(10절)의 시도 수까지 사라지고, 틀린 코드를 수백 번 넣는
--          대입 공격을 셀 수 없다(2026-10-29 사용자 결정). 앱이 받는 응답은 예외 때와 같아 SDK 는 그대로다.
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

  -- 이 블록 안의 쓰기(이력·횟수·우편)는 실패하면 블록째 되돌려진다 — 요청 제한의 시도 수는 블록 밖이라 남는다.
  begin
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
  -- 뒤에서 예외가 나면 블록 전체가 롤백되므로 이력만 남는 일은 없다.
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
  exception when raise_exception then
    if sqlerrm not in ('coupon_not_found', 'coupon_inactive', 'coupon_expired', 'coupon_already_used', 'coupon_exhausted') then
      raise;
    end if;
    perform set_config('response.status', '400', true);
    return jsonb_build_object('code', 'P0001', 'message', sqlerrm, 'details', null, 'hint', null);
  end;
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

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_list · ts_admin_coupon_codes — 운영 콘솔 쿠폰 목록·발급 코드
-- ---------------------------------------------------------------------------
-- 라이브 DB 에만 있고 이 파일에 빠져 있던 것을 옮겨 왔다(2026-10-23) — 없으면 새 프로젝트에서 콘솔 쿠폰 화면이 안 열린다.
create or replace function public.ts_admin_coupon_list(p_search text default '')
returns jsonb
language sql
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
    'id', c.id, 'kind', c.kind, 'title', c.title, 'content', c.content,
    'category', c.category, 'items', c.items, 'localized', c.localized,
    'expires_at', c.expires_at, 'mail_expires_days', c.mail_expires_days,
    'max_uses', c.max_uses, 'used_count', c.used_count, 'is_active', c.is_active,
    'created_by', c.created_by, 'created_at', c.created_at,
    'code', (select cc.code from public.coupon_codes cc where cc.coupon_id = c.id and c.kind = 'keyword' limit 1),
    'issued_count', (select count(*) from public.coupon_codes cc where cc.coupon_id = c.id)
  ) order by c.created_at desc), '[]'::jsonb)
  from public.coupons c
  where coalesce(btrim(p_search), '') = '' or c.title ilike '%' || btrim(p_search) || '%';
$$;

-- 코드마다 사용 이력을 붙인다. 키워드 쿠폰은 코드 하나를 여러 명이 쓰므로 사용한 사람 수만큼 줄이 나온다.
-- p_limit 는 돌려주는 줄 수 상한이다 — 코드만 세면 키워드 쿠폰은 쓴 사람 수만큼(수만 줄) 나온다.
-- CSV 내려받기는 크게 넘긴다.
create or replace function public.ts_admin_coupon_codes(p_coupon_id uuid, p_limit int default 500)
returns jsonb
language sql
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
    'code', t.code, 'redeemed_at', t.redeemed_at, 'account_id', t.account_id, 'user_id', t.user_id
  ) order by t.code, t.redeemed_at), '[]'::jsonb)
  from (
    select cc.code, r.redeemed_at, r.account_id, r.user_id
      from (
        select code from public.coupon_codes where coupon_id = p_coupon_id order by code limit p_limit
      ) cc
      left join public.coupon_redemptions r on r.code = cc.code and r.coupon_id = p_coupon_id
     order by cc.code, r.redeemed_at
     limit p_limit
  ) t;
$$;

revoke all on function public.ts_admin_coupon_list(text)       from public, anon, authenticated;
revoke all on function public.ts_admin_coupon_codes(uuid, int) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_list(text)       to service_role;
grant execute on function public.ts_admin_coupon_codes(uuid, int) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_player_coupons — 플레이어 1명의 쿠폰 사용 이력(최근 순)
-- ---------------------------------------------------------------------------
-- 쿠폰을 지우면 이력도 함께 사라진다(cascade) — 여기 안 보이면 지운 쿠폰일 수 있다.
create or replace function public.ts_admin_player_coupons(p_account_id uuid, p_limit int default 100)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(to_jsonb(t) order by t.redeemed_at desc), '[]'::jsonb)
    from (
      select r.code, r.kind, c.title, r.mail_id, r.redeemed_at
        from public.coupon_redemptions r
        join public.coupons c on c.id = r.coupon_id
       where r.account_id = p_account_id
       order by r.redeemed_at desc
       limit least(500, greatest(1, coalesce(p_limit, 100)))
    ) t;
$$;

comment on function public.ts_admin_player_coupons(uuid,int) is '플레이어 1명의 쿠폰 사용 이력. service_role 전용.';
revoke all on function public.ts_admin_player_coupons(uuid,int) from public, anon, authenticated;
grant execute on function public.ts_admin_player_coupons(uuid,int) to service_role;


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
-- 운영자 공지. 보낸 사람이 없어 account_id 는 0 UUID, 닉네임은 빈 값 — 게임은 is_notice 로 구분해 표시한다.
alter table public.chat_messages add column if not exists is_notice    boolean not null default false;

-- 쓰기가 잦은 테이블이라 인덱스를 최소로 둔다.
--   커서 조회는 항상 (채널, 범위, id > 커서) 형태고, 정리 작업은 채널별 created_at 범위다.
create index if not exists chat_messages_cursor_idx  on public.chat_messages (channel_id, scope_key, id);
create index if not exists chat_messages_cleanup_idx on public.chat_messages (channel_id, created_at);

-- 일일 채팅 개수 — 날짜(게임 시간대)·채널 종류별 보낸 메시지 수. 메시지는 채널 보관 기간이 지나면 지워지므로
-- 보낼 때 세어 둔다. 운영자 공지는 세지 않는다. 지우지 않는다.
-- shard: 같은 날·종류를 8줄로 나눠 센다 — 한 줄에 모든 메시지가 +1 하면 채팅이 몰릴 때 그 줄을 기다리느라 보내기가 밀린다.
create table if not exists public.chat_daily_stats (
  day      date     not null,
  kind     text     not null,
  shard    smallint not null default 0,
  messages integer  not null default 0,
  primary key (day, kind, shard)
);
alter table public.chat_daily_stats add column if not exists shard smallint not null default 0;
do $$
begin
  if not exists (select 1 from pg_index i join pg_attribute a on a.attrelid = i.indrelid and a.attnum = any(i.indkey)
                  where i.indrelid = 'public.chat_daily_stats'::regclass and i.indisprimary and a.attname = 'shard') then
    alter table public.chat_daily_stats drop constraint chat_daily_stats_pkey;
    alter table public.chat_daily_stats add primary key (day, kind, shard);
  end if;
end $$;
alter table public.chat_daily_stats enable row level security;
-- [의도적] 정책 없음 — 운영 콘솔이 ts_admin_chat_stats 로만 읽는다.
revoke all on table public.chat_daily_stats from anon, authenticated;

create or replace function public.ts_chat_count_daily()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.is_notice then return null; end if;
  insert into public.chat_daily_stats (day, kind, shard, messages)
  select (new.created_at at time zone public.ts_game_time_zone())::date, c.kind, (new.id % 8)::smallint, 1
    from public.chat_channels c where c.id = new.channel_id
  on conflict (day, kind, shard) do update set messages = public.chat_daily_stats.messages + 1;
  return null;
end;
$$;
revoke all on function public.ts_chat_count_daily() from public, anon, authenticated;

drop trigger if exists trg_chat_count_daily on public.chat_messages;
create trigger trg_chat_count_daily
after insert on public.chat_messages
for each row execute function public.ts_chat_count_daily();

-- 처음 설치할 때 이미 남아 있던 메시지도 센다(보관 기간 안의 것만 남아 있다). 이미 센 날·종류는 건드리지 않는다.
insert into public.chat_daily_stats (day, kind, shard, messages)
select x.day, x.kind, 0, x.n
  from (select (m.created_at at time zone public.ts_game_time_zone())::date as day, c.kind, count(*) as n
          from public.chat_messages m join public.chat_channels c on c.id = m.channel_id
         where not m.is_notice
         group by 1, 2) x
 where not exists (select 1 from public.chat_daily_stats s where s.day = x.day and s.kind = x.kind);

-- 콘솔 "통계 › 채팅" — 최근 p_days 일(게임 시간대 날짜)의 일일 채팅 개수, 채널 종류별(global·server·group·direct).
create or replace function public.ts_admin_chat_stats(p_days integer)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with params as (
    select least(365, greatest(1, coalesce(p_days, 30))) as days, (now() at time zone public.ts_game_time_zone())::date as today
  ),
  days as (
    select generate_series(p.today - p.days + 1, p.today, interval '1 day')::date as day from params p
  ),
  per_kind as (
    select s.day, s.kind, sum(s.messages)::bigint as messages
      from public.chat_daily_stats s, params p
     where s.day > p.today - p.days
     group by s.day, s.kind
  )
  select jsonb_build_object(
    'daily', coalesce((select jsonb_agg(jsonb_build_object(
                 'day', d.day,
                 'total', coalesce((select sum(k.messages) from per_kind k where k.day = d.day), 0),
                 'by_kind', coalesce((select jsonb_object_agg(k.kind, k.messages) from per_kind k where k.day = d.day), '{}'::jsonb))
               order by d.day) from days d), '[]'::jsonb),
    'first_day', (select min(day) from public.chat_daily_stats)
  );
$$;

comment on function public.ts_admin_chat_stats(integer) is '콘솔 통계 — 일일 채팅 개수(공지 제외), 채널 종류별. 게임 시간대 날짜. service_role 전용.';
revoke all on function public.ts_admin_chat_stats(integer) from public, anon, authenticated;
grant execute on function public.ts_admin_chat_stats(integer) to service_role;

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
  v_text := public.ts_mask_banned_words(v_text);

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
               (m.deleted_at is not null) as deleted, m.created_at, m.is_notice
          from public.chat_messages m
         where m.channel_id = c.id and m.scope_key = v_scope
         order by m.id desc limit v_limit
      ) t;
    else
      select coalesce(jsonb_agg(t order by t.id), '[]'::jsonb) into v_rows
      from (
        select m.id, m.account_id, m.user_id, m.display_name,
               case when m.deleted_at is null then m.content else null end as content,
               (m.deleted_at is not null) as deleted, m.created_at, m.is_notice
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

-- 공지는 전체·서버 채널에만 보낸다(귓속말·로비는 대상이 정해진 대화라 뺀다).
-- 서버 채널에서 p_server_id 가 null 이면 모든 서버에 한 줄씩 넣는다. 넣은 줄 수를 돌려준다.
create or replace function public.ts_admin_chat_notice(
  p_channel_id uuid,
  p_content    text,
  p_server_id  uuid default null
)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  c       public.chat_channels%rowtype;
  v_body  text := btrim(coalesce(p_content, ''));
  v_count int;
begin
  select * into c from public.chat_channels where id = p_channel_id;
  if not found then
    raise exception 'chat_channel_not_found';
  end if;
  if c.kind not in ('global', 'server') then
    raise exception 'chat_kind_unsupported: %', c.kind;
  end if;
  if v_body = '' then
    raise exception 'chat_empty';
  end if;
  if char_length(v_body) > 1000 then
    raise exception 'chat_too_long';
  end if;

  if c.kind = 'global' then
    insert into public.chat_messages (channel_id, scope_key, account_id, content, is_notice)
    values (c.id, '', '00000000-0000-0000-0000-000000000000', v_body, true);
    v_count := 1;
  else
    insert into public.chat_messages (channel_id, scope_key, account_id, content, is_notice)
    select c.id, gs.id::text, '00000000-0000-0000-0000-000000000000', v_body, true
      from public.game_servers gs
     where p_server_id is null or gs.id = p_server_id;
    get diagnostics v_count = row_count;
    if v_count = 0 then
      raise exception 'server_not_found';
    end if;
  end if;
  return v_count;
end;
$$;

comment on function public.ts_admin_chat_notice(uuid, text, uuid) is
  '채팅 공지(어드민). 전체·서버 채널에 운영자 공지를 넣는다. service_role 전용.';

revoke all on function public.ts_admin_chat_notice(uuid, text, uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_chat_notice(uuid, text, uuid) to service_role;

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

create or replace function public.ts_admin_chat_unmute(p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  delete from public.chat_mutes where id = p_id;
  if not found then
    raise exception 'chat_mute_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_chat_unmute(bigint) is '채팅 차단 해제(행 삭제). service_role 전용.';

revoke all on function public.ts_admin_chat_unmute(bigint) from public, anon, authenticated;
grant execute on function public.ts_admin_chat_unmute(bigint) to service_role;

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
-- 19. 매치 결과 신고
-- #############################################################################

-- 참가자 각자가 자기 쪽 결과를 신고합니다(ts_match_report_result). 클라이언트 신고를 그대로 믿지 않고
-- 상대의 신고와 상호 지목·승패(또는 협동 결과)가 맞아야만 지급합니다. 지급 금액도 클라가 보내지 않고,
-- game_code 별로 이 절에서 만드는 match_reward_configs 를 서버가 읽어 계산해 우편함으로 보냅니다.
-- game_code 로 프로젝트·모드를 구분하므로 여러 게임이 이 한 벌의 테이블·RPC 를 함께 씁니다.
--
-- 상대가 없는 판(봇·혼자 하는 판)은 ts_match_report_solo_result 로 기록만 남깁니다(status='solo', 지급 없음).
-- 쌓인 기록은 본인 것만 ts_match_stats(승·패·점수 요약)·ts_match_history(최근 목록)로 읽습니다.
-- 확인 단계(verified·pending·solo)를 나눠 돌려주므로, 어디까지 믿을지는 게임이 고릅니다.
-- 랭킹·시즌은 15절 리더보드가 맡습니다.

create table if not exists public.match_reward_configs (
  game_code         text primary key,
  mode              text not null default 'competitive' check (mode in ('competitive', 'coop')),
  win_items         jsonb not null default '[]'::jsonb,
  mail_title        text not null default '매치 보상',
  mail_content      text not null default '',
  mail_expires_days int not null default 7,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  updated_by        text null
);

alter table public.match_reward_configs add column if not exists updated_by text;

comment on table public.match_reward_configs is
  'game_code 별 매치 보상 정책. mode=competitive 는 승패가 반대여야, coop 는 결과가 같아야 검증 통과. 운영 콘솔 매치 보상 화면에서 값만 바꿉니다(코드 변경 불필요).';
comment on column public.match_reward_configs.win_items is '승리 보상 아이템 배열 [{key,count}, ...]. mails.items 와 같은 형식.';

create table if not exists public.match_results (
  id                    uuid primary key default gen_random_uuid(),
  session_id            text not null,
  game_code             text not null,
  account_id            uuid not null references auth.users (id) on delete cascade,
  user_id               text not null,
  is_win                boolean not null,
  opponent_account_id   uuid null,
  score                 double precision null,
  status                text not null default 'pending',
  paid_mail_id          uuid null references public.mails (id) on delete set null,
  rewarded_at           timestamptz null,
  reported_at           timestamptz not null default now(),
  resolved_at           timestamptz null
);

-- 상대 없는 판(status='solo')은 상대 계정이 없다. 이미 설치된 프로젝트는 아래 alter 로 같은 모양에 수렴한다.
alter table public.match_results add column if not exists score double precision;
-- 보상 우편은 기한이 지나면 지워져 paid_mail_id 가 null 이 된다. 지급 여부는 이 칸으로 남긴다.
alter table public.match_results add column if not exists rewarded_at timestamptz;
update public.match_results set rewarded_at = resolved_at
 where paid_mail_id is not null and rewarded_at is null;
alter table public.match_results alter column opponent_account_id drop not null;
alter table public.match_results drop constraint if exists match_results_status_check;
alter table public.match_results
  add constraint match_results_status_check check (status in ('pending', 'paid', 'mismatch', 'solo'));
alter table public.match_results drop constraint if exists match_results_bot_opponent_check;
alter table public.match_results drop constraint if exists match_results_solo_opponent_check;
alter table public.match_results
  add constraint match_results_solo_opponent_check check ((status = 'solo') = (opponent_account_id is null));

comment on table public.match_results is
  '참가자별 매치 기록. (session_id, account_id) 유니크로 재신고를 멱등하게 만듭니다. status=solo 는 상대 없는 판(봇·혼자)으로, 교차검증·지급 없이 기록만 남깁니다.';
comment on column public.match_results.score is
  '게임이 정하는 판 점수(웨이브·점수·시간 등). 선택. 상대 신고와 맞춰 보지 않으므로 믿을 수 있는 정도는 그 판의 status 를 따릅니다.';
comment on column public.match_results.rewarded_at is
  '이 참가자에게 보상 우편을 보낸 시각. 우편이 만료돼 지워져도 남는다(paid_mail_id 는 null 이 된다).';

create unique index if not exists match_results_session_account_key
  on public.match_results (session_id, account_id);
create index if not exists match_results_session_idx on public.match_results (session_id);
drop index if exists public.match_results_account_game_idx;
create index if not exists match_results_account_game_reported_idx
  on public.match_results (account_id, game_code, reported_at desc);
create index if not exists match_results_game_account_idx
  on public.match_results (game_code, account_id);

alter table public.match_reward_configs enable row level security;
alter table public.match_results        enable row level security;

-- 두 테이블 모두 SECURITY DEFINER RPC 경유로만 읽고 쓴다 — 클라이언트 직접 접근 없음(RLS 정책 없이 grant 만 회수).
revoke all on table public.match_reward_configs from anon, authenticated;
revoke all on table public.match_results        from anon, authenticated;

-- p_score 를 붙이며 인자 개수가 바뀌었다. 옛 4인자판이 남으면 PostgREST 가 어느 쪽을 부를지 헷갈린다.
drop function if exists public.ts_match_report_result(text, text, boolean, uuid);

create or replace function public.ts_match_report_result(
  p_session_id          text,
  p_game_code           text,
  p_is_win              boolean,
  p_opponent_account_id uuid,
  p_score               double precision default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid          uuid := auth.uid();
  v_user         text;
  v_cfg          public.match_reward_configs%rowtype;
  v_mine         public.match_results%rowtype;
  v_opp          public.match_results%rowtype;
  v_mail_id      uuid;
  v_mine_mail_id uuid;
  v_now          timestamptz := now();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_session_id is null or btrim(p_session_id) = '' then
    raise exception 'match_session_id_empty';
  end if;
  if p_game_code is null or btrim(p_game_code) = '' then
    raise exception 'match_game_code_empty';
  end if;
  if p_opponent_account_id is null then
    raise exception 'match_opponent_required';
  end if;
  if p_opponent_account_id = v_uid then
    raise exception 'match_opponent_invalid';
  end if;
  -- NaN 이 한 판이라도 들어가면 그 단계의 최댓값이 영영 NaN 이 된다(Postgres 는 NaN 을 가장 큰 값으로 친다).
  if p_score is not null and (p_score = 'NaN'::float8 or p_score in ('Infinity'::float8, '-Infinity'::float8)) then
    raise exception 'match_score_invalid';
  end if;

  -- 세션 단위 직렬화 — 두 참가자가 거의 동시에 불러도 한 쪽씩 처리되어 이중 지급이 나지 않는다.
  perform pg_advisory_xact_lock(hashtextextended(btrim(p_session_id), 0));

  select * into v_mine from public.match_results
  where session_id = btrim(p_session_id) and account_id = v_uid
  for update;

  -- 보상 설정은 새로 기록할 때만 본다 — 이미 끝난 판의 재시도는 그사이 설정이 지워져도 같은 답을 받아야 한다.
  if not found then
    if not exists (select 1 from public.match_reward_configs where game_code = btrim(p_game_code)) then
      raise exception 'match_reward_config_not_found';
    end if;

    select p.user_id into v_user from public.user_profiles p where p.account_id = v_uid;
    v_user := coalesce(v_user, v_uid::text);

    insert into public.match_results
      (session_id, game_code, account_id, user_id, is_win, opponent_account_id, score)
    values
      (btrim(p_session_id), btrim(p_game_code), v_uid, v_user, p_is_win, p_opponent_account_id, p_score)
    returning * into v_mine;

    -- 이 세션에서 나를 지목하고 기다리던 다른 사람의 기록은, 내가 다른 상대를 지목했으므로 맞춰 볼 신고가
    -- 영영 오지 않는다 — 무효로 끝낸다. 내가 지목한 상대의 기록은 아래에서 맞춰 본다.
    update public.match_results set status = 'mismatch', resolved_at = v_now
    where session_id = btrim(p_session_id) and opponent_account_id = v_uid
      and account_id <> p_opponent_account_id and status = 'pending';
  end if;

  -- 상대 없는 판으로 이미 기록한 세션을 대인전으로 다시 신고하면, 상대 신고와 맞춰 지급받는 길이 열린다.
  -- 처음과 다른 상대·게임 코드로 재신고하는 것도 같다 — 지급은 처음 신고를 기준으로만 한다.
  if v_mine.status = 'solo'
     or v_mine.opponent_account_id is distinct from p_opponent_account_id
     or v_mine.game_code is distinct from btrim(p_game_code) then
    raise exception 'match_result_mismatch';
  end if;

  -- 이미 끝난 처리는 재신고해도 그대로 반환한다 — 멱등.
  if v_mine.status = 'paid' then
    return jsonb_build_object('status', 'already_paid', 'rewarded', v_mine.rewarded_at is not null);
  end if;
  if v_mine.status = 'mismatch' then
    return jsonb_build_object('status', 'mismatch', 'rewarded', false);
  end if;

  select * into v_opp from public.match_results
  where session_id = btrim(p_session_id) and account_id = v_mine.opponent_account_id
  for update;

  if not found then
    return jsonb_build_object('status', 'pending', 'rewarded', false);
  end if;

  -- 무효는 예외 대신 상태로 돌려준다 — raise 하면 같은 호출의 쓰기가 전부 되돌려져 무효 표시가 남지 않는다.
  -- 상대가 나를 지목하지 않았거나 상대 기록이 이미 다른 판정으로 끝났으면(지급·무효·상대 없는 판)
  -- 상대 기록은 다른 판의 것이다 — 내 기록만 무효로 한다.
  if v_opp.opponent_account_id is distinct from v_uid or v_opp.status <> 'pending' then
    update public.match_results set status = 'mismatch', resolved_at = v_now
    where id = v_mine.id;
    return jsonb_build_object('status', 'mismatch', 'rewarded', false);
  end if;
  -- 서로 지목했는데 게임 코드가 다르면 같은 판에 대한 엇갈린 신고다 — 둘 다 무효로 해야 먼저 신고한 쪽이
  -- 대기로 영영 남지 않는다(승패가 엇갈릴 때와 같다).
  if v_opp.game_code is distinct from v_mine.game_code then
    update public.match_results set status = 'mismatch', resolved_at = v_now
    where id in (v_mine.id, v_opp.id);
    return jsonb_build_object('status', 'mismatch', 'rewarded', false);
  end if;

  -- 보상 설정은 저장된 기록의 game_code 로 읽는다(이번 호출 인자가 아니라).
  select * into v_cfg from public.match_reward_configs where game_code = v_mine.game_code;
  if not found then
    raise exception 'match_reward_config_not_found';
  end if;

  -- 결과 일치 확인 — 경쟁은 승패가 반대, 협동은 결과가 같아야 한다.
  if (v_cfg.mode = 'competitive' and v_mine.is_win = v_opp.is_win)
     or (v_cfg.mode = 'coop' and v_mine.is_win is distinct from v_opp.is_win) then
    update public.match_results set status = 'mismatch', resolved_at = v_now
    where id in (v_mine.id, v_opp.id);
    return jsonb_build_object('status', 'mismatch', 'rewarded', false);
  end if;

  -- 검증 통과 — 승리 쪽에만 우편으로 지급하고, 양쪽 모두 처리 완료로 표시한다.
  v_mine_mail_id := null;
  if v_mine.is_win and jsonb_array_length(v_cfg.win_items) > 0 then
    insert into public.mails (account_id, user_id, sender_type, title, content, expires_at, items, category)
    values (v_uid, v_mine.user_id, 'system', v_cfg.mail_title, v_cfg.mail_content,
            v_now + make_interval(days => v_cfg.mail_expires_days), v_cfg.win_items, 'match_result')
    returning id into v_mine_mail_id;
  end if;

  update public.match_results
  set status = 'paid', paid_mail_id = v_mine_mail_id, resolved_at = v_now,
      rewarded_at = case when v_mine_mail_id is null then null else v_now end
  where id = v_mine.id;

  v_mail_id := null;
  if v_opp.is_win and jsonb_array_length(v_cfg.win_items) > 0 then
    insert into public.mails (account_id, user_id, sender_type, title, content, expires_at, items, category)
    values (v_opp.account_id, v_opp.user_id, 'system', v_cfg.mail_title, v_cfg.mail_content,
            v_now + make_interval(days => v_cfg.mail_expires_days), v_cfg.win_items, 'match_result')
    returning id into v_mail_id;
  end if;

  update public.match_results
  set status = 'paid', paid_mail_id = v_mail_id, resolved_at = v_now,
      rewarded_at = case when v_mail_id is null then null else v_now end
  where id = v_opp.id;

  -- rewarded는 승패가 아니라 실제로 우편이 갔는지를 반영한다(win_items가 비어 있으면 승리해도 우편 없음).
  return jsonb_build_object('status', 'paid', 'rewarded', v_mine_mail_id is not null);
end;
$$;

comment on function public.ts_match_report_result(text, text, boolean, uuid, double precision) is
  '본인 매치 결과 신고. 상대 신고와 교차검증돼야 지급. 재신고는 멱등(점수도 처음 값 유지). SECURITY DEFINER.';

revoke all on function public.ts_match_report_result(text, text, boolean, uuid, double precision) from public, anon;
grant execute on function public.ts_match_report_result(text, text, boolean, uuid, double precision) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_report_solo_result — 상대 없는 판(봇·혼자)을 기록한다. 맞춰 볼 상대 신고가 없어 검증할 수 없으므로
-- 우편 지급은 하지 않는다. game_code 는 보상 설정에 등록된 것만 받는다(오타로 다른 코드에 쌓이지 않게).
-- ---------------------------------------------------------------------------

drop function if exists public.ts_match_report_bot_result(text, text, boolean);

create or replace function public.ts_match_report_solo_result(
  p_session_id text,
  p_game_code  text,
  p_is_win     boolean,
  p_score      double precision default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid  uuid := auth.uid();
  v_user text;
  v_mine public.match_results%rowtype;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_session_id is null or btrim(p_session_id) = '' then
    raise exception 'match_session_id_empty';
  end if;
  if p_game_code is null or btrim(p_game_code) = '' then
    raise exception 'match_game_code_empty';
  end if;
  if p_score is not null and (p_score = 'NaN'::float8 or p_score in ('Infinity'::float8, '-Infinity'::float8)) then
    raise exception 'match_score_invalid';
  end if;

  -- 대인전 신고와 같은 세션 잠금 — 상대의 대기 기록을 무효로 바꾸는 아래 처리가 그 신고와 엇갈리지 않게.
  perform pg_advisory_xact_lock(hashtextextended(btrim(p_session_id), 0));

  -- 재신고는 멱등. 같은 세션을 대인전으로 이미 신고했으면 두 신고가 서로 다른 판을 가리킨 것이다.
  select * into v_mine from public.match_results
  where session_id = btrim(p_session_id) and account_id = v_uid;
  if found then
    if v_mine.status <> 'solo' or v_mine.game_code is distinct from btrim(p_game_code) then
      raise exception 'match_result_mismatch';
    end if;
    return;
  end if;

  -- 보상 설정은 새로 기록할 때만 본다 — 이미 남긴 판의 재시도는 그사이 설정이 지워져도 성공해야 한다.
  if not exists (select 1 from public.match_reward_configs where game_code = btrim(p_game_code)) then
    raise exception 'match_reward_config_not_found';
  end if;

  select p.user_id into v_user from public.user_profiles p where p.account_id = v_uid;
  v_user := coalesce(v_user, v_uid::text);

  insert into public.match_results
    (session_id, game_code, account_id, user_id, is_win, opponent_account_id, score, status, resolved_at)
  values
    (btrim(p_session_id), btrim(p_game_code), v_uid, v_user, p_is_win, null, p_score, 'solo', now());

  -- 이 세션에서 나를 상대로 지목하고 기다리는 기록은 맞춰 볼 신고가 영영 오지 않는다 — 무효로 끝낸다.
  update public.match_results set status = 'mismatch', resolved_at = now()
  where session_id = btrim(p_session_id) and opponent_account_id = v_uid and status = 'pending';
end;
$$;

comment on function public.ts_match_report_solo_result(text, text, boolean, double precision) is
  '상대 없는 판(봇·혼자) 기록. 교차검증·우편 지급 없음. 재신고는 멱등. SECURITY DEFINER.';

revoke all on function public.ts_match_report_solo_result(text, text, boolean, double precision) from public, anon;
grant execute on function public.ts_match_report_solo_result(text, text, boolean, double precision) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_stats — 내 승·패 수와 점수 최댓값·최솟값을 확인 단계별로 나눠 돌려준다. 어디까지 믿을지는 게임이 고른다:
-- verified 만 상대 신고와 맞춰 본 판이고, pending(상대 미신고)·solo 는 본인 신고뿐이라 조작할 수 있다.
-- 점수는 어느 단계든 본인이 보낸 값이다. mismatch 는 세지 않는다. 판이 없는 단계는 키가 빠진다.
-- ---------------------------------------------------------------------------

drop function if exists public.ts_match_win_count(text);

create or replace function public.ts_match_stats(p_game_code text)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_game_code is null or btrim(p_game_code) = '' then
    raise exception 'match_game_code_empty';
  end if;

  return (
    select coalesce(jsonb_object_agg(b.trust, jsonb_build_object(
             'wins', b.wins, 'losses', b.losses, 'max_score', b.max_score, 'min_score', b.min_score)), '{}'::jsonb)
    from (
      select case r.status when 'paid' then 'verified' else r.status end as trust,
             count(*) filter (where r.is_win)     as wins,
             count(*) filter (where not r.is_win) as losses,
             max(r.score)                         as max_score,
             min(r.score)                         as min_score
      from public.match_results r
      where r.account_id = v_uid and r.game_code = btrim(p_game_code)
        and r.status in ('paid', 'pending', 'solo')
      group by 1
    ) b
  );
end;
$$;

comment on function public.ts_match_stats(text) is
  '내 매치 요약(verified·pending·solo 별 승·패·점수 최대·최소). mismatch 제외. SECURITY DEFINER.';

revoke all on function public.ts_match_stats(text) from public, anon;
grant execute on function public.ts_match_stats(text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_history — 내 매치 기록을 최근 순으로 돌려준다. p_game_code 가 비면 모든 게임 코드.
-- 다음 쪽은 마지막 행의 reported_at 을 p_before 로 넘겨 받는다. 한 번에 최대 100개.
-- ---------------------------------------------------------------------------

create or replace function public.ts_match_history(
  p_game_code text        default null,
  p_limit     int         default 20,
  p_before    timestamptz default null
)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  v_code  text := nullif(btrim(coalesce(p_game_code, '')), '');
  v_limit int  := greatest(1, least(coalesce(p_limit, 20), 100));
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  return (
    select coalesce(jsonb_agg(jsonb_build_object(
             'session_id',          t.session_id,
             'game_code',           t.game_code,
             'is_win',              t.is_win,
             'score',               t.score,
             'status',              case t.status when 'paid' then 'verified' else t.status end,
             'opponent_account_id', t.opponent_account_id,
             'reported_at',         t.reported_at) order by t.reported_at desc), '[]'::jsonb)
    from (
      select r.session_id, r.game_code, r.is_win, r.score, r.status, r.opponent_account_id, r.reported_at
      from public.match_results r
      where r.account_id = v_uid
        and (v_code is null or r.game_code = v_code)
        and (p_before is null or r.reported_at < p_before)
      order by r.reported_at desc
      limit v_limit
    ) t
  );
end;
$$;

comment on function public.ts_match_history(text, int, timestamptz) is
  '내 매치 기록 최근 순(최대 100개, p_before 로 다음 쪽). status 는 verified·pending·mismatch·solo. SECURITY DEFINER.';

revoke all on function public.ts_match_history(text, int, timestamptz) from public, anon;
grant execute on function public.ts_match_history(text, int, timestamptz) to authenticated;

-- ---------------------------------------------------------------------------
-- 운영 전용 — 보상 설정 조회·등록·삭제, 플레이어 대전 기록 조회(운영 콘솔, service_role)
-- ---------------------------------------------------------------------------

create or replace function public.ts_admin_match_reward_config_list()
returns setof public.match_reward_configs
language sql
security definer
set search_path = public
as $$
  select * from public.match_reward_configs order by game_code;
$$;

-- p_create_only·p_by 를 붙이며 인자 개수가 바뀌었다. 옛 6인자판이 남으면 PostgREST 가 어느 쪽을 부를지 헷갈린다.
drop function if exists public.ts_admin_match_reward_config_upsert(text, text, jsonb, text, text, int);

-- p_create_only 가 true 면 이미 있는 코드에 대해 match_reward_config_exists 로 거절한다 —
-- 콘솔의 "추가"가 목록이 낡았거나 불러오기에 실패한 채로 기존 설정을 덮어쓰지 않게.
create or replace function public.ts_admin_match_reward_config_upsert(
  p_game_code         text,
  p_mode              text,
  p_win_items         jsonb,
  p_mail_title        text    default '매치 보상',
  p_mail_content      text    default '',
  p_mail_expires_days int     default 7,
  p_create_only       boolean default false,
  p_by                text    default null
)
returns public.match_reward_configs
language plpgsql
security definer
set search_path = public
as $$
declare
  v_row public.match_reward_configs%rowtype;
  v_key text;
  v_cnt text;
begin
  if p_game_code is null or btrim(p_game_code) = '' then
    raise exception 'match_game_code_empty';
  end if;
  -- 게임이 코드에 박아 넣는 값이라 공백·대문자가 섞이면 신고가 조용히 다른 코드로 간다.
  if btrim(p_game_code) !~ '^[a-z0-9_]{1,64}$' then
    raise exception 'match_game_code_invalid: %', p_game_code;
  end if;
  if p_mode not in ('competitive', 'coop') then
    raise exception 'invalid_match_mode: %', p_mode;
  end if;
  if coalesce(p_mail_expires_days, 7) not between 1 and 365 then
    raise exception 'invalid_mail_expires_days';
  end if;
  if p_win_items is null or jsonb_typeof(p_win_items) <> 'array' then
    raise exception 'items_not_array';
  end if;
  -- 보상 아이템은 카탈로그에 있는 키만 허용한다. 수량은 SDK 가 int 로 읽으므로 int 범위로 자른다
  -- (넘으면 그 플레이어의 우편함 목록 해석이 통째로 실패한다).
  for v_key, v_cnt in
    select e->>'key', e->>'count' from jsonb_array_elements(p_win_items) e
  loop
    if coalesce(btrim(v_key), '') = '' then
      raise exception 'item_key_empty';
    end if;
    if v_cnt is null or v_cnt !~ '^[0-9]{1,10}$' or v_cnt::bigint not between 1 and 2147483647 then
      raise exception 'item_count_invalid: %', v_key;
    end if;
    if not exists (select 1 from public.game_items g where g.key = v_key) then
      raise exception 'unknown_item_key: %', v_key;
    end if;
  end loop;

  -- 추가는 확인과 저장을 한 문장으로 한다 — 둘이 거의 동시에 같은 코드를 추가해도 뒤쪽이 덮어쓰지 않는다.
  if p_create_only then
    insert into public.match_reward_configs
      (game_code, mode, win_items, mail_title, mail_content, mail_expires_days, updated_at, updated_by)
    values
      (btrim(p_game_code), p_mode, p_win_items, coalesce(p_mail_title, '매치 보상'),
       coalesce(p_mail_content, ''), coalesce(p_mail_expires_days, 7), now(), p_by)
    on conflict (game_code) do nothing
    returning * into v_row;
    if not found then
      raise exception 'match_reward_config_exists: %', btrim(p_game_code);
    end if;
    return v_row;
  end if;

  insert into public.match_reward_configs
    (game_code, mode, win_items, mail_title, mail_content, mail_expires_days, updated_at, updated_by)
  values
    (btrim(p_game_code), p_mode, p_win_items, coalesce(p_mail_title, '매치 보상'),
     coalesce(p_mail_content, ''), coalesce(p_mail_expires_days, 7), now(), p_by)
  on conflict (game_code) do update set
    mode              = excluded.mode,
    win_items         = excluded.win_items,
    mail_title        = excluded.mail_title,
    mail_content      = excluded.mail_content,
    mail_expires_days = excluded.mail_expires_days,
    updated_at        = now(),
    updated_by        = excluded.updated_by
  returning * into v_row;

  return v_row;
end;
$$;

comment on function public.ts_admin_match_reward_config_list() is '매치 보상 설정 전체 조회. service_role 전용.';
comment on function public.ts_admin_match_reward_config_upsert(text, text, jsonb, text, text, int, boolean, text) is
  '매치 보상 설정 등록·수정. p_create_only 면 이미 있는 코드를 거절. service_role 전용.';

revoke all on function public.ts_admin_match_reward_config_list() from public, anon, authenticated;
revoke all on function public.ts_admin_match_reward_config_upsert(text, text, jsonb, text, text, int, boolean, text) from public, anon, authenticated;
grant execute on function public.ts_admin_match_reward_config_list() to service_role;
grant execute on function public.ts_admin_match_reward_config_upsert(text, text, jsonb, text, text, int, boolean, text) to service_role;

-- ts_admin_match_reward_overview — 보상 설정과 게임 코드별 기록 수를 함께 돌려준다.
-- 설정을 지운 뒤에도 기록이 남은 코드는 config 가 null 인 줄로 나온다(그 코드의 신고는 지금 실패한다).
create or replace function public.ts_admin_match_reward_overview()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'game_code',        coalesce(c.game_code, s.game_code),
           'config',           case when c.game_code is null then null else jsonb_build_object(
                                 'mode', c.mode, 'win_items', c.win_items, 'mail_title', c.mail_title,
                                 'mail_content', c.mail_content, 'mail_expires_days', c.mail_expires_days,
                                 'updated_at', c.updated_at, 'updated_by', c.updated_by) end,
           'record_count',     coalesce(s.record_count, 0),
           'player_count',     coalesce(s.player_count, 0),
           'pending_count',    coalesce(s.pending_count, 0),
           'last_reported_at', s.last_reported_at)
         order by coalesce(c.game_code, s.game_code)), '[]'::jsonb)
  from public.match_reward_configs c
  full outer join (
    select game_code, count(*) as record_count, count(distinct account_id) as player_count,
           count(*) filter (where status = 'pending') as pending_count,
           max(reported_at) as last_reported_at
    from public.match_results
    group by game_code
  ) s on s.game_code = c.game_code;
$$;

comment on function public.ts_admin_match_reward_overview() is
  '매치 보상 설정 + 게임 코드별 기록 수·플레이어 수·상대 미신고 수·최근 신고 시각. service_role 전용.';

revoke all on function public.ts_admin_match_reward_overview() from public, anon, authenticated;
grant execute on function public.ts_admin_match_reward_overview() to service_role;

-- ts_admin_match_reward_config_delete — 설정만 지운다. 쌓인 기록은 남고, 이 코드로 오는 신고는 그때부터 실패한다.
create or replace function public.ts_admin_match_reward_config_delete(p_game_code text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_deleted int;
begin
  delete from public.match_reward_configs where game_code = btrim(coalesce(p_game_code, ''));
  get diagnostics v_deleted = row_count;
  return jsonb_build_object('deleted', v_deleted > 0);
end;
$$;

comment on function public.ts_admin_match_reward_config_delete(text) is
  '매치 보상 설정 삭제. 기록은 남는다. service_role 전용.';

revoke all on function public.ts_admin_match_reward_config_delete(text) from public, anon, authenticated;
grant execute on function public.ts_admin_match_reward_config_delete(text) to service_role;

-- ts_admin_match_player_records — 플레이어 한 명의 대전 기록. 요약은 게임 코드·확인 단계별(무효 포함),
-- 목록은 최근 순으로 p_offset 부터 p_limit 개(1~100). p_game_code 가 비면 모든 코드.
create or replace function public.ts_admin_match_player_records(
  p_account_id uuid,
  p_game_code  text default null,
  p_limit      int  default 20,
  p_offset     int  default 0
)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_code   text := nullif(btrim(coalesce(p_game_code, '')), '');
  v_limit  int  := greatest(1, least(coalesce(p_limit, 20), 100));
  v_offset int  := greatest(0, coalesce(p_offset, 0));
begin
  return jsonb_build_object(
    'summary', coalesce((
      select jsonb_agg(jsonb_build_object(
               'game_code', b.game_code, 'status', b.status,
               'wins', b.wins, 'losses', b.losses, 'max_score', b.max_score, 'min_score', b.min_score)
             order by b.game_code, b.status)
      from (
        select r.game_code,
               case r.status when 'paid' then 'verified' else r.status end as status,
               count(*) filter (where r.is_win)     as wins,
               count(*) filter (where not r.is_win) as losses,
               max(r.score) as max_score,
               min(r.score) as min_score
        from public.match_results r
        where r.account_id = p_account_id
        group by 1, 2
      ) b
    ), '[]'::jsonb),
    'total', (
      select count(*) from public.match_results r
      where r.account_id = p_account_id and (v_code is null or r.game_code = v_code)
    ),
    'rows', coalesce((
      select jsonb_agg(jsonb_build_object(
               'session_id',          t.session_id,
               'game_code',           t.game_code,
               'is_win',              t.is_win,
               'score',               t.score,
               'status',              case t.status when 'paid' then 'verified' else t.status end,
               'rewarded',            t.rewarded_at is not null,
               'opponent_account_id', t.opponent_account_id,
               'opponent_exists',     exists (select 1 from auth.users u where u.id = t.opponent_account_id),
               'opponent_name',       coalesce(d.display_name, ''),
               'reported_at',         t.reported_at,
               'resolved_at',         t.resolved_at)
             order by t.reported_at desc)
      from (
        select r.* from public.match_results r
        where r.account_id = p_account_id and (v_code is null or r.game_code = v_code)
        order by r.reported_at desc
        limit v_limit offset v_offset
      ) t
      left join public.display_names d on d.account_id = t.opponent_account_id
    ), '[]'::jsonb)
  );
end;
$$;

comment on function public.ts_admin_match_player_records(uuid, text, int, int) is
  '플레이어 대전 기록(게임 코드·확인 단계별 요약 + 최근 순 목록). service_role 전용.';

revoke all on function public.ts_admin_match_player_records(uuid, text, int, int) from public, anon, authenticated;
grant execute on function public.ts_admin_match_player_records(uuid, text, int, int) to service_role;

notify pgrst, 'reload schema';


-- #############################################################################
-- 20. 친구
-- #############################################################################

-- 닉네임으로 검색해 친구 요청을 보내고, 수락하면 친구가 됩니다. 친구 관계는 요청 행 하나가
-- status='accepted'로 남는 것으로 표현합니다(A→B든 B→A든 한 행). 클라이언트는 이 테이블에
-- 직접 접근하지 않고 전부 SECURITY DEFINER RPC를 거칩니다(정책 0개, mail_batches 등과 같은 패턴).
--
-- 두 사람이 동시에 서로에게 요청을 보내면, 나중 요청은 새 행을 만들지 않고 상대의 기존 pending
-- 행을 즉시 accepted로 전환합니다 — 안 그러면 양쪽 다 "요청 보냄"인 상태로 서로를 못 보고
-- 영원히 pending으로 남습니다.
-- =============================================================================
-- 친구 — friend_requests + RPC
-- 선행: 02(user_profiles, display_names)
-- =============================================================================

create table if not exists public.friend_requests (
  id                    uuid primary key default gen_random_uuid(),
  requester_account_id  uuid not null references auth.users (id) on delete cascade,
  addressee_account_id  uuid not null references auth.users (id) on delete cascade,
  status                text not null default 'pending' check (status in ('pending', 'accepted', 'declined', 'cancelled')),
  created_at            timestamptz not null default now(),
  responded_at          timestamptz null,
  constraint friend_requests_not_self check (requester_account_id <> addressee_account_id)
);

comment on table public.friend_requests is
  '친구 요청/친구 관계. status=accepted인 행이 곧 친구 관계다. 조회·응답은 전부 RPC 경유(SECURITY DEFINER), 클라이언트 직접 접근 없음.';

-- 같은 방향 중복 pending 방지. 반대 방향 pending은 RPC가 즉시 accepted로 합친다(위 설명).
create unique index if not exists friend_requests_pending_pair_uidx
  on public.friend_requests (requester_account_id, addressee_account_id)
  where status = 'pending';

create index if not exists friend_requests_addressee_idx on public.friend_requests (addressee_account_id, status);
create index if not exists friend_requests_requester_idx on public.friend_requests (requester_account_id, status);

alter table public.friend_requests enable row level security;

revoke all on table public.friend_requests from anon, authenticated;

-- ---------------------------------------------------------------------------
-- friend_settings — 친구 기능 제한값. 행 하나만 두고 운영자가 값만 바꾼다.
--   제한이 없으면 봇 하나가 전체 유저에게 친구 요청을 도배할 수 있어 기본값을 둔다.
-- ---------------------------------------------------------------------------
create table if not exists public.friend_settings (
  id                       int primary key default 1 check (id = 1),
  max_friends              int not null default 100 check (max_friends > 0),
  max_pending_sent         int not null default 50  check (max_pending_sent > 0),
  request_cooldown_seconds int not null default 3   check (request_cooldown_seconds >= 0),
  updated_at               timestamptz not null default now(),
  updated_by               text
);

alter table public.friend_settings add column if not exists updated_by text;

insert into public.friend_settings (id) values (1) on conflict (id) do nothing;

comment on table public.friend_settings is
  '친구 기능 제한값(행 하나). max_friends=친구 수 상한, max_pending_sent=보낸 대기 요청 상한, request_cooldown_seconds=연속 요청 최소 간격. 기본값은 여기 한 곳에만 둔다.';

alter table public.friend_settings enable row level security;

revoke all on table public.friend_settings from anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_settings — 제한값을 읽는다.
--   기본값을 여기 다시 적지 않는다. 적어 두면 테이블 DEFAULT 와 두 벌이 되어 한쪽만
--   고쳤을 때 조용히 갈라진다. 설정 행은 위 insert 가 만들고 지울 수 있는 경로가 없으므로
--   (delete 권한을 가진 롤도, 지우는 RPC 도 없다) 없으면 설치가 깨진 것이다 — 무제한으로
--   열어 두지 않고 곧바로 실패시킨다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_settings()
returns public.friend_settings
language plpgsql
stable
security definer
set search_path = public
as $$
declare v_cfg public.friend_settings%rowtype;
begin
  select * into v_cfg from public.friend_settings where id = 1;
  if not found then
    raise exception 'friend_settings_missing';
  end if;
  return v_cfg;
end;
$$;

revoke all on function public.ts_friend_settings() from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_count — 그 계정의 현재 친구 수. 상한 검사가 여러 곳에 있어 함수로 뺐다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_count(p_account_id uuid)
returns int
language sql
stable
security definer
set search_path = public
as $$
  select count(*)::int from public.friend_requests
   where status = 'accepted'
     and (requester_account_id = p_account_id or addressee_account_id = p_account_id);
$$;

revoke all on function public.ts_friend_count(uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_are_friends — 두 계정이 친구인지(status=accepted). 방향은 무관하다. 검색·귓속말·
-- 로비 초대 등 여러 곳에서 반복되던 조건이라 여기 하나로 뺐다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_are_friends(p_a uuid, p_b uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1 from public.friend_requests
     where status = 'accepted'
       and ((requester_account_id = p_a and addressee_account_id = p_b)
         or (requester_account_id = p_b and addressee_account_id = p_a))
  );
$$;

revoke all on function public.ts_are_friends(uuid, uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_search — 닉네임 정확 일치(대소문자 무시) 검색. 부분 검색은 전체 유저 스캔이
-- 가능해져 열지 않는다. 이미 친구·요청 중인지 status도 같이 돌려준다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_search(p_nickname text)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid    uuid := auth.uid();
  v_target uuid;
  v_status text;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_nickname is null or btrim(p_nickname) = '' then
    raise exception 'friend_nickname_empty';
  end if;

  select account_id into v_target
    from public.display_names
   where lower(trim(display_name)) = lower(trim(p_nickname));

  if not found then
    raise exception 'friend_nickname_not_found';
  end if;
  if v_target = v_uid then
    raise exception 'friend_self_request';
  end if;

  select case
           when status = 'accepted' then 'friends'
           when status = 'pending' and requester_account_id = v_uid then 'request_sent'
           when status = 'pending' and addressee_account_id = v_uid then 'request_received'
           else 'none'
         end
    into v_status
    from public.friend_requests
   where status in ('pending', 'accepted')
     and ((requester_account_id = v_uid and addressee_account_id = v_target)
       or (requester_account_id = v_target and addressee_account_id = v_uid))
   order by created_at desc
   limit 1;

  return jsonb_build_object(
    'account_id', v_target,
    'display_name', (select display_name from public.display_names where account_id = v_target),
    'status', coalesce(v_status, 'none'),
    -- 검색은 닉네임 정확 일치라 이미 닉네임을 아는 사람만 조회할 수 있다(부분 검색·목록 열람 불가).
    -- 그 전제 위에서 노출하기로 한 값이다 — 개인이 끄는 수단(플레이어 간 차단)은 아직 없다.
    'last_activity_at', (select last_activity_at from public.user_profiles where account_id = v_target)
  );
end;
$$;

comment on function public.ts_friend_search(text) is
  '닉네임 정확 일치(대소문자 무시)로 유저 검색. 친구·요청 상태도 같이 반환.';

revoke all on function public.ts_friend_search(text) from public, anon;
grant execute on function public.ts_friend_search(text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_request_send — 친구 요청 전송. 상대가 이미 나에게 보낸 pending이 있으면
-- 즉시 상호 수락으로 전환한다(상호 요청 교착 방지).
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_request_send(p_target_account_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid      uuid := auth.uid();
  v_now      timestamptz := now();
  v_existing public.friend_requests%rowtype;
  v_cfg      public.friend_settings%rowtype;
  v_new_id   uuid;
  v_last     timestamptz;
  v_count    int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_target_account_id is null then
    raise exception 'friend_target_required';
  end if;
  if p_target_account_id = v_uid then
    raise exception 'friend_self_request';
  end if;

  v_cfg := public.ts_friend_settings();

  -- 아직 행이 없으면 아래 FOR UPDATE가 잠글 대상이 없어 동시 요청이 안 걸린다 — A→B·B→A가
  -- 정확히 동시에 들어오면 둘 다 "없음"을 보고 각자 pending 행을 만들어 영원히 안 합쳐질 수
  -- 있다. 쌍 자체를 advisory lock으로 잠가 한쪽씩 처리되게 한다.
  perform pg_advisory_xact_lock(hashtextextended(
    least(v_uid, p_target_account_id)::text || ':' || greatest(v_uid, p_target_account_id)::text, 0));

  -- 두 사람 사이의 최신 유효 행(가는 방향 무관)을 잠근다 — 동시 요청·중복 요청을 직렬화.
  select * into v_existing
    from public.friend_requests
   where status in ('pending', 'accepted')
     and ((requester_account_id = v_uid and addressee_account_id = p_target_account_id)
       or (requester_account_id = p_target_account_id and addressee_account_id = v_uid))
   order by created_at desc
   limit 1
   for update;

  if found then
    if v_existing.status = 'accepted' then
      raise exception 'friend_already_friends';
    end if;
    if v_existing.requester_account_id = p_target_account_id then
      -- 상호 요청이라 여기서 친구가 된다 — 양쪽 상한을 모두 확인한다.
      if public.ts_friend_count(v_uid) >= v_cfg.max_friends then
        raise exception 'friend_limit_reached';
      end if;
      if public.ts_friend_count(p_target_account_id) >= v_cfg.max_friends then
        raise exception 'friend_target_limit_reached';
      end if;
      update public.friend_requests set status = 'accepted', responded_at = v_now where id = v_existing.id;
      return jsonb_build_object('status', 'accepted', 'request_id', v_existing.id);
    end if;
    raise exception 'friend_request_already_sent';
  end if;

  -- 새 요청을 만들 때만 도배·상한을 본다. 위 상호 수락 경로는 요청을 새로 만드는 게 아니라
  -- 이미 온 요청을 받는 것이라 쿨다운·대기 상한 대상이 아니다.
  if v_cfg.request_cooldown_seconds > 0 then
    select max(created_at) into v_last
      from public.friend_requests where requester_account_id = v_uid;
    if v_last is not null and v_last > v_now - make_interval(secs => v_cfg.request_cooldown_seconds) then
      raise exception 'friend_request_too_fast';
    end if;
  end if;

  if public.ts_friend_count(v_uid) >= v_cfg.max_friends then
    raise exception 'friend_limit_reached';
  end if;

  -- 상대가 꽉 찼으면 여기서 거절한다. 안 그러면 요청은 들어가지만 상대가 수락을 누를 때
  -- 거절되고, 그 요청은 pending 으로 남아 보낸 쪽의 대기 상한만 영구히 갉아먹는다.
  if public.ts_friend_count(p_target_account_id) >= v_cfg.max_friends then
    raise exception 'friend_target_limit_reached';
  end if;

  select count(*) into v_count
    from public.friend_requests
   where requester_account_id = v_uid and status = 'pending';
  if v_count >= v_cfg.max_pending_sent then
    raise exception 'friend_pending_limit_reached';
  end if;

  insert into public.friend_requests (requester_account_id, addressee_account_id)
  values (v_uid, p_target_account_id)
  returning id into v_new_id;

  return jsonb_build_object('status', 'pending', 'request_id', v_new_id);
end;
$$;

comment on function public.ts_friend_request_send(uuid) is
  '친구 요청 전송. 상대가 이미 나에게 보낸 pending이 있으면 즉시 상호 수락으로 전환.';

revoke all on function public.ts_friend_request_send(uuid) from public, anon;
grant execute on function public.ts_friend_request_send(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_requests_list — 내가 받은/보낸 대기 중 요청 목록.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_requests_list(p_direction text default 'incoming')
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_direction not in ('incoming', 'outgoing') then
    raise exception 'friend_direction_invalid';
  end if;

  if p_direction = 'incoming' then
    return coalesce((
      select jsonb_agg(jsonb_build_object(
               'request_id', r.id, 'account_id', r.requester_account_id,
               'display_name', coalesce(d.display_name, ''), 'created_at', r.created_at,
               'last_activity_at', p.last_activity_at)
             order by r.created_at desc)
        from public.friend_requests r
        left join public.display_names d on d.account_id = r.requester_account_id
        left join public.user_profiles p on p.account_id = r.requester_account_id
       where r.addressee_account_id = v_uid and r.status = 'pending'
    ), '[]'::jsonb);
  else
    return coalesce((
      select jsonb_agg(jsonb_build_object(
               'request_id', r.id, 'account_id', r.addressee_account_id,
               'display_name', coalesce(d.display_name, ''), 'created_at', r.created_at,
               'last_activity_at', p.last_activity_at)
             order by r.created_at desc)
        from public.friend_requests r
        left join public.display_names d on d.account_id = r.addressee_account_id
        left join public.user_profiles p on p.account_id = r.addressee_account_id
       where r.requester_account_id = v_uid and r.status = 'pending'
    ), '[]'::jsonb);
  end if;
end;
$$;

comment on function public.ts_friend_requests_list(text) is
  '내 대기 중 친구 요청 목록. p_direction: incoming(받은)/outgoing(보낸).';

revoke all on function public.ts_friend_requests_list(text) from public, anon;
grant execute on function public.ts_friend_requests_list(text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_request_respond — 받은 요청을 수락/거절한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_request_respond(p_request_id uuid, p_accept boolean)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_req public.friend_requests%rowtype;
  v_cfg public.friend_settings%rowtype;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into v_req from public.friend_requests where id = p_request_id for update;
  if not found or v_req.addressee_account_id <> v_uid then
    raise exception 'friend_request_not_found';
  end if;
  if v_req.status <> 'pending' then
    raise exception 'friend_request_not_pending';
  end if;

  -- 요청을 보낸 뒤 그 사이에 양쪽 중 한 명이 상한을 채웠을 수 있어 수락 시점에 다시 본다.
  if p_accept then
    v_cfg := public.ts_friend_settings();
    if public.ts_friend_count(v_uid) >= v_cfg.max_friends then
      raise exception 'friend_limit_reached';
    end if;
    if public.ts_friend_count(v_req.requester_account_id) >= v_cfg.max_friends then
      raise exception 'friend_target_limit_reached';
    end if;
  end if;

  update public.friend_requests
     set status = case when p_accept then 'accepted' else 'declined' end,
         responded_at = now()
   where id = p_request_id;

  return jsonb_build_object('status', case when p_accept then 'accepted' else 'declined' end);
end;
$$;

comment on function public.ts_friend_request_respond(uuid, boolean) is
  '받은 친구 요청 수락/거절. 본인이 addressee인 pending 요청만 처리.';

revoke all on function public.ts_friend_request_respond(uuid, boolean) from public, anon;
grant execute on function public.ts_friend_request_respond(uuid, boolean) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_request_cancel — 내가 보낸 pending 요청을 취소한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_request_cancel(p_request_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_req public.friend_requests%rowtype;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into v_req from public.friend_requests where id = p_request_id for update;
  if not found or v_req.requester_account_id <> v_uid then
    raise exception 'friend_request_not_found';
  end if;
  if v_req.status <> 'pending' then
    raise exception 'friend_request_not_pending';
  end if;

  update public.friend_requests set status = 'cancelled', responded_at = now() where id = p_request_id;

  return jsonb_build_object('status', 'cancelled');
end;
$$;

comment on function public.ts_friend_request_cancel(uuid) is
  '내가 보낸 pending 친구 요청 취소.';

revoke all on function public.ts_friend_request_cancel(uuid) from public, anon;
grant execute on function public.ts_friend_request_cancel(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friends_list — 내 친구 목록.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friends_list()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  return coalesce((
    select jsonb_agg(jsonb_build_object(
             'account_id', r.other_id, 'display_name', coalesce(d.display_name, ''), 'since', r.responded_at,
             'last_activity_at', p.last_activity_at)
           order by r.responded_at desc)
      from (
        select responded_at,
               case when requester_account_id = v_uid then addressee_account_id else requester_account_id end as other_id
          from public.friend_requests
         where status = 'accepted' and (requester_account_id = v_uid or addressee_account_id = v_uid)
      ) r
      left join public.display_names d on d.account_id = r.other_id
      left join public.user_profiles p on p.account_id = r.other_id
  ), '[]'::jsonb);
end;
$$;

comment on function public.ts_friends_list() is
  '내 친구 목록(닉네임 포함).';

revoke all on function public.ts_friends_list() from public, anon;
grant execute on function public.ts_friends_list() to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_remove — 친구를 삭제한다(관계 행을 지워 다시 요청 가능한 상태로 되돌린다).
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_remove(p_friend_account_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_n   int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  delete from public.friend_requests
   where status = 'accepted'
     and ((requester_account_id = v_uid and addressee_account_id = p_friend_account_id)
       or (requester_account_id = p_friend_account_id and addressee_account_id = v_uid));
  get diagnostics v_n = row_count;

  if v_n = 0 then
    raise exception 'friend_not_found';
  end if;

  return jsonb_build_object('status', 'removed');
end;
$$;

comment on function public.ts_friend_remove(uuid) is
  '친구 삭제. 관계 행을 지워 다시 요청 가능한 상태로 되돌린다.';

revoke all on function public.ts_friend_remove(uuid) from public, anon;
grant execute on function public.ts_friend_remove(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_limits — 게임이 화면에 "1 / 100" 같은 분모를 띄우려면 상한을 알아야 한다.
--   운영 콘솔에서 바꾸는 값이라 게임에 박아 두면 바꾸는 순간 화면이 틀려진다.
--   ts_friend_settings() 는 운영 전용이라 클라이언트용으로 따로 연다(읽기만, 세 값뿐).
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_limits()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
           'max_friends',              c.max_friends,
           'max_pending_sent',         c.max_pending_sent,
           'request_cooldown_seconds', c.request_cooldown_seconds)
    from public.ts_friend_settings() c;
$$;

comment on function public.ts_friend_limits() is
  '친구 기능 제한값 읽기(클라이언트용). 값만 돌려주고 수정은 운영 전용이다.';

revoke all on function public.ts_friend_limits() from public, anon;
grant execute on function public.ts_friend_limits() to authenticated;

-- ---------------------------------------------------------------------------
-- 친구 1:1 채팅(귓속말) — 18절 채팅 인프라(chat_channels·chat_messages)를 그대로 쓴다.
-- kind='direct' 채널은 18절 주석에 "미구현"으로 남아 있던 것을 여기서 채운다: 채널 행은
-- 하나뿐이고, 실제로 대화를 가르는 건 scope_key(정렬된 계정쌍)다. 친구가 아니면 보낼 수 없다.
-- ---------------------------------------------------------------------------

insert into public.chat_channels (kind, code, display_name, max_length, slow_mode_seconds, retention_days)
values ('direct', 'direct', '귓속말', 500, 0, 30)
on conflict (code) do nothing;

create or replace function public.ts_chat_direct_scope_key(p_a uuid, p_b uuid)
returns text
language sql
immutable
as $$
  select least(p_a, p_b)::text || ':' || greatest(p_a, p_b)::text;
$$;

revoke all on function public.ts_chat_direct_scope_key(uuid, uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_chat_send_direct — 친구에게 귓속말 발송. 길이·뮤트·도배 검사는 18절 ts_chat_send와 동일.
-- ---------------------------------------------------------------------------
create or replace function public.ts_chat_send_direct(p_target_account_id uuid, p_content text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  v_user  text;
  v_name  text;
  c       public.chat_channels%rowtype;
  v_scope text;
  v_text  text;
  v_last  timestamptz;
  v_id    bigint;
  v_at    timestamptz;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_target_account_id is null then
    raise exception 'friend_target_required';
  end if;
  if p_target_account_id = v_uid then
    raise exception 'friend_self_request';
  end if;
  if not public.ts_are_friends(v_uid, p_target_account_id) then
    raise exception 'friend_not_found';
  end if;

  select * into c from public.chat_channels where code = 'direct';
  if not found or not c.is_active then
    raise exception 'chat_channel_inactive';
  end if;

  v_text := btrim(coalesce(p_content, ''));
  if v_text = '' then
    raise exception 'chat_message_empty';
  end if;
  if char_length(v_text) > c.max_length then
    raise exception 'chat_message_too_long';
  end if;
  v_text := public.ts_mask_banned_words(v_text);

  if exists (
    select 1 from public.chat_mutes m
     where m.account_id = v_uid and m.until > now()
       and (m.channel_id is null or m.channel_id = c.id)
  ) then
    raise exception 'chat_muted';
  end if;

  v_scope := public.ts_chat_direct_scope_key(v_uid, p_target_account_id);

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

comment on function public.ts_chat_send_direct(uuid, text) is
  '친구에게 귓속말 발송. 친구가 아니면 friend_not_found로 실패.';

revoke all on function public.ts_chat_send_direct(uuid, text) from public, anon;
grant execute on function public.ts_chat_send_direct(uuid, text) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_chat_fetch_direct — 특정 친구와의 대화를 커서 조회. after_id<=0이면 최근 p_limit개.
-- ---------------------------------------------------------------------------
create or replace function public.ts_chat_fetch_direct(p_target_account_id uuid, p_after_id bigint default 0, p_limit int default 50)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  v_scope text;
  c       public.chat_channels%rowtype;
  v_limit int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_target_account_id is null then
    raise exception 'friend_target_required';
  end if;

  select * into c from public.chat_channels where code = 'direct';
  if not found then
    raise exception 'chat_channel_inactive';
  end if;

  v_scope := public.ts_chat_direct_scope_key(v_uid, p_target_account_id);
  v_limit := least(greatest(coalesce(p_limit, 50), 1), 200);

  if coalesce(p_after_id, 0) <= 0 then
    return coalesce((
      select jsonb_agg(t order by t.id)
        from (
          select m.id, m.account_id, m.user_id, m.display_name,
                 case when m.deleted_at is null then m.content else null end as content,
                 (m.deleted_at is not null) as deleted, m.created_at
            from public.chat_messages m
           where m.channel_id = c.id and m.scope_key = v_scope
           order by m.id desc limit v_limit
        ) t
    ), '[]'::jsonb);
  end if;

  return coalesce((
    select jsonb_agg(t order by t.id)
      from (
        select m.id, m.account_id, m.user_id, m.display_name,
               case when m.deleted_at is null then m.content else null end as content,
               (m.deleted_at is not null) as deleted, m.created_at
          from public.chat_messages m
         where m.channel_id = c.id and m.scope_key = v_scope and m.id > p_after_id
         order by m.id limit v_limit
      ) t
  ), '[]'::jsonb);
end;
$$;

comment on function public.ts_chat_fetch_direct(uuid, bigint, int) is
  '특정 친구와의 귓속말 커서 조회. after_id<=0이면 최근 메시지부터.';

revoke all on function public.ts_chat_fetch_direct(uuid, bigint, int) from public, anon;
grant execute on function public.ts_chat_fetch_direct(uuid, bigint, int) to authenticated;

-- ---------------------------------------------------------------------------
-- 로비 채팅 — 18절 채팅 인프라를 재사용한다. kind='group' 채널 하나에 로비 id를 범위 키로
-- 써서, 그 로비 사람들끼리만 보이는 대화가 된다. 귓속말과 같은 방식이라 글자 수 제한·도배
-- 방지·운영자 삭제·보관 기간 정리가 그대로 따라온다.
--   초대가 친구로 제한되지 않으므로 로비에는 친구가 아닌 사람이 섞인다 — 귓속말(친구 전용)로는
--   대화가 안 되는 조합이라 이 채널이 필요하다.
--   보관 1일: 로비는 10분이면 닫히므로 대화를 길게 남길 이유가 없다.
-- ---------------------------------------------------------------------------
insert into public.chat_channels (kind, code, display_name, max_length, slow_mode_seconds, retention_days)
values ('group', 'lobby', '로비 채팅', 300, 1, 1)
on conflict (code) do nothing;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_chat_member — 그 로비에서 대화할 자격이 있는지. 없으면 예외.
--   수락해서 실제로 방에 들어온 사람만이다. 초대만 받은 상태는 막는다 — 열어 두면 초대가
--   "모르는 사람에게 말을 거는 통로"가 되어, 귓속말을 친구로 제한한 것이 그대로 뚫린다.
--   수락 전에 물어볼 일이 있으면 들어와서 묻고 나가면 된다(나가기는 되돌릴 수 있다).
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_chat_member(p_lobby_id uuid, p_account_id uuid)
returns void
language plpgsql
stable
security definer
set search_path = public
as $$
declare v_status text;
begin
  if not exists (select 1 from public.match_lobbies
                  where id = p_lobby_id and status in ('open', 'started')) then
    raise exception 'match_lobby_not_open';
  end if;

  select status into v_status from public.match_lobby_members
   where lobby_id = p_lobby_id and account_id = p_account_id;

  if not found or v_status <> 'accepted' then
    raise exception 'match_lobby_member_not_found';
  end if;
end;
$$;

revoke all on function public.ts_match_lobby_chat_member(uuid, uuid) from public, anon, authenticated;

create or replace function public.ts_chat_send_lobby(p_lobby_id uuid, p_content text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  v_user  text;
  v_name  text;
  c       public.chat_channels%rowtype;
  v_scope text;
  v_text  text;
  v_last  timestamptz;
  v_id    bigint;
  v_at    timestamptz;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_lobby_id is null then
    raise exception 'match_lobby_not_found';
  end if;

  perform public.ts_match_lobby_chat_member(p_lobby_id, v_uid);

  select * into c from public.chat_channels where code = 'lobby';
  if not found or not c.is_active then
    raise exception 'chat_channel_inactive';
  end if;

  v_text := btrim(coalesce(p_content, ''));
  if v_text = '' then
    raise exception 'chat_message_empty';
  end if;
  if char_length(v_text) > c.max_length then
    raise exception 'chat_message_too_long';
  end if;
  v_text := public.ts_mask_banned_words(v_text);

  if exists (
    select 1 from public.chat_mutes m
     where m.account_id = v_uid and m.until > now()
       and (m.channel_id is null or m.channel_id = c.id)
  ) then
    raise exception 'chat_muted';
  end if;

  v_scope := p_lobby_id::text;

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

comment on function public.ts_chat_send_lobby(uuid, text) is
  '로비 대화 보내기. 수락한 멤버만 가능하고(초대만 받은 상태는 불가) 로비가 닫히면 막힌다.';

revoke all on function public.ts_chat_send_lobby(uuid, text) from public, anon;
grant execute on function public.ts_chat_send_lobby(uuid, text) to authenticated;

create or replace function public.ts_chat_fetch_lobby(
  p_lobby_id uuid,
  p_after_id bigint default 0,
  p_limit    int    default 50
)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  c       public.chat_channels%rowtype;
  v_scope text;
  v_limit int := least(greatest(coalesce(p_limit, 50), 1), 200);
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_lobby_id is null then
    raise exception 'match_lobby_not_found';
  end if;

  perform public.ts_match_lobby_chat_member(p_lobby_id, v_uid);

  select * into c from public.chat_channels where code = 'lobby';
  if not found then
    raise exception 'chat_channel_not_found';
  end if;

  v_scope := p_lobby_id::text;

  if coalesce(p_after_id, 0) <= 0 then
    return coalesce((
      select jsonb_agg(t order by t.id)
        from (
          select m.id, m.account_id, m.user_id, m.display_name,
                 case when m.deleted_at is null then m.content else null end as content,
                 (m.deleted_at is not null) as deleted, m.created_at
            from public.chat_messages m
           where m.channel_id = c.id and m.scope_key = v_scope
           order by m.id desc limit v_limit
        ) t
    ), '[]'::jsonb);
  end if;

  return coalesce((
    select jsonb_agg(t order by t.id)
      from (
        select m.id, m.account_id, m.user_id, m.display_name,
               case when m.deleted_at is null then m.content else null end as content,
               (m.deleted_at is not null) as deleted, m.created_at
          from public.chat_messages m
         where m.channel_id = c.id and m.scope_key = v_scope and m.id > p_after_id
         order by m.id limit v_limit
      ) t
  ), '[]'::jsonb);
end;
$$;

comment on function public.ts_chat_fetch_lobby(uuid, bigint, int) is
  '로비 대화 커서 조회. after_id<=0이면 최근 메시지부터.';

revoke all on function public.ts_chat_fetch_lobby(uuid, bigint, int) from public, anon;
grant execute on function public.ts_chat_fetch_lobby(uuid, bigint, int) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_friend_request_cleanup — 오래 방치된 대기 요청을 취소로 돌린다. cron이 호출.
--   대기 상한(max_pending_sent)은 pending 개수로 세므로, 무시된 요청이 영원히 남으면
--   그 상한이 "평생 보낼 수 있는 요청 수"가 되어 버린다. 요청을 무시하는 건 흔한 행동이라
--   서버가 스스로 회복시켜 준다 — 로비의 expires_at·cleanup과 같은 역할이다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_friend_request_cleanup(p_days int default 30)
returns int
language sql
security definer
set search_path = public
as $$
  with stale as (
    update public.friend_requests
       set status = 'cancelled', responded_at = now()
     where status = 'pending'
       and created_at < now() - make_interval(days => greatest(coalesce(p_days, 30), 1))
    returning id
  )
  select count(*)::int from stale;
$$;

comment on function public.ts_friend_request_cleanup(int) is
  '30일 넘게 방치된 pending 친구 요청을 cancelled로 전환. cron이 하루 한 번 호출.';

revoke all on function public.ts_friend_request_cleanup(int) from public, anon, authenticated;

do $$
begin
  if exists (select 1 from pg_extension where extname = 'pg_cron') then
    perform cron.unschedule('ts_friend_request_cleanup') where exists (select 1 from cron.job where jobname = 'ts_friend_request_cleanup');
    perform cron.schedule('ts_friend_request_cleanup', '20 4 * * *', $cron$ select public.ts_friend_request_cleanup(); $cron$);
  end if;
end $$;

-- =============================================================================
-- 운영자 RPC (service_role 전용)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_admin_friend_overview — 특정 플레이어의 친구 관계 한눈에 보기.
--   "친구가 사라졌다" 같은 문의를 확인하려면 친구 목록과 대기 중 요청을 같이 봐야 한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_friend_overview(p_account_id uuid)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
    'account_id',   p_account_id,
    'display_name', coalesce((select display_name from public.display_names where account_id = p_account_id), ''),
    'friend_count', public.ts_friend_count(p_account_id),
    'max_friends',  (public.ts_friend_settings()).max_friends,
    'friends', coalesce((
      select jsonb_agg(jsonb_build_object(
               'account_id', r.other_id, 'display_name', coalesce(d.display_name, ''),
               'since', r.responded_at)
             order by r.responded_at desc)
        from (
          select responded_at,
                 case when requester_account_id = p_account_id then addressee_account_id else requester_account_id end as other_id
            from public.friend_requests
           where status = 'accepted'
             and (requester_account_id = p_account_id or addressee_account_id = p_account_id)
        ) r
        left join public.display_names d on d.account_id = r.other_id
    ), '[]'::jsonb),
    'incoming', coalesce((
      select jsonb_agg(jsonb_build_object(
               'request_id', r.id, 'account_id', r.requester_account_id,
               'display_name', coalesce(d.display_name, ''), 'created_at', r.created_at)
             order by r.created_at desc)
        from public.friend_requests r
        left join public.display_names d on d.account_id = r.requester_account_id
       where r.addressee_account_id = p_account_id and r.status = 'pending'
    ), '[]'::jsonb),
    'outgoing', coalesce((
      select jsonb_agg(jsonb_build_object(
               'request_id', r.id, 'account_id', r.addressee_account_id,
               'display_name', coalesce(d.display_name, ''), 'created_at', r.created_at)
             order by r.created_at desc)
        from public.friend_requests r
        left join public.display_names d on d.account_id = r.addressee_account_id
       where r.requester_account_id = p_account_id and r.status = 'pending'
    ), '[]'::jsonb)
  );
$$;

comment on function public.ts_admin_friend_overview(uuid) is
  '플레이어 한 명의 친구 목록·대기 요청·상한 현황. service_role 전용.';

revoke all on function public.ts_admin_friend_overview(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_friend_overview(uuid) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_friend_settings_get / _update — 제한값 조회·수정.
-- ---------------------------------------------------------------------------
-- 둘 다 jsonb 로 돌려준다. 행 타입(returns public.friend_settings)으로 두면 PostgREST 가
-- "여러 줄이 될 수 있는 것"으로 보고 배열로 감싸 보내, 운영 콘솔이 값을 못 읽는다.
-- 이 파일의 다른 운영 RPC 도 전부 jsonb 다.
drop function if exists public.ts_admin_friend_settings_get();
drop function if exists public.ts_admin_friend_settings_update(int, int, int);

create or replace function public.ts_admin_friend_settings_get()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
           'max_friends',              c.max_friends,
           'max_pending_sent',         c.max_pending_sent,
           'request_cooldown_seconds', c.request_cooldown_seconds,
           'updated_at',               c.updated_at,
           'updated_by',               c.updated_by)
    from public.ts_friend_settings() c;
$$;

comment on function public.ts_admin_friend_settings_get() is
  '친구 제한값 조회. service_role 전용.';

revoke all on function public.ts_admin_friend_settings_get() from public, anon, authenticated;
grant execute on function public.ts_admin_friend_settings_get() to service_role;

create or replace function public.ts_admin_friend_settings_update(
  p_max_friends              int,
  p_max_pending_sent         int,
  p_request_cooldown_seconds int,
  p_by                       text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare v_row public.friend_settings%rowtype;
begin
  -- 테이블 CHECK 는 하한만 본다. 상한이 없으면 오타 하나로 기능이 멈춘다 — 간격은 초 단위라
  -- 3600 을 넣으면 "최근 1시간 안에 요청한 모든 유저"가 차단되고, 그 실패는 정상 거절로
  -- 분류돼 로그에도 빨갛게 안 뜬다. 그래서 여기서 범위를 먼저 막는다.
  --   친구 수 1~1000 · 대기 요청 1~500 · 간격 0~300초(0은 "제한 없음", 화면 안내와 같다).
  -- 값을 더 키워야 하면 이 세 줄만 고치면 된다.
  if p_max_friends is null or p_max_pending_sent is null or p_request_cooldown_seconds is null
     or p_max_friends              not between 1 and 1000
     or p_max_pending_sent         not between 1 and 500
     or p_request_cooldown_seconds not between 0 and 300 then
    raise exception 'friend_settings_out_of_range';
  end if;

  insert into public.friend_settings (id, max_friends, max_pending_sent, request_cooldown_seconds, updated_at, updated_by)
  values (1, p_max_friends, p_max_pending_sent, p_request_cooldown_seconds, now(), p_by)
  on conflict (id) do update
    set max_friends              = excluded.max_friends,
        max_pending_sent         = excluded.max_pending_sent,
        request_cooldown_seconds = excluded.request_cooldown_seconds,
        updated_at               = now(),
        updated_by               = excluded.updated_by
  returning * into v_row;

  return jsonb_build_object(
           'max_friends',              v_row.max_friends,
           'max_pending_sent',         v_row.max_pending_sent,
           'request_cooldown_seconds', v_row.request_cooldown_seconds,
           'updated_at',               v_row.updated_at,
           'updated_by',               v_row.updated_by);
end;
$$;

comment on function public.ts_admin_friend_settings_update(int, int, int, text) is
  '친구 제한값 수정. 범위를 벗어나면 friend_settings_out_of_range. service_role 전용.';

revoke all on function public.ts_admin_friend_settings_update(int, int, int, text) from public, anon, authenticated;
grant execute on function public.ts_admin_friend_settings_update(int, int, int, text) to service_role;

notify pgrst, 'reload schema';


-- #############################################################################
-- 21. 매치 로비(초대)
-- #############################################################################

-- 사람을 초대해 같은 대기방(로비)에 모읍니다. 초대 대상은 친구가 아니어도 됩니다 — 닉네임으로
-- 찾아 바로 부르는 흐름을 막지 않으려는 것이고, 도배는 match_lobby_settings 가 막습니다.
-- 팀·상대·그냥 같은 방인지는 게임마다 다르므로 이 SDK는 "누가 로비에 있고 어떤 상태인지"만
-- 관리하고, 방 이름·참가자 칸(metadata) 의 의미와 실제 게임 연결은 게임이 정합니다.
-- 로비 id를 문자열로 캐스팅하면 그대로 기존 매치 결과 신고(19절, session_id)에 넘길 수 있습니다.
-- 알림은 폴링이 전제입니다 — ts_match_lobby_list_my()를 주기적으로 불러 상태 변화를 감지합니다.
-- =============================================================================
-- 매치 로비 — match_lobbies + match_lobby_members + RPC
-- 선행: 02(user_profiles, display_names), 18(chat_channels, 로비 대화에 사용)
-- =============================================================================

create table if not exists public.match_lobbies (
  id              uuid primary key default gen_random_uuid(),
  game_code       text not null,
  host_account_id uuid not null references auth.users (id) on delete cascade,
  name            text null,
  -- 게임이 자유롭게 담는 칸(맵·규칙·모드 세부 등). 서버는 내용을 해석하지 않는다.
  -- 프로젝트마다 다른 값은 컬럼을 늘리지 말고 여기에 넣는다.
  metadata        jsonb not null default '{}'::jsonb,
  status          text not null default 'open' check (status in ('open', 'started', 'cancelled', 'expired')),
  max_members     int not null default 8 check (max_members between 2 and 64),
  created_at      timestamptz not null default now(),
  -- 실제 값은 ts_match_lobby_create 가 match_lobby_settings.lobby_expire_minutes 로 덮어쓴다.
  -- 이 기본값은 그 경로를 안 타는 INSERT 를 위한 마지막 방어선일 뿐이니 운영 설정과 맞추려 하지 말 것.
  expires_at      timestamptz not null default (now() + interval '10 minutes'),
  started_at      timestamptz null,
  ended_at        timestamptz null
);

comment on table public.match_lobbies is
  '매치 로비. 초대는 친구가 아니어도 된다. id를 text로 캐스팅하면 그대로 match_results.session_id로 쓸 수 있다. name·metadata 해석은 게임 몫.';

create table if not exists public.match_lobby_members (
  lobby_id     uuid not null references public.match_lobbies (id) on delete cascade,
  account_id   uuid not null references auth.users (id) on delete cascade,
  status       text not null default 'invited' check (status in ('invited', 'accepted', 'declined', 'left')),
  -- 게임이 참가자마다 담는 칸(팀·진영·캐릭터·준비 상태 등). 서버는 내용을 해석하지 않는다.
  metadata     jsonb not null default '{}'::jsonb,
  invited_at   timestamptz not null default now(),
  responded_at timestamptz null,
  primary key (lobby_id, account_id)
);

-- 기존 설치본에서 올라오는 경로. role_tag 는 metadata 로 대체됐다.
alter table public.match_lobbies       add column if not exists name     text;
alter table public.match_lobbies       add column if not exists metadata jsonb not null default '{}'::jsonb;
alter table public.match_lobby_members add column if not exists metadata jsonb not null default '{}'::jsonb;
alter table public.match_lobby_members drop column if exists role_tag;

comment on table public.match_lobby_members is
  '로비 참가자. metadata는 서버가 의미를 두지 않는 자유 칸 — 팀이든 진영이든 게임이 정해서 쓴다.';

create index if not exists match_lobbies_host_idx on public.match_lobbies (host_account_id, status);
create index if not exists match_lobby_members_account_idx on public.match_lobby_members (account_id, status);

alter table public.match_lobbies       enable row level security;
alter table public.match_lobby_members enable row level security;

-- 두 테이블 모두 SECURITY DEFINER RPC 경유로만 접근(정책 0개) — friend_requests와 같은 패턴.
revoke all on table public.match_lobbies       from anon, authenticated;
revoke all on table public.match_lobby_members from anon, authenticated;

-- ---------------------------------------------------------------------------
-- match_lobby_settings — 초대 도배를 막는 값. 행 하나만 두고 운영자가 값만 바꾼다.
--   초대는 친구가 아니어도 보낼 수 있다(닉네임으로 찾아 바로 부르는 흐름을 막지 않으려고).
--   그래서 "아무나 부를 수 있다"가 성립하므로, 받는 쪽이 감당할 양을 여기서 제한한다.
-- ---------------------------------------------------------------------------
create table if not exists public.match_lobby_settings (
  id                           int primary key default 1 check (id = 1),
  max_pending_invites_received int not null default 20 check (max_pending_invites_received > 0),
  invite_cooldown_seconds      int not null default 2  check (invite_cooldown_seconds >= 0),
  -- 아무도 수락하지 않은 방을 언제 닫을지. 게임이 정하지 않고 운영이 정한다 — 적정 대기 시간은
  -- 플레이 패턴을 보고 조정할 값이지 클라이언트가 매번 넘길 값이 아니다.
  lobby_expire_minutes         int not null default 10 check (lobby_expire_minutes between 1 and 1440),
  updated_at                   timestamptz not null default now(),
  updated_by                   text
);

-- 아래 두 칸은 이 테이블을 만든 뒤에 추가했다. 중간 판으로 이미 설치한 DB 에는 없으므로 채운다.
alter table public.match_lobby_settings add column if not exists updated_by           text;
alter table public.match_lobby_settings add column if not exists lobby_expire_minutes int not null default 10;

-- add column 은 CHECK 를 붙여 주지 않는다. 기존 설치본에도 같은 범위가 걸리도록 따로 건다.
do $$
begin
  if not exists (
    select 1 from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public'
      and t.relname = 'match_lobby_settings'
      and c.conname = 'match_lobby_settings_lobby_expire_minutes_check'
  ) then
    alter table public.match_lobby_settings
      add constraint match_lobby_settings_lobby_expire_minutes_check
      check (lobby_expire_minutes between 1 and 1440);
  end if;
end $$;

insert into public.match_lobby_settings (id) values (1) on conflict (id) do nothing;

comment on table public.match_lobby_settings is
  '로비 운영 설정(행 하나). max_pending_invites_received=받는 쪽이 동시에 쌓아 둘 수 있는 대기 초대 수, invite_cooldown_seconds=같은 사람이 연달아 초대를 보낼 때의 최소 간격, lobby_expire_minutes=아무도 수락하지 않은 방이 닫히기까지의 시간. 기본값은 여기 한 곳에만 둔다.';

alter table public.match_lobby_settings enable row level security;

revoke all on table public.match_lobby_settings from anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_settings — 제한값을 읽는다. 기본값을 여기 다시 적지 않는다(친구 쪽과 같은 이유).
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_settings()
returns public.match_lobby_settings
language plpgsql
stable
security definer
set search_path = public
as $$
declare v_cfg public.match_lobby_settings%rowtype;
begin
  select * into v_cfg from public.match_lobby_settings where id = 1;
  if not found then
    raise exception 'match_lobby_settings_missing';
  end if;
  return v_cfg;
end;
$$;

revoke all on function public.ts_match_lobby_settings() from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_guard_invite — 초대 한 건이 허용되는지 보고, 아니면 예외를 던진다.
--   로비 생성(여러 명 한꺼번에)과 추가 초대 두 곳에서 같은 판정을 써야 해서 함수로 뺐다.
-- ---------------------------------------------------------------------------
-- p_lobby_id 가 붙어 시그니처가 바뀌었다. create or replace 는 인자 목록이 다르면 오버로드를
-- 만들 뿐이라, 기존 2인자 호출이 옛 함수로 계속 가 버린다. 먼저 지운다.
drop function if exists public.ts_match_lobby_guard_invite(uuid, uuid);

create or replace function public.ts_match_lobby_guard_invite(
  p_inviter  uuid,
  p_target   uuid,
  p_lobby_id uuid default null   -- 지금 채우고 있는 로비. 이 방 건은 도배로 세지 않는다.
)
returns void
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_cfg   public.match_lobby_settings%rowtype;
  v_count int;
  v_last  timestamptz;
begin
  if p_target = p_inviter then
    return;  -- 방장 자신은 초대가 아니라 참가다.
  end if;

  v_cfg := public.ts_match_lobby_settings();

  -- 받는 쪽이 지금 떠안고 있는 대기 초대. 끝난 로비 것은 자리를 차지하지 않으므로 뺀다.
  -- 이 방 건도 뺀다 — 안 그러면 같은 사람을 다시 초대하는 재시도가 자기 자신 때문에 막힌다.
  select count(*) into v_count
    from public.match_lobby_members m
    join public.match_lobbies l on l.id = m.lobby_id
   where m.account_id = p_target and m.status = 'invited' and l.status = 'open'
     and (p_lobby_id is null or l.id <> p_lobby_id);
  if v_count >= v_cfg.max_pending_invites_received then
    raise exception 'match_lobby_invite_limit_reached';
  end if;

  if v_cfg.invite_cooldown_seconds > 0 then
    -- 한 방을 채우는 건 한 번의 행동이다. 추가 초대 API 가 한 명씩만 받으므로, 이 방 건을
    -- 빼지 않으면 "세 명 골라 부르기" 같은 정상 흐름이 두 번째부터 전부 거절된다.
    select max(m.invited_at) into v_last
      from public.match_lobby_members m
      join public.match_lobbies l on l.id = m.lobby_id
     where l.host_account_id = p_inviter and m.account_id <> p_inviter
       and (p_lobby_id is null or l.id <> p_lobby_id);
    if v_last is not null and v_last > now() - make_interval(secs => v_cfg.invite_cooldown_seconds) then
      raise exception 'match_lobby_invite_too_fast';
    end if;
  end if;
end;
$$;

revoke all on function public.ts_match_lobby_guard_invite(uuid, uuid, uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_create — 로비를 만들고 나를 accepted로, 초대 대상을 invited로 등록한다.
-- 친구가 아니어도 초대할 수 있다 — 닉네임으로 찾아 바로 부르는 흐름을 막지 않으려는 것이고,
-- 도배는 match_lobby_settings(받는 쪽 대기 초대 상한·보내는 쪽 쿨다운)로 막는다.
-- ---------------------------------------------------------------------------
-- 파라미터가 늘어 시그니처가 바뀌었다. create or replace는 인자 목록이 다르면 교체가 아니라
-- 오버로드를 만들고, 그러면 두 개짜리 호출이 "어느 쪽인지 모르겠다"며 실패한다. 먼저 지운다.
drop function if exists public.ts_match_lobby_create(text, uuid[]);

create or replace function public.ts_match_lobby_create(
  p_game_code           text,
  p_invited_account_ids uuid[] default '{}',
  p_name                text   default null,
  p_max_members         int    default null,
  p_metadata            jsonb  default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid      uuid := auth.uid();
  v_lobby_id uuid;
  v_target   uuid;
  v_cfg      public.match_lobby_settings%rowtype;
  v_max      int;
  v_name     text;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;
  if p_game_code is null or btrim(p_game_code) = '' then
    raise exception 'match_game_code_empty';
  end if;

  -- 안 넘기면 테이블 기본값과 같은 8. 범위는 컬럼 CHECK 와 같지만 여기서 먼저 걸러 이름 붙은
  -- 사유로 돌려준다 — 안 그러면 게임이 영문 제약조건 오류를 받는다.
  v_max := coalesce(p_max_members, 8);
  if v_max not between 2 and 64 then
    raise exception 'match_lobby_max_members_invalid';
  end if;

  v_name := nullif(btrim(coalesce(p_name, '')), '');
  if v_name is not null and char_length(v_name) > 40 then
    raise exception 'match_lobby_name_too_long';
  end if;

  -- 한 명이라도 막히면 로비 자체를 안 만든다 — 절반만 초대된 방이 남는 것보다 낫다.
  foreach v_target in array coalesce(p_invited_account_ids, '{}')
  loop
    perform public.ts_match_lobby_guard_invite(v_uid, v_target);
  end loop;

  -- 만료까지의 시간은 운영이 정한다(match_lobby_settings). 게임이 매번 넘길 값이 아니다.
  v_cfg := public.ts_match_lobby_settings();

  insert into public.match_lobbies (game_code, host_account_id, name, max_members, metadata, expires_at)
  values (btrim(p_game_code), v_uid, v_name, v_max, coalesce(p_metadata, '{}'::jsonb),
          now() + make_interval(mins => v_cfg.lobby_expire_minutes))
  returning id into v_lobby_id;

  insert into public.match_lobby_members (lobby_id, account_id, status, responded_at)
  values (v_lobby_id, v_uid, 'accepted', now());

  if array_length(p_invited_account_ids, 1) > 0 then
    -- 정원은 여기서도 센다. 추가 초대(ts_match_lobby_invite)만 막아 두면 생성 한 번에 50명을
    -- 넘겨 정원 2인 방을 채울 수 있다. 호스트 자신이 한 자리를 쓰므로 +1, 같은 사람을 여러 번
    -- 넣은 배열은 아래 on conflict 로 한 명이 되니 distinct 로 센다.
    if 1 + (select count(distinct target) from unnest(p_invited_account_ids) as target
             where target is not null and target <> v_uid) > v_max then
      raise exception 'match_lobby_full';
    end if;

    insert into public.match_lobby_members (lobby_id, account_id)
    select v_lobby_id, target from unnest(p_invited_account_ids) as target
     where target is not null and target <> v_uid
    on conflict (lobby_id, account_id) do nothing;
  end if;

  return jsonb_build_object('lobby_id', v_lobby_id, 'session_id', v_lobby_id::text);
end;
$$;

comment on function public.ts_match_lobby_create(text, uuid[], text, int, jsonb) is
  '매치 로비 생성 + 초대. 초대는 친구가 아니어도 되고 도배는 match_lobby_settings가 막는다. name·metadata는 게임이 정하는 값이며 서버는 해석하지 않는다. lobby_id를 그대로 session_id로 쓸 수 있다.';

revoke all on function public.ts_match_lobby_create(text, uuid[], text, int, jsonb) from public, anon;
grant execute on function public.ts_match_lobby_create(text, uuid[], text, int, jsonb) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_invite — 진행 중(open)인 로비에 한 명을 더 초대한다(호스트 전용, 친구가 아니어도 된다).
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_invite(p_lobby_id uuid, p_account_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  v_lobby public.match_lobbies%rowtype;
  v_count int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into v_lobby from public.match_lobbies where id = p_lobby_id for update;
  if not found or v_lobby.host_account_id <> v_uid then
    raise exception 'match_lobby_not_found';
  end if;
  if v_lobby.status <> 'open' then
    raise exception 'match_lobby_not_open';
  end if;
  perform public.ts_match_lobby_guard_invite(v_uid, p_account_id, p_lobby_id);

  -- 정원은 지금 자리를 차지한 인원(초대 대기·수락)만 센다 — 거절·퇴장한 행은 남아있어도
  -- 자리를 차지하지 않는다.
  select count(*) into v_count from public.match_lobby_members
   where lobby_id = p_lobby_id and status in ('invited', 'accepted');
  if v_count >= v_lobby.max_members then
    raise exception 'match_lobby_full';
  end if;

  insert into public.match_lobby_members (lobby_id, account_id)
  values (p_lobby_id, p_account_id)
  on conflict (lobby_id, account_id) do nothing;

  return jsonb_build_object('status', 'invited');
end;
$$;

comment on function public.ts_match_lobby_invite(uuid, uuid) is
  '열려 있는 로비에 추가 초대(호스트 전용). 초대 대상은 친구가 아니어도 된다.';

revoke all on function public.ts_match_lobby_invite(uuid, uuid) from public, anon;
grant execute on function public.ts_match_lobby_invite(uuid, uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_respond — 초대받은 사람이 수락/거절한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_respond(p_lobby_id uuid, p_accept boolean)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid      uuid := auth.uid();
  v_member   public.match_lobby_members%rowtype;
  v_lobby    public.match_lobbies%rowtype;
  v_accepted int;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  -- 로비 행을 잠근다. 정원 확인과 수락 사이에 남이 끼어들면 둘 다 "자리 있음"을 보고 같이
  -- 들어가므로, 같은 로비의 수락은 여기서 한 줄로 세운다(ts_match_lobby_invite 도 같은 행을 잠근다).
  select * into v_lobby from public.match_lobbies where id = p_lobby_id for update;
  if not found or v_lobby.status <> 'open' then
    raise exception 'match_lobby_not_open';
  end if;

  select * into v_member from public.match_lobby_members
   where lobby_id = p_lobby_id and account_id = v_uid for update;
  if not found or v_member.status <> 'invited' then
    raise exception 'match_lobby_invite_not_found';
  end if;

  -- 초대받을 때는 자리가 있었어도 그사이 다른 사람들이 먼저 수락했을 수 있다. 수락하는 이 시점에
  -- 다시 센다 — 안 그러면 대기 초대가 정원보다 많을 때 먼저 누른 순서대로 정원을 넘어 들어간다.
  if p_accept then
    select count(*) into v_accepted from public.match_lobby_members
     where lobby_id = p_lobby_id and status = 'accepted';
    if v_accepted >= v_lobby.max_members then
      raise exception 'match_lobby_full';
    end if;
  end if;

  update public.match_lobby_members
     set status = case when p_accept then 'accepted' else 'declined' end,
         responded_at = now()
   where lobby_id = p_lobby_id and account_id = v_uid;

  return jsonb_build_object('status', case when p_accept then 'accepted' else 'declined' end);
end;
$$;

comment on function public.ts_match_lobby_respond(uuid, boolean) is
  '로비 초대 수락/거절.';

revoke all on function public.ts_match_lobby_respond(uuid, boolean) from public, anon;
grant execute on function public.ts_match_lobby_respond(uuid, boolean) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_leave — 멤버가 로비를 나간다. 호스트가 나가면 로비 전체를 취소한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_leave(p_lobby_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid   uuid := auth.uid();
  v_lobby public.match_lobbies%rowtype;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  select * into v_lobby from public.match_lobbies where id = p_lobby_id for update;
  if not found then
    raise exception 'match_lobby_not_found';
  end if;

  if v_lobby.host_account_id = v_uid then
    -- 이미 취소·만료된 로비를 또 "취소했다"고 응답하면 호출부가 실제로 안 바뀐 걸 성공으로
    -- 오인한다 — open·started일 때만 실제로 바꾸고, 아니면 명시적으로 실패시킨다.
    if v_lobby.status not in ('open', 'started') then
      raise exception 'match_lobby_not_open';
    end if;
    update public.match_lobbies set status = 'cancelled', ended_at = now() where id = p_lobby_id;
    return jsonb_build_object('status', 'cancelled');
  end if;

  update public.match_lobby_members set status = 'left', responded_at = now()
   where lobby_id = p_lobby_id and account_id = v_uid;
  if not found then
    raise exception 'match_lobby_member_not_found';
  end if;

  return jsonb_build_object('status', 'left');
end;
$$;

comment on function public.ts_match_lobby_leave(uuid) is
  '로비 나가기. 호스트가 나가면 로비 전체 취소.';

revoke all on function public.ts_match_lobby_leave(uuid) from public, anon;
grant execute on function public.ts_match_lobby_leave(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_set_member_meta — 참가자 칸(metadata)을 통째로 바꾼다.
--   호스트는 누구 것이든, 참가자는 자기 것만. 준비 완료처럼 본인이 바꾸는 값이 있어서
--   호스트 전용으로 두면 쓸 수 없는 흐름이 생긴다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_match_lobby_set_role(uuid, uuid, text);

create or replace function public.ts_match_lobby_set_member_meta(p_lobby_id uuid, p_account_id uuid, p_metadata jsonb)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid    uuid := auth.uid();
  v_target uuid := coalesce(p_account_id, auth.uid());
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  if v_target <> v_uid
     and not exists (select 1 from public.match_lobbies where id = p_lobby_id and host_account_id = v_uid) then
    raise exception 'match_lobby_not_found';
  end if;

  update public.match_lobby_members set metadata = coalesce(p_metadata, '{}'::jsonb)
   where lobby_id = p_lobby_id and account_id = v_target;

  if not found then
    raise exception 'match_lobby_member_not_found';
  end if;

  return jsonb_build_object('status', 'ok');
end;
$$;

comment on function public.ts_match_lobby_set_member_meta(uuid, uuid, jsonb) is
  '로비 참가자 칸 지정. 호스트는 누구 것이든, 참가자는 자기 것만. 팀·진영·준비 상태 등 의미는 게임이 정한다.';

revoke all on function public.ts_match_lobby_set_member_meta(uuid, uuid, jsonb) from public, anon;
grant execute on function public.ts_match_lobby_set_member_meta(uuid, uuid, jsonb) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_start — 호스트가 시작을 알린다. 폴링 중인 멤버가 status='started'로
-- 이를 감지한다. 실제 접속·연결은 게임이 한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_start(p_lobby_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  update public.match_lobbies
     set status = 'started', started_at = now()
   where id = p_lobby_id and host_account_id = v_uid and status = 'open';

  if not found then
    raise exception 'match_lobby_not_open';
  end if;

  return jsonb_build_object('status', 'started');
end;
$$;

comment on function public.ts_match_lobby_start(uuid) is
  '로비 시작 표시(호스트 전용). 실제 게임 연결은 게임이 처리.';

revoke all on function public.ts_match_lobby_start(uuid) from public, anon;
grant execute on function public.ts_match_lobby_start(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_cancel — 호스트가 로비를 취소한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_cancel(p_lobby_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  update public.match_lobbies
     set status = 'cancelled', ended_at = now()
   where id = p_lobby_id and host_account_id = v_uid and status = 'open';

  if not found then
    raise exception 'match_lobby_not_open';
  end if;

  return jsonb_build_object('status', 'cancelled');
end;
$$;

comment on function public.ts_match_lobby_cancel(uuid) is
  '로비 취소(호스트 전용).';

revoke all on function public.ts_match_lobby_cancel(uuid) from public, anon;
grant execute on function public.ts_match_lobby_cancel(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_list_my — 폴링용. 내가 호스트거나 멤버인 로비 + 참가자 상태를 전부 돌려준다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_list_my()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  return coalesce((
    select jsonb_agg(jsonb_build_object(
             'lobby_id', l.id, 'session_id', l.id::text, 'game_code', l.game_code,
             'name', l.name, 'metadata', l.metadata, 'max_members', l.max_members,
             'host_account_id', l.host_account_id, 'status', l.status,
             'created_at', l.created_at, 'expires_at', l.expires_at, 'started_at', l.started_at,
             'members', (
               select jsonb_agg(jsonb_build_object(
                        'account_id', m.account_id, 'display_name', coalesce(d.display_name, ''),
                        'status', m.status, 'metadata', m.metadata)
                      order by m.invited_at)
                 from public.match_lobby_members m
                 left join public.display_names d on d.account_id = m.account_id
                where m.lobby_id = l.id
             ))
           order by l.created_at desc)
      from public.match_lobbies l
     where l.status in ('open', 'started')
       and (l.host_account_id = v_uid or exists (
             select 1 from public.match_lobby_members
              where lobby_id = l.id and account_id = v_uid and status in ('invited', 'accepted')
           ))
  ), '[]'::jsonb);
end;
$$;

comment on function public.ts_match_lobby_list_my() is
  '내가 호스트·멤버인 진행 중 로비 목록(폴링용). 상태 변화 감지에 쓴다.';

revoke all on function public.ts_match_lobby_list_my() from public, anon;
grant execute on function public.ts_match_lobby_list_my() to authenticated;

-- ---------------------------------------------------------------------------
-- ts_match_lobby_cleanup — 만료된 open 로비를 정리한다. cron이 주기적으로 호출.
-- ---------------------------------------------------------------------------
create or replace function public.ts_match_lobby_cleanup()
returns int
language sql
security definer
set search_path = public
as $$
  with expired as (
    update public.match_lobbies
       set status = 'expired', ended_at = now()
     where status = 'open' and expires_at < now()
    returning id
  )
  select count(*)::int from expired;
$$;

comment on function public.ts_match_lobby_cleanup() is
  '만료된 open 로비를 expired로 전환. cron이 5분마다 호출.';

revoke all on function public.ts_match_lobby_cleanup() from public, anon, authenticated;

-- =============================================================================
-- 운영자 RPC (service_role 전용)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_admin_match_lobby_list — 로비 현황. 끝난 로비까지 보이므로 "그때 누가 있었는지"를
--   사후에 확인할 수 있다. p_status 를 주면 그 상태만, 없으면 전부.
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_match_lobby_list(
  p_status text default null,
  p_limit  int  default 50,
  p_offset int  default 0
)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with page as (
    select *
      from public.match_lobbies
     where p_status is null or status = p_status
     -- id 보조 정렬이 없으면 created_at 이 같은 로비들의 순서가 조회마다 달라져,
     -- 페이지를 넘길 때 같은 로비가 두 번 보이거나 한 개가 통째로 빠진다.
     order by created_at desc, id desc
     limit  least(greatest(coalesce(p_limit, 50), 1), 200)
    offset greatest(coalesce(p_offset, 0), 0)
  )
  select jsonb_build_object(
    'total', (select count(*) from public.match_lobbies where p_status is null or status = p_status),
    'rows', coalesce((
      select jsonb_agg(jsonb_build_object(
               'lobby_id', l.id, 'game_code', l.game_code, 'name', l.name, 'metadata', l.metadata,
               'host_account_id', l.host_account_id, 'status', l.status,
               'max_members', l.max_members, 'created_at', l.created_at,
               'started_at', l.started_at, 'ended_at', l.ended_at, 'expires_at', l.expires_at,
               'members', (
                 select coalesce(jsonb_agg(jsonb_build_object(
                          'account_id', m.account_id, 'display_name', coalesce(d.display_name, ''),
                          'status', m.status, 'metadata', m.metadata,
                          'invited_at', m.invited_at, 'responded_at', m.responded_at)
                        order by m.invited_at), '[]'::jsonb)
                   from public.match_lobby_members m
                   left join public.display_names d on d.account_id = m.account_id
                  where m.lobby_id = l.id
               ))
             order by l.created_at desc, l.id desc)
        from page l
    ), '[]'::jsonb)
  );
$$;

comment on function public.ts_admin_match_lobby_list(text, int, int) is
  '매치 로비 현황(멤버 포함). 끝난 로비도 조회된다. service_role 전용.';

revoke all on function public.ts_admin_match_lobby_list(text, int, int) from public, anon, authenticated;
grant execute on function public.ts_admin_match_lobby_list(text, int, int) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_match_lobby_settings_get / _update — 초대 제한값 조회·수정.
--   친구 제한값과 같은 모양이다. 상한을 두는 이유도 같다 — 테이블 CHECK 는 하한만 보므로,
--   운영자가 0 이나 터무니없이 큰 값을 넣어 초대를 통째로 막거나 풀어 버리지 못하게 한다.
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_match_lobby_settings_get()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
           'max_pending_invites_received', c.max_pending_invites_received,
           'invite_cooldown_seconds',      c.invite_cooldown_seconds,
           'lobby_expire_minutes',         c.lobby_expire_minutes,
           'updated_at',                   c.updated_at,
           'updated_by',                   c.updated_by)
    from public.ts_match_lobby_settings() c;
$$;

comment on function public.ts_admin_match_lobby_settings_get() is
  '로비 운영 설정 조회. service_role 전용.';

revoke all on function public.ts_admin_match_lobby_settings_get() from public, anon, authenticated;
grant execute on function public.ts_admin_match_lobby_settings_get() to service_role;

drop function if exists public.ts_admin_match_lobby_settings_update(int, int, text);

create or replace function public.ts_admin_match_lobby_settings_update(
  p_max_pending_invites_received int,
  p_invite_cooldown_seconds      int,
  p_lobby_expire_minutes         int,
  p_by                           text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare v_row public.match_lobby_settings%rowtype;
begin
  if p_max_pending_invites_received is null or p_invite_cooldown_seconds is null
     or p_lobby_expire_minutes is null
     or p_max_pending_invites_received not between 1 and 200
     or p_invite_cooldown_seconds      not between 0 and 300
     or p_lobby_expire_minutes         not between 1 and 1440 then
    raise exception 'match_lobby_settings_out_of_range';
  end if;

  insert into public.match_lobby_settings
         (id, max_pending_invites_received, invite_cooldown_seconds, lobby_expire_minutes, updated_at, updated_by)
  values (1, p_max_pending_invites_received, p_invite_cooldown_seconds, p_lobby_expire_minutes, now(), p_by)
  on conflict (id) do update
    set max_pending_invites_received = excluded.max_pending_invites_received,
        invite_cooldown_seconds      = excluded.invite_cooldown_seconds,
        lobby_expire_minutes         = excluded.lobby_expire_minutes,
        updated_at                   = now(),
        updated_by                   = excluded.updated_by
  returning * into v_row;

  return jsonb_build_object(
           'max_pending_invites_received', v_row.max_pending_invites_received,
           'invite_cooldown_seconds',      v_row.invite_cooldown_seconds,
           'lobby_expire_minutes',         v_row.lobby_expire_minutes,
           'updated_at',                   v_row.updated_at,
           'updated_by',                   v_row.updated_by);
end;
$$;

comment on function public.ts_admin_match_lobby_settings_update(int, int, int, text) is
  '로비 운영 설정 수정. 범위를 벗어나면 match_lobby_settings_out_of_range. service_role 전용.';

revoke all on function public.ts_admin_match_lobby_settings_update(int, int, int, text) from public, anon, authenticated;
grant execute on function public.ts_admin_match_lobby_settings_update(int, int, int, text) to service_role;

do $$
begin
  if exists (select 1 from pg_extension where extname = 'pg_cron') then
    perform cron.unschedule('ts_match_lobby_cleanup') where exists (select 1 from cron.job where jobname = 'ts_match_lobby_cleanup');
    perform cron.schedule('ts_match_lobby_cleanup', '*/5 * * * *', $cron$ select public.ts_match_lobby_cleanup(); $cron$);
  end if;
end $$;

notify pgrst, 'reload schema';


-- #############################################################################
-- 22. 고객센터
-- #############################################################################

-- 플레이어 문의와 운영자 답변. 게임은 SDK 의 고객센터 창(웹 페이지)을 띄우기만 하고, 화면이 엣지 함수
-- `support-api` 를 부른다. 이 절의 함수는 전부 service_role 전용이다(ts_support_unread_count 만 예외 —
-- 게임이 새 답변 표시를 띄우려고 직접 부른다). 문의는 대화 꼴이다: 운영자가 답하면 "답변 완료", 플레이어가
-- 덧붙이면 다시 "답변 대기". 운영자가 "종료"하면 더 덧붙일 수 없다.
--
-- 차단된 계정도 문의할 수 있다 — 로그인이 막혀 세션이 없으므로 support-api 가 기기에 남은 로그인 기록이나
-- 차단으로 거절된 구글·애플 로그인의 본인 증명으로 계정을 확인한다(ts_support_account_from_*).
-- 첨부 그림은 비공개 저장소 support-attachments 에 `<계정>/<무작위>.<확장자>` 로 두고, 보여 줄 때만 서명 URL 을 만든다.
-- =============================================================================
-- 고객센터 — support_categories + support_tickets + support_messages + RPC
-- 선행: 02(user_profiles, display_names)
-- =============================================================================

create table if not exists public.support_categories (
  id         serial      primary key,
  name       text        not null check (char_length(btrim(name)) between 1 and 40),
  -- 언어별 이름 {"en": "Payment"}. 없는 언어는 name 을 쓴다.
  localized  jsonb       not null default '{}'::jsonb,
  sort_order int         not null default 0,
  is_active  boolean     not null default true,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create unique index if not exists support_categories_name_key on public.support_categories (lower(btrim(name)));
alter table public.support_categories enable row level security;
revoke all on table public.support_categories from anon, authenticated;

insert into public.support_categories (name, localized, sort_order)
select v.name, v.localized, v.sort_order
  from (values
    ('결제',      '{"en": "Payment"}'::jsonb,    1),
    ('계정',      '{"en": "Account"}'::jsonb,    2),
    ('버그 신고', '{"en": "Bug report"}'::jsonb, 3),
    ('기타',      '{"en": "Other"}'::jsonb,      4)
  ) as v(name, localized, sort_order)
 where not exists (select 1 from public.support_categories);

create table if not exists public.support_tickets (
  id              uuid        primary key default gen_random_uuid(),
  -- 탈퇴로 계정이 지워져도 문의는 user_id 로 남는다.
  account_id      uuid        references auth.users (id) on delete set null,
  user_id         text        not null,
  category_id     int         references public.support_categories (id) on delete set null,
  status          text        not null default 'open' check (status in ('open', 'answered', 'closed')),
  -- 문의를 연 순간 차단 중이었는지. 콘솔이 차단 관련 문의를 가려 볼 수 있게.
  was_banned      boolean     not null default false,
  -- 기기·앱 정보(플랫폼·앱 버전·OS·기기 모델·언어). SDK 가 보낸 그대로 — 서버는 해석하지 않는다.
  meta            jsonb       not null default '{}'::jsonb,
  -- 운영자 답을 플레이어가 아직 안 읽었는지. 게임의 새 답변 표시가 이걸 센다.
  player_unread   boolean     not null default false,
  created_at      timestamptz not null default now(),
  updated_at      timestamptz not null default now(),
  last_message_at timestamptz not null default now()
);
create index if not exists support_tickets_status_idx  on public.support_tickets (status, last_message_at desc);
create index if not exists support_tickets_account_idx on public.support_tickets (account_id, last_message_at desc);
alter table public.support_tickets enable row level security;
revoke all on table public.support_tickets from anon, authenticated;

create table if not exists public.support_messages (
  id          bigint      generated always as identity primary key,
  ticket_id   uuid        not null references public.support_tickets (id) on delete cascade,
  sender      text        not null check (sender in ('player', 'admin')),
  body        text        not null default '',
  -- 저장소 경로 배열. 플레이어 메시지에만 있다.
  attachments jsonb       not null default '[]'::jsonb,
  admin_email text,
  created_at  timestamptz not null default now(),
  check (char_length(body) <= 2000),
  check (char_length(btrim(body)) > 0 or jsonb_array_length(attachments) > 0)
);
create index if not exists support_messages_ticket_idx on public.support_messages (ticket_id, id);
alter table public.support_messages enable row level security;
revoke all on table public.support_messages from anon, authenticated;

-- 첨부 그림 저장소. 비공개 — support-api·admin-api 가 서명 URL 로만 보여 준다.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('support-attachments', 'support-attachments', false, 5242880, array['image/png', 'image/jpeg', 'image/webp'])
on conflict (id) do update set
  public = false, file_size_limit = excluded.file_size_limit, allowed_mime_types = excluded.allowed_mime_types;

-- 고객센터 창 접속 표 서명 비밀값 — support-api 가 확인한 계정을 12시간짜리 표로 서명한다.
-- 설치 때 무작위로 만들어 Vault 에 둔다. 이미 있으면 다시 만들지 않는다.
do $$
begin
  if not exists (select 1 from vault.secrets where name = 'support_token_secret') then
    perform vault.create_secret(encode(extensions.gen_random_bytes(32), 'hex'), 'support_token_secret',
      '고객센터 창 접속 표 서명용. support-api 전용.');
  end if;
end $$;

create or replace function public.ts_support_token_secret()
returns text
language sql
stable
security definer
set search_path = public
as $$
  select decrypted_secret from vault.decrypted_secrets where name = 'support_token_secret';
$$;

-- ---------------------------------------------------------------------------
-- 차단된 계정의 본인 확인 — 로그인이 막혀 세션이 없을 때 support-api 가 쓴다.
-- ---------------------------------------------------------------------------
-- 기기에 남은 갱신 토큰. 아직 안 쓴(회수 안 된) 토큰만 받는다 — 기기는 마지막으로 받은 것을 들고 있다.
-- [주의] 인증 서버가 갱신 토큰을 표에 그대로 적는 형식일 때만 맞는다(2026-10-29 두 DB 확인, 12자).
--        새 형식(세션별 서명 키)으로 바뀌면 여기서 못 찾아 null 이 되고, 새 SDK 가 함께 보내는
--        마지막 접속 토큰(서명 확인)으로 넘어간다.
create or replace function public.ts_support_account_from_refresh_token(p_token text)
returns uuid
language sql
stable
security definer
set search_path = public
as $$
  select rt.user_id::uuid
    from auth.refresh_tokens rt
   where rt.token = p_token and not rt.revoked
   limit 1;
$$;

-- 구글·애플 계정 연결. p_sub 는 인증 서버가 이미 확인한 ID 토큰의 sub 다(support-api 가 확인 뒤에만 부른다).
create or replace function public.ts_support_account_from_identity(p_provider text, p_sub text)
returns uuid
language sql
stable
security definer
set search_path = public
as $$
  select i.user_id from auth.identities i where i.provider = p_provider and i.provider_id = p_sub limit 1;
$$;

-- ---------------------------------------------------------------------------
-- 플레이어 쪽 — support-api 가 확인한 계정으로 부른다(service_role).
-- ---------------------------------------------------------------------------
-- 분류 이름은 언어 코드(ko·en …)의 localized 값, 없으면 한국어가 아닌 플레이어에게는 영어, 그것도 없으면 name.
create or replace function public.ts_support_categories(p_lang text)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object('id', c.id, 'name', coalesce(nullif(c.localized ->> p_lang, ''), case when p_lang <> 'ko' then nullif(c.localized ->> 'en', '') end, c.name))
                            order by c.sort_order, c.id), '[]'::jsonb)
    from public.support_categories c
   where c.is_active;
$$;

create or replace function public.ts_support_tickets(p_account uuid, p_lang text)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'id', t.id, 'status', t.status, 'unread', t.player_unread,
           'category', coalesce(nullif(c.localized ->> p_lang, ''), case when p_lang <> 'ko' then nullif(c.localized ->> 'en', '') end, c.name),
           'preview', left(f.body, 80), 'created_at', t.created_at, 'last_message_at', t.last_message_at)
           order by t.last_message_at desc), '[]'::jsonb)
    from public.support_tickets t
    left join public.support_categories c on c.id = t.category_id
    left join lateral (
      select m.body from public.support_messages m where m.ticket_id = t.id order by m.id limit 1
    ) f on true
   where t.account_id = p_account;
$$;

-- 문의 하나와 메시지 전부. 읽었으니 새 답변 표시를 끈다.
create or replace function public.ts_support_ticket(p_account uuid, p_ticket uuid, p_lang text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v jsonb;
begin
  update public.support_tickets set player_unread = false
   where id = p_ticket and account_id = p_account and player_unread;

  select jsonb_build_object(
           'id', t.id, 'status', t.status, 'created_at', t.created_at,
           'category', coalesce(nullif(c.localized ->> p_lang, ''), case when p_lang <> 'ko' then nullif(c.localized ->> 'en', '') end, c.name),
           'messages', coalesce((
             select jsonb_agg(jsonb_build_object('id', m.id, 'sender', m.sender, 'body', m.body,
                                                 'attachments', m.attachments, 'created_at', m.created_at) order by m.id)
               from public.support_messages m where m.ticket_id = t.id), '[]'::jsonb))
    into v
    from public.support_tickets t
    left join public.support_categories c on c.id = t.category_id
   where t.id = p_ticket and t.account_id = p_account;

  if v is null then
    raise exception 'support_ticket_not_found';
  end if;
  return v;
end;
$$;

-- 첨부 경로가 그 계정 폴더의 것인지(남의 그림을 붙이지 못하게), 3장 이하인지.
create or replace function public.ts_support_check_attachments(p_account uuid, p_attachments jsonb)
returns jsonb
language plpgsql
immutable
set search_path = public
as $$
declare
  v jsonb := coalesce(p_attachments, '[]'::jsonb);
  p text;
begin
  if jsonb_typeof(v) <> 'array' or jsonb_array_length(v) > 3 then
    raise exception 'support_attachments_invalid';
  end if;
  for p in select jsonb_array_elements_text(v) loop
    if p !~ ('^' || p_account::text || '/[0-9a-f-]{36}\.(png|jpg|webp)$') then
      raise exception 'support_attachments_invalid';
    end if;
  end loop;
  return v;
end;
$$;

-- 한 계정이 한 시간에 남길 수 있는 메시지 수. 도배 방지.
create or replace function public.ts_support_guard_rate(p_account uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if (select count(*) from public.support_messages m join public.support_tickets t on t.id = m.ticket_id
       where t.account_id = p_account and m.sender = 'player' and m.created_at > now() - interval '1 hour') >= 20 then
    raise exception 'support_too_fast';
  end if;
end;
$$;

create or replace function public.ts_support_create(
  p_account uuid, p_category int, p_body text, p_attachments jsonb, p_meta jsonb, p_was_banned boolean
)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
  v_user text;
  v_att  jsonb := public.ts_support_check_attachments(p_account, p_attachments);
  v_id   uuid;
begin
  if char_length(coalesce(p_body, '')) > 2000 then
    raise exception 'support_body_too_long';
  end if;
  if char_length(btrim(coalesce(p_body, ''))) = 0 and jsonb_array_length(v_att) = 0 then
    raise exception 'support_body_empty';
  end if;
  if not exists (select 1 from public.support_categories where id = p_category and is_active) then
    raise exception 'support_category_invalid';
  end if;
  -- 답을 기다리는 문의가 쌓이면 새로 열지 못하게 한다 — 같은 내용을 여러 번 여는 걸 막는다.
  if (select count(*) from public.support_tickets where account_id = p_account and status = 'open') >= 5 then
    raise exception 'support_too_many_open';
  end if;
  perform public.ts_support_guard_rate(p_account);

  select p.user_id into v_user from public.user_profiles p where p.account_id = p_account;

  insert into public.support_tickets (account_id, user_id, category_id, was_banned, meta)
  values (p_account, coalesce(v_user, p_account::text), p_category, coalesce(p_was_banned, false),
          coalesce(p_meta, '{}'::jsonb))
  returning id into v_id;

  insert into public.support_messages (ticket_id, sender, body, attachments)
  values (v_id, 'player', btrim(coalesce(p_body, '')), v_att);
  return v_id;
end;
$$;

-- 플레이어가 덧붙이기. 종료된 문의에는 못 붙인다. 붙이면 다시 답변 대기.
create or replace function public.ts_support_reply(p_account uuid, p_ticket uuid, p_body text, p_attachments jsonb)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_att    jsonb := public.ts_support_check_attachments(p_account, p_attachments);
  v_status text;
begin
  if char_length(coalesce(p_body, '')) > 2000 then
    raise exception 'support_body_too_long';
  end if;
  if char_length(btrim(coalesce(p_body, ''))) = 0 and jsonb_array_length(v_att) = 0 then
    raise exception 'support_body_empty';
  end if;
  select status into v_status from public.support_tickets where id = p_ticket and account_id = p_account for update;
  if v_status is null then
    raise exception 'support_ticket_not_found';
  end if;
  if v_status = 'closed' then
    raise exception 'support_ticket_closed';
  end if;
  perform public.ts_support_guard_rate(p_account);

  insert into public.support_messages (ticket_id, sender, body, attachments)
  values (p_ticket, 'player', btrim(coalesce(p_body, '')), v_att);
  update public.support_tickets
     set status = 'open', updated_at = now(), last_message_at = now()
   where id = p_ticket;
end;
$$;

-- 이 계정의 문의에 실제로 붙어 있는 그림 경로. support-api 가 올리기만 하고 안 붙인 그림을 지울 때 남길 것을 가른다.
create or replace function public.ts_support_account_attachments(p_account uuid)
returns text[]
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(array_agg(distinct a.path), '{}')
    from public.support_messages m
    join public.support_tickets t on t.id = m.ticket_id
    cross join lateral jsonb_array_elements_text(m.attachments) as a(path)
   where t.account_id = p_account;
$$;

comment on function public.ts_support_account_attachments(uuid) is '이 계정 문의에 붙은 그림 경로. 안 붙인 그림 정리용. service_role 전용.';
revoke all on function public.ts_support_account_attachments(uuid) from public, anon, authenticated;
grant execute on function public.ts_support_account_attachments(uuid) to service_role;

-- ---------------------------------------------------------------------------
-- 자주 묻는 질문 — 고객센터 창 첫 화면과 문의 쓰기(고른 분류의 것)에 보인다. 운영 콘솔 "고객센터 › 자주 묻는 질문".
-- 공개 정보라 창은 접속 표 없이도 읽는다(support-api faqs). 분류를 지우지 않으므로 분류가 꺼져도 FAQ 는 남는다.
-- ---------------------------------------------------------------------------
create table if not exists public.support_faqs (
  id          serial      primary key,
  category_id int         references public.support_categories (id) on delete set null,
  question    text        not null check (char_length(btrim(question)) between 1 and 200),
  answer      text        not null check (char_length(btrim(answer)) between 1 and 4000),
  -- 언어별 문구 {"en": {"question": "...", "answer": "..."}}. 없는 언어는 한국어가 아니면 en, 그것도 없으면 위 값.
  localized   jsonb       not null default '{}'::jsonb,
  sort_order  int         not null default 0,
  is_active   boolean     not null default true,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  updated_by  text
);
create index if not exists support_faqs_order_idx on public.support_faqs (sort_order, id);
alter table public.support_faqs enable row level security;
revoke all on table public.support_faqs from anon, authenticated;

create or replace function public.ts_support_faqs(p_lang text)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with l as (
    select f.*,
           coalesce(f.localized -> p_lang, case when p_lang <> 'ko' then f.localized -> 'en' end) as loc
      from public.support_faqs f
     where f.is_active
  )
  select coalesce(jsonb_agg(jsonb_build_object(
           'id', l.id, 'category_id', l.category_id,
           'question', coalesce(nullif(l.loc ->> 'question', ''), l.question),
           'answer',   coalesce(nullif(l.loc ->> 'answer', ''),   l.answer))
           order by l.sort_order, l.id), '[]'::jsonb)
    from l;
$$;

create or replace function public.ts_admin_support_faqs()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'id', f.id, 'category_id', f.category_id, 'category', c.name, 'question', f.question, 'answer', f.answer,
           'localized', f.localized, 'sort_order', f.sort_order, 'is_active', f.is_active,
           'updated_at', f.updated_at, 'updated_by', f.updated_by)
           order by f.sort_order, f.id), '[]'::jsonb)
    from public.support_faqs f
    left join public.support_categories c on c.id = f.category_id;
$$;

-- 추가(p_id null)·바꾸기.
create or replace function public.ts_admin_support_faq_upsert(
  p_id int, p_category int, p_question text, p_answer text, p_localized jsonb, p_sort_order int, p_is_active boolean, p_by text
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if char_length(btrim(coalesce(p_question, ''))) not between 1 and 200 then
    raise exception 'support_faq_question_invalid';
  end if;
  if char_length(btrim(coalesce(p_answer, ''))) not between 1 and 4000 then
    raise exception 'support_faq_answer_invalid';
  end if;
  if p_category is not null and not exists (select 1 from public.support_categories where id = p_category) then
    raise exception 'support_category_not_found';
  end if;
  if p_id is null then
    insert into public.support_faqs (category_id, question, answer, localized, sort_order, is_active, updated_by)
    values (p_category, btrim(p_question), btrim(p_answer), coalesce(p_localized, '{}'::jsonb),
            coalesce(p_sort_order, 0), coalesce(p_is_active, true), p_by);
  else
    update public.support_faqs
       set category_id = p_category, question = btrim(p_question), answer = btrim(p_answer),
           localized = coalesce(p_localized, '{}'::jsonb), sort_order = coalesce(p_sort_order, sort_order),
           is_active = coalesce(p_is_active, is_active), updated_at = now(), updated_by = p_by
     where id = p_id;
    if not found then
      raise exception 'support_faq_not_found';
    end if;
  end if;
end;
$$;

-- FAQ 는 다른 기록이 가리키지 않아 지울 수 있다.
create or replace function public.ts_admin_support_faq_delete(p_id int)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  delete from public.support_faqs where id = p_id;
  if not found then
    raise exception 'support_faq_not_found';
  end if;
end;
$$;

-- 순서 바꾸기 — 콘솔에서 끌어 놓은 차례 그대로(p_ids 의 앞이 위). 목록에 없는 FAQ 는 건드리지 않는다.
create or replace function public.ts_admin_support_faq_reorder(p_ids int[], p_by text)
returns void
language sql
security definer
set search_path = public
as $$
  update public.support_faqs f
     set sort_order = o.ord, updated_at = now(), updated_by = p_by
    from unnest(p_ids) with ordinality as o(id, ord)
   where f.id = o.id and f.sort_order is distinct from o.ord;
$$;

comment on function public.ts_admin_support_faq_reorder(int[], text) is '자주 묻는 질문 순서 바꾸기(끌어 놓은 차례). service_role 전용.';
revoke all on function public.ts_admin_support_faq_reorder(int[], text) from public, anon, authenticated;
grant execute on function public.ts_admin_support_faq_reorder(int[], text) to service_role;

-- 켜기·끄기만 — 다른 칸은 건드리지 않는다. 화면이 불러 둔 옛 질문·답을 함께 보내면 다른 운영자의 수정을 덮는다.
create or replace function public.ts_admin_support_faq_set_active(p_id int, p_is_active boolean, p_by text)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  update public.support_faqs set is_active = p_is_active, updated_at = now(), updated_by = p_by where id = p_id;
  if not found then
    raise exception 'support_faq_not_found';
  end if;
end;
$$;

create or replace function public.ts_admin_support_category_set_active(p_id int, p_is_active boolean)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  update public.support_categories set is_active = p_is_active, updated_at = now() where id = p_id;
  if not found then
    raise exception 'support_category_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_support_faq_set_active(int, boolean, text) is '자주 묻는 질문 켜기·끄기만. service_role 전용.';
comment on function public.ts_admin_support_category_set_active(int, boolean) is '고객센터 분류 켜기·끄기만. service_role 전용.';
revoke all on function public.ts_admin_support_faq_set_active(int, boolean, text) from public, anon, authenticated;
revoke all on function public.ts_admin_support_category_set_active(int, boolean)  from public, anon, authenticated;
grant execute on function public.ts_admin_support_faq_set_active(int, boolean, text) to service_role;
grant execute on function public.ts_admin_support_category_set_active(int, boolean)  to service_role;

comment on function public.ts_support_faqs(text) is '고객센터 창의 자주 묻는 질문(켜진 것). service_role 전용.';
comment on function public.ts_admin_support_faqs() is '콘솔 자주 묻는 질문 전부. service_role 전용.';
comment on function public.ts_admin_support_faq_upsert(int, int, text, text, jsonb, int, boolean, text) is '자주 묻는 질문 추가·바꾸기. service_role 전용.';
comment on function public.ts_admin_support_faq_delete(int) is '자주 묻는 질문 지우기. service_role 전용.';

revoke all on function public.ts_support_faqs(text)                                                   from public, anon, authenticated;
revoke all on function public.ts_admin_support_faqs()                                                 from public, anon, authenticated;
revoke all on function public.ts_admin_support_faq_upsert(int, int, text, text, jsonb, int, boolean, text) from public, anon, authenticated;
revoke all on function public.ts_admin_support_faq_delete(int)                                        from public, anon, authenticated;
grant execute on function public.ts_support_faqs(text)                                                   to service_role;
grant execute on function public.ts_admin_support_faqs()                                                 to service_role;
grant execute on function public.ts_admin_support_faq_upsert(int, int, text, text, jsonb, int, boolean, text) to service_role;
grant execute on function public.ts_admin_support_faq_delete(int)                                        to service_role;

notify pgrst, 'reload schema';

-- 게임의 새 답변 표시 — 로그인한 플레이어가 직접 부른다.
create or replace function public.ts_support_unread_count()
returns int
language sql
stable
security definer
set search_path = public
as $$
  select count(*)::int from public.support_tickets where account_id = auth.uid() and player_unread;
$$;

-- ---------------------------------------------------------------------------
-- 운영 콘솔 "고객센터"
-- ---------------------------------------------------------------------------
-- 목록. p_status null = 전부. p_search 는 닉네임·user_id·계정 ID 일부.
create or replace function public.ts_admin_support_tickets(
  p_status text, p_category int, p_search text, p_limit int, p_offset int
)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  with base as (
    select t.*, c.name as category, d.display_name
      from public.support_tickets t
      left join public.support_categories c on c.id = t.category_id
      left join public.display_names d on d.account_id = t.account_id
     where (p_status is null or t.status = p_status)
       and (p_category is null or t.category_id = p_category)
       and (coalesce(btrim(p_search), '') = ''
            or d.display_name ilike '%' || btrim(p_search) || '%'
            or t.user_id ilike '%' || btrim(p_search) || '%'
            or t.account_id::text ilike '%' || btrim(p_search) || '%')
  )
  select jsonb_build_object(
    'total', (select count(*) from base),
    'counts', (select jsonb_build_object(
                 'open',     count(*) filter (where status = 'open'),
                 'answered', count(*) filter (where status = 'answered'),
                 'closed',   count(*) filter (where status = 'closed'))
               from public.support_tickets),
    'rows', coalesce((
      select jsonb_agg(jsonb_build_object(
               'id', b.id, 'account_id', b.account_id, 'user_id', b.user_id, 'display_name', b.display_name,
               'category', b.category, 'status', b.status, 'was_banned', b.was_banned,
               'preview', left((select m.body from public.support_messages m where m.ticket_id = b.id order by m.id limit 1), 80),
               'created_at', b.created_at, 'last_message_at', b.last_message_at)
               order by b.last_message_at desc)
        from (select * from base order by last_message_at desc
              limit greatest(1, least(coalesce(p_limit, 50), 200)) offset greatest(coalesce(p_offset, 0), 0)) b),
      '[]'::jsonb));
$$;

create or replace function public.ts_admin_support_ticket(p_ticket uuid)
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select jsonb_build_object(
           'id', t.id, 'account_id', t.account_id, 'user_id', t.user_id, 'display_name', d.display_name,
           'category', c.name, 'category_id', t.category_id, 'status', t.status, 'was_banned', t.was_banned,
           'meta', t.meta, 'created_at', t.created_at, 'last_message_at', t.last_message_at,
           'messages', coalesce((
             select jsonb_agg(jsonb_build_object('id', m.id, 'sender', m.sender, 'body', m.body,
                                                 'attachments', m.attachments, 'admin_email', m.admin_email,
                                                 'created_at', m.created_at) order by m.id)
               from public.support_messages m where m.ticket_id = t.id), '[]'::jsonb))
    from public.support_tickets t
    left join public.support_categories c on c.id = t.category_id
    left join public.display_names d on d.account_id = t.account_id
   where t.id = p_ticket;
$$;

-- 운영자 답변. 답하면 "답변 완료"가 되고 플레이어 쪽에 새 답변 표시가 켜진다.
create or replace function public.ts_admin_support_reply(p_ticket uuid, p_body text, p_by text)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_status text;
begin
  if char_length(btrim(coalesce(p_body, ''))) = 0 then
    raise exception 'support_body_empty';
  end if;
  if char_length(p_body) > 2000 then
    raise exception 'support_body_too_long';
  end if;
  -- 상태를 잠그고 본다 — 다른 운영자가 같은 순간 "종료"하면 답이 그걸 덮어 말없이 다시 열 수 있다.
  select status into v_status from public.support_tickets where id = p_ticket for update;
  if v_status is null then
    raise exception 'support_ticket_not_found';
  end if;
  -- 종료한 문의에 답하면 말없이 다시 열린다 — 콘솔이 먼저 "다시 열기"를 하게 한다.
  if v_status = 'closed' then
    raise exception 'support_ticket_closed';
  end if;
  insert into public.support_messages (ticket_id, sender, body, admin_email)
  values (p_ticket, 'admin', btrim(p_body), p_by);
  update public.support_tickets
     set status = 'answered', player_unread = true, updated_at = now(), last_message_at = now()
   where id = p_ticket;
end;
$$;

-- 상태 바꾸기 — 주로 종료. 다시 열 수도 있다.
create or replace function public.ts_admin_support_set_status(p_ticket uuid, p_status text)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_status not in ('open', 'answered', 'closed') then
    raise exception 'support_status_invalid';
  end if;
  update public.support_tickets set status = p_status, updated_at = now() where id = p_ticket;
  if not found then
    raise exception 'support_ticket_not_found';
  end if;
end;
$$;

create or replace function public.ts_admin_support_categories()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(jsonb_agg(jsonb_build_object(
           'id', c.id, 'name', c.name, 'localized', c.localized, 'sort_order', c.sort_order, 'is_active', c.is_active,
           'tickets', (select count(*) from public.support_tickets t where t.category_id = c.id))
           order by c.sort_order, c.id), '[]'::jsonb)
    from public.support_categories c;
$$;

-- 분류 추가(p_id null)·바꾸기. 지우지 않는다 — 옛 문의가 분류를 잃지 않게 끄기만 한다.
create or replace function public.ts_admin_support_category_upsert(
  p_id int, p_name text, p_localized jsonb, p_sort_order int, p_is_active boolean
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if char_length(btrim(coalesce(p_name, ''))) not between 1 and 40 then
    raise exception 'support_category_name_invalid';
  end if;
  if exists (select 1 from public.support_categories
              where lower(btrim(name)) = lower(btrim(p_name)) and (p_id is null or id <> p_id)) then
    raise exception 'support_category_name_taken';
  end if;
  if p_id is null then
    insert into public.support_categories (name, localized, sort_order, is_active)
    values (btrim(p_name), coalesce(p_localized, '{}'::jsonb), coalesce(p_sort_order, 0), coalesce(p_is_active, true));
  else
    update public.support_categories
       set name = btrim(p_name), localized = coalesce(p_localized, '{}'::jsonb), sort_order = coalesce(p_sort_order, sort_order),
           is_active = coalesce(p_is_active, is_active), updated_at = now()
     where id = p_id;
    if not found then
      raise exception 'support_category_not_found';
    end if;
  end if;
end;
$$;

comment on function public.ts_support_token_secret() is '고객센터 창 접속 표 서명 비밀값(복호화). support-api 전용 — service_role 만.';
comment on function public.ts_support_account_from_refresh_token(text) is '기기에 남은 갱신 토큰 → 계정. 차단된 계정의 본인 확인용. service_role 전용.';
comment on function public.ts_support_account_from_identity(text, text) is '구글·애플 계정 연결 → 계정. 인증 서버가 확인한 ID 토큰의 sub 로만 부른다. service_role 전용.';
comment on function public.ts_support_categories(text) is '고객센터 분류(켜진 것). service_role 전용.';
comment on function public.ts_support_tickets(uuid, text) is '플레이어 문의 목록. service_role 전용.';
comment on function public.ts_support_ticket(uuid, uuid, text) is '플레이어 문의 하나 + 메시지. 새 답변 표시를 끈다. service_role 전용.';
comment on function public.ts_support_check_attachments(uuid, jsonb) is '첨부 경로 검사(그 계정 폴더·3장 이하). 내부 헬퍼.';
comment on function public.ts_support_guard_rate(uuid) is '한 시간 20개 메시지 제한. 내부 헬퍼.';
comment on function public.ts_support_create(uuid, int, text, jsonb, jsonb, boolean) is '문의 열기. service_role 전용.';
comment on function public.ts_support_reply(uuid, uuid, text, jsonb) is '플레이어 덧붙이기. service_role 전용.';
comment on function public.ts_support_unread_count() is '새 답변이 있는 내 문의 수. 게임이 직접 부른다.';
comment on function public.ts_admin_support_tickets(text, int, text, int, int) is '콘솔 고객센터 목록. service_role 전용.';
comment on function public.ts_admin_support_ticket(uuid) is '콘솔 고객센터 문의 하나. service_role 전용.';
comment on function public.ts_admin_support_reply(uuid, text, text) is '운영자 답변. service_role 전용.';
comment on function public.ts_admin_support_set_status(uuid, text) is '문의 상태 바꾸기. service_role 전용.';
comment on function public.ts_admin_support_categories() is '고객센터 분류 전부. service_role 전용.';
comment on function public.ts_admin_support_category_upsert(int, text, jsonb, int, boolean) is '고객센터 분류 추가·바꾸기. service_role 전용.';

revoke all on function public.ts_support_token_secret()                                      from public, anon, authenticated;
revoke all on function public.ts_support_account_from_refresh_token(text)                    from public, anon, authenticated;
revoke all on function public.ts_support_account_from_identity(text, text)                   from public, anon, authenticated;
revoke all on function public.ts_support_categories(text)                                    from public, anon, authenticated;
revoke all on function public.ts_support_tickets(uuid, text)                                 from public, anon, authenticated;
revoke all on function public.ts_support_ticket(uuid, uuid, text)                            from public, anon, authenticated;
revoke all on function public.ts_support_check_attachments(uuid, jsonb)                      from public, anon, authenticated;
revoke all on function public.ts_support_guard_rate(uuid)                                    from public, anon, authenticated;
revoke all on function public.ts_support_create(uuid, int, text, jsonb, jsonb, boolean)      from public, anon, authenticated;
revoke all on function public.ts_support_reply(uuid, uuid, text, jsonb)                      from public, anon, authenticated;
revoke all on function public.ts_support_unread_count()                                      from public, anon, authenticated;
revoke all on function public.ts_admin_support_tickets(text, int, text, int, int)            from public, anon, authenticated;
revoke all on function public.ts_admin_support_ticket(uuid)                                  from public, anon, authenticated;
revoke all on function public.ts_admin_support_reply(uuid, text, text)                       from public, anon, authenticated;
revoke all on function public.ts_admin_support_set_status(uuid, text)                        from public, anon, authenticated;
revoke all on function public.ts_admin_support_categories()                                  from public, anon, authenticated;
revoke all on function public.ts_admin_support_category_upsert(int, text, jsonb, int, boolean) from public, anon, authenticated;

grant execute on function public.ts_support_token_secret()                                   to service_role;
grant execute on function public.ts_support_account_from_refresh_token(text)                 to service_role;
grant execute on function public.ts_support_account_from_identity(text, text)                to service_role;
grant execute on function public.ts_support_categories(text)                                 to service_role;
grant execute on function public.ts_support_tickets(uuid, text)                              to service_role;
grant execute on function public.ts_support_ticket(uuid, uuid, text)                         to service_role;
grant execute on function public.ts_support_create(uuid, int, text, jsonb, jsonb, boolean)   to service_role;
grant execute on function public.ts_support_reply(uuid, uuid, text, jsonb)                   to service_role;
grant execute on function public.ts_support_unread_count()                                   to authenticated;
grant execute on function public.ts_admin_support_tickets(text, int, text, int, int)         to service_role;
grant execute on function public.ts_admin_support_ticket(uuid)                               to service_role;
grant execute on function public.ts_admin_support_reply(uuid, text, text)                    to service_role;
grant execute on function public.ts_admin_support_set_status(uuid, text)                     to service_role;
grant execute on function public.ts_admin_support_categories()                               to service_role;
grant execute on function public.ts_admin_support_category_upsert(int, text, jsonb, int, boolean) to service_role;

notify pgrst, 'reload schema';


-- #############################################################################
-- 23. 클라이언트 권한 최소화
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
grant select, insert, update, delete on table public.user_sessions to authenticated;
grant select, insert, update, delete on table public.user_data     to authenticated;

-- 프로필 — 읽기만, 칸 단위(2절과 같은 칸). 쓰기는 전부 SECURITY DEFINER 함수가 한다.
grant select (id, user_id, account_id, server_id, withdrawn_at) on table public.user_profiles to authenticated;

-- 클라이언트 권한을 남기지 않는 테이블(정책 0개 = RLS 가 이미 전면 차단):
--   account_closures, apple_auth_tokens, anonymous_recovery_tokens, mail_batches, mail_categories,
--   mail_schedules, user_ban_messages, user_data_logs,
--   match_reward_configs, match_results, friend_requests, friend_settings,
--   match_lobbies, match_lobby_members, match_lobby_settings

-- ---------------------------------------------------------------------------
-- 3. 함수 — 전부 회수 후 SDK 가 실제로 호출하는 RPC 만 되돌려 준다
--    트리거 함수도 회수한다 — 트리거가 실행될 때는 EXECUTE 를 보지 않으므로 동작은 같고,
--    빼 두면 프로젝트마다 기본 권한 차이가 그대로 남아 두 DB 가 갈라진다.
-- ---------------------------------------------------------------------------
do $$
declare r record;
begin
  for r in
    select p.oid::regprocedure as sig
      from pg_proc p join pg_namespace n on n.oid = p.pronamespace
     where n.nspname = 'public'
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
grant execute on function public.ts_display_name_check(text, uuid)                           to authenticated;
grant execute on function public.ts_anon_recovery_get_refresh_token(text, text)              to anon, authenticated;
grant execute on function public.ts_anon_recovery_delete_by_fingerprint(text, text)          to anon, authenticated;
grant execute on function public.ts_anon_recovery_upsert_refresh_token(text, text, uuid, text) to anon, authenticated;
-- 요청 제한 사전 검사(10절) — 모든 데이터 요청 앞에서 돈다. 빠지면 그 역할의 요청이 전부 실패한다.
grant execute on function public.ts_request_limit_check()                                    to anon, authenticated, service_role;
grant execute on function public.ts_request_limit_ping()                                     to authenticated;

-- 로그인 사용자 전용 RPC
grant execute on function public.ts_ensure_my_profile(text, text)          to authenticated;
grant execute on function public.ts_ensure_my_row(text, text)              to authenticated;
grant execute on function public.ts_my_server_id()                         to authenticated;
grant execute on function public.ts_my_withdrawal_status()                 to authenticated;
grant execute on function public.ts_after_sign_in(text, text)              to authenticated;
grant execute on function public.ts_session_heartbeat(integer)             to authenticated;
grant execute on function public.ts_request_withdrawal(integer)            to authenticated;
grant execute on function public.ts_transfer_my_server(text, text)         to authenticated;
grant execute on function public.ts_delete_my_anon_recovery_tokens()       to authenticated;
grant execute on function public.ts_view_mail_for_user(uuid)               to authenticated;
grant execute on function public.ts_claim_mail_items(uuid)                 to authenticated;
grant execute on function public.ts_claim_all_mail_items(text)             to authenticated;
grant execute on function public.ts_delete_mail_for_user(uuid)             to authenticated;
grant execute on function public.ts_delete_claimed_mails_for_user(text)    to authenticated;
grant execute on function public.ts_mail_inbox_counts()                    to authenticated;
grant execute on function public.ts_mail_client_settings()                 to authenticated;

-- 본인 우편 RPC는 auth.uid() 기반이라 service_role로는 동작하지 않는다.
-- 프로젝트 기본 권한 차이와 무관하게 같은 상태가 되도록 명시적으로 회수한다.
revoke execute on function public.ts_view_mail_for_user(uuid)   from service_role;
revoke execute on function public.ts_claim_mail_items(uuid)     from service_role;
revoke execute on function public.ts_delete_mail_for_user(uuid) from service_role;
revoke execute on function public.ts_mail_inbox_counts()        from service_role;

-- 인앱 결제 (07_purchases.sql)
grant execute on function public.ts_mark_purchase_granted(text)                       to authenticated;

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

-- 쿠폰
grant execute on function public.ts_coupon_redeem(text)                               to authenticated;

-- 채팅
grant execute on function public.ts_chat_send(text, text)                             to authenticated;
grant execute on function public.ts_chat_fetch_many(jsonb, int)                        to authenticated;
grant execute on function public.ts_chat_channels()                                   to authenticated;
grant execute on function public.ts_chat_send_direct(uuid, text)                      to authenticated;
grant execute on function public.ts_chat_fetch_direct(uuid, bigint, int)              to authenticated;
grant execute on function public.ts_chat_send_lobby(uuid, text)                       to authenticated;
grant execute on function public.ts_chat_fetch_lobby(uuid, bigint, int)               to authenticated;

-- 매치 결과
grant execute on function public.ts_match_report_result(text, text, boolean, uuid, double precision) to authenticated;
grant execute on function public.ts_match_report_solo_result(text, text, boolean, double precision) to authenticated;
grant execute on function public.ts_match_stats(text)                                 to authenticated;
grant execute on function public.ts_match_history(text, int, timestamptz)             to authenticated;

-- 친구
grant execute on function public.ts_friend_search(text)                              to authenticated;
grant execute on function public.ts_friend_request_send(uuid)                        to authenticated;
grant execute on function public.ts_friend_requests_list(text)                       to authenticated;
grant execute on function public.ts_friend_request_respond(uuid, boolean)            to authenticated;
grant execute on function public.ts_friend_request_cancel(uuid)                      to authenticated;
grant execute on function public.ts_friends_list()                                   to authenticated;
grant execute on function public.ts_friend_remove(uuid)                              to authenticated;
grant execute on function public.ts_friend_limits()                                   to authenticated;

-- 매치 로비(초대)
grant execute on function public.ts_match_lobby_create(text, uuid[], text, int, jsonb) to authenticated;
grant execute on function public.ts_match_lobby_invite(uuid, uuid)                   to authenticated;
grant execute on function public.ts_match_lobby_respond(uuid, boolean)               to authenticated;
grant execute on function public.ts_match_lobby_leave(uuid)                          to authenticated;
grant execute on function public.ts_match_lobby_set_member_meta(uuid, uuid, jsonb)   to authenticated;
grant execute on function public.ts_match_lobby_start(uuid)                          to authenticated;
grant execute on function public.ts_match_lobby_cancel(uuid)                         to authenticated;
grant execute on function public.ts_match_lobby_list_my()                            to authenticated;

-- 고객센터 — 게임의 새 답변 표시
grant execute on function public.ts_support_unread_count()                           to authenticated;

-- 클라이언트에 열지 않는 함수(운영·cron·트리거 전용). postgres·service_role 로만 호출합니다:
--   admin_add_user_data_column / admin_drop_user_data_column / admin_update_user_data_column
--   ts_protect_field / ts_unprotect_field  ← SECURITY DEFINER 로 CHECK 제약을 조작한다.
--                                            열려 있으면 플레이어가 재화 최소값 보호를 해제할 수 있다.
--   ts_run_due_mail_schedules / ts_withdrawal_sweep_kick / ts_cleanup_expired_mails
--   ts_admin_* (우편 발송·카탈로그·리더보드·매치 보상 설정·플레이어 조회), rls_auto_enable, ts_mail_schedule_next_run
--   ts_leaderboard_rotate_due / ts_leaderboard_next_rotation_at / ts_leaderboard_columns_of
--   ts_admin_leaderboard_*  ← 리더보드 정의·점수 정정·컬럼 DDL. 열리면 클라이언트가
--                             리더보드를 지우거나 점수를 조작할 수 있다.
--   ts_match_lobby_cleanup / ts_friend_request_cleanup ← cron 전용(만료 로비·방치 요청 정리).
--   ts_chat_direct_scope_key ← 내부 헬퍼(귓속말 범위 키 계산).
--   ts_are_friends / ts_friend_count / ts_friend_settings ← 내부 헬퍼(친구 판정·수·제한값).
--   ts_match_lobby_settings / ts_match_lobby_guard_invite / ts_match_lobby_chat_member
--                          ← 내부 헬퍼(로비 초대 제한값·초대 판정·대화 자격).
--   ts_admin_friend_overview / ts_admin_friend_settings_* / ts_admin_match_lobby_list ← 운영 콘솔 전용.
--   ts_support_* (ts_support_unread_count 제외) ← 고객센터 창의 엣지 함수(support-api) 전용.

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
--  where n.nspname = 'public'
--    and (has_function_privilege('anon', p.oid, 'EXECUTE')
--         or has_function_privilege('authenticated', p.oid, 'EXECUTE'))
--  order by anon desc, p.proname;
--
-- 3) 기본 권한에서 anon·authenticated 가 빠졌는지 (postgres 행에 둘이 없어야 함)
-- select defaclrole::regrole::text as grantor, defaclobjtype, defaclacl::text
--   from pg_default_acl where defaclnamespace::regnamespace::text = 'public';
