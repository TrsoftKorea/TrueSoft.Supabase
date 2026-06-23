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
