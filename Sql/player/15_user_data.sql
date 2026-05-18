-- user_data — 표준 유저 세이브 테이블 (단일 테이블 방식)
-- 모든 게임 세이브 데이터를 하나의 테이블에 저장합니다.
-- 새 컬럼은 ALTER TABLE ADD COLUMN으로 추가합니다.
-- [append-only 정책] 컬럼 삭제·이름 변경은 금지합니다.
--
-- =============================================================================
-- user_data 테이블 생성
-- 선행: 04_user_saves.sql (set_updated_at, ts_update_last_activity_at)
-- =============================================================================

create table if not exists public.user_data (
  id         uuid        primary key default gen_random_uuid(),
  user_id    text        not null,
  account_id uuid        not null unique references auth.users(id) on delete cascade,
  server_id  uuid        not null references public.game_servers(id),
  updated_at timestamptz not null default now(),

  -- ─── 게임 데이터 컬럼 ─────────────────────────────────────────────────────
  -- 새 컬럼 추가: ALTER TABLE public.user_data ADD COLUMN <이름> <타입> NOT NULL DEFAULT <기본값>;
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
