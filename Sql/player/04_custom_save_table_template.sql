-- =============================================================================
-- 커스텀 유저 세이브 테이블 템플릿
-- 선행: 04_user_saves.sql (ts_ensure_my_row)
--
-- 사용법:
--   1. 이 파일을 복사해서 프로젝트에 맞는 이름으로 저장합니다.
--   2. 모든 'custom_saves'를 실제 테이블명으로 바꿉니다.
--   3. "게임 데이터 컬럼" 섹션에 프로젝트 전용 컬럼을 추가합니다.
--   4. C# 모델 클래스에 [DataTable("테이블명")] 어트리뷰트를 붙입니다.
--
-- 구조 요구사항 (SDK 필수):
--   - account_id uuid unique    → RLS 및 ts_ensure_my_row 기준
--   - user_id text              → 운영·감사용 플레이어 고유 id
--   - server_id uuid            → 서버 샤드 (ts_default_server_id() 기반)
--   - updated_at timestamptz    → SDK가 자동으로 갱신
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 테이블 생성
-- ---------------------------------------------------------------------------
create table if not exists public.custom_saves (
  id uuid primary key default gen_random_uuid(),
  user_id text not null,
  account_id uuid unique references auth.users (id) on delete set null,
  server_id uuid references public.game_servers (id) on delete restrict,
  updated_at timestamptz not null default now()

  -- 여기에 게임 데이터 컬럼을 추가합니다. 예:
  -- ,level integer not null default 1
  -- ,exp bigint not null default 0
  -- ,gold integer not null default 0
);

comment on table public.custom_saves is '커스텀 유저 세이브. account_id RLS 기반.';
comment on column public.custom_saves.user_id is '플레이어 고유 id (운영·감사용).';
comment on column public.custom_saves.account_id is 'auth.users.id. 탈퇴 시 NULL.';
comment on column public.custom_saves.server_id is '세이브가 속한 서버 id.';

-- ---------------------------------------------------------------------------
-- 인덱스
-- ---------------------------------------------------------------------------
create index if not exists custom_saves_user_id_idx on public.custom_saves (user_id);
create index if not exists custom_saves_account_id_idx on public.custom_saves (account_id)
  where account_id is not null;
create index if not exists custom_saves_server_id_idx on public.custom_saves (server_id);

-- ---------------------------------------------------------------------------
-- Row Level Security
-- ---------------------------------------------------------------------------
alter table public.custom_saves enable row level security;

drop policy if exists "custom_saves_select_own" on public.custom_saves;
drop policy if exists "custom_saves_insert_own" on public.custom_saves;
drop policy if exists "custom_saves_update_own" on public.custom_saves;

create policy "custom_saves_select_own"
on public.custom_saves for select
using (account_id = auth.uid());

create policy "custom_saves_insert_own"
on public.custom_saves for insert
with check (
  account_id = auth.uid()
  and server_id is not null
  and server_id = public.auth_user_server_id()
);

create policy "custom_saves_update_own"
on public.custom_saves for update
using (account_id = auth.uid())
with check (
  server_id is not null
  and server_id = public.auth_user_server_id()
);
