-- 애널리틱스 이벤트 추적 테이블입니다.
-- 세션 시작·종료·keep-alive와 임의 이벤트(광고 재생 등)를 기록합니다.
-- INSERT/UPDATE는 클라이언트(RLS: account_id = auth.uid())가 직접 수행합니다.
--
-- =============================================================================
-- 애널리틱스 세션 테이블 (analytics_sessions)
-- 선행: 없음 (auth.users 참조만 필요)
--
-- 로그인 시 세션이 자동 생성되고, 앱 실행 중 5분마다 last_active_at이 갱신됩니다.
-- 앱 종료·백그라운드 전환 시 is_closed = true, ended_at이 설정됩니다.
-- =============================================================================

create table if not exists public.analytics_sessions (
  id             bigint generated always as identity primary key,
  session_id     text        not null unique,
  account_id     uuid        references auth.users(id) on delete set null,
  user_id        text,
  started_at     timestamptz not null default now(),
  last_active_at timestamptz not null default now(),
  ended_at       timestamptz,
  platform       text,        -- 'android' | 'ios' | 'windows' | 'macos' | ...
  app_version    text,
  is_closed      boolean     not null default false
);

comment on table public.analytics_sessions is
  '앱 세션 라이프사이클. 로그인 시 자동 생성되고 앱 종료 시 닫힙니다.';

alter table public.analytics_sessions enable row level security;

drop policy if exists "sessions_insert_own" on public.analytics_sessions;
create policy "sessions_insert_own"
  on public.analytics_sessions for insert
  with check (auth.uid() = account_id);

drop policy if exists "sessions_update_own" on public.analytics_sessions;
create policy "sessions_update_own"
  on public.analytics_sessions for update
  using (auth.uid() = account_id);

-- =============================================================================
-- 애널리틱스 이벤트 테이블 (analytics_events)
-- 선행: 없음 (auth.users 참조만 필요)
--
-- 이름 기반 이벤트를 기록합니다. 광고 재생, 레벨 클리어 등 임의 이벤트에 사용합니다.
-- account_id / user_id / session_id는 SDK가 자동으로 주입합니다.
-- =============================================================================

create table if not exists public.analytics_events (
  id          bigint generated always as identity primary key,
  account_id  uuid        references auth.users(id) on delete set null,
  user_id     text,
  session_id  text,
  event_name  text        not null,
  event_time  timestamptz not null default now()
);

comment on table public.analytics_events is
  '광고 재생·레벨 클리어 등 이름 기반 이벤트 기록.';

alter table public.analytics_events enable row level security;

drop policy if exists "events_insert_own" on public.analytics_events;
create policy "events_insert_own"
  on public.analytics_events for insert
  with check (auth.uid() = account_id);
