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
  rotation_tz             text        not null default 'Asia/Seoul',
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
  extra_data        text,
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

-- leaderboard_scores 만 SELECT 를 남긴다. 데이터 접근용이 아니라 **스키마 노출용**이다.
--   · 클래스 생성기가 PostgREST OpenAPI 에서 컬럼·타입을 읽으려면 테이블이 노출돼야 한다.
--   · RLS 가 켜져 있고 SELECT 정책이 0개라 실제 행은 한 건도 반환되지 않는다(검증 완료).
--   · 순위·기록 조회는 전부 SECURITY DEFINER RPC 를 통한다.
grant select on table public.leaderboard_scores to authenticated;


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
  p_tz             text        default 'Asia/Seoul',
  p_from           timestamptz default now()
)
returns timestamptz
language plpgsql
stable
as $$
declare
  v_tz     text        := coalesce(nullif(btrim(p_tz), ''), 'Asia/Seoul');
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
  p_extra_data text  default null,
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
      (table_code, rotation_count, account_id, user_id, server_id, score, extra_data,
       score_achieved_at, first_recorded_at, score_count, updated_at %s)
    select $1, $2, $3, $4, $5, $6, $7, now(), now(), 1, now() %s
    from jsonb_populate_record(null::public.leaderboard_scores, coalesce($8, '{}'::jsonb)) d
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
      extra_data  = coalesce(excluded.extra_data, leaderboard_scores.extra_data),
      server_id   = excluded.server_id,
      user_id     = excluded.user_id,
      score_count = leaderboard_scores.score_count + 1,
      updated_at  = now() %s
    returning score
  $f$, v_ins, v_sel, t.record_type, t.record_type, v_upd);

  execute v_sql
    into v_new
    using t.code, t.rotation_count, v_uid, v_user, v_srv, p_score, p_extra_data, p_data;

  return jsonb_build_object('score', v_new, 'rotation_count', t.rotation_count);
end;
$$;

comment on function public.ts_leaderboard_submit_score(text,numeric,text,jsonb) is
  '본인 점수 기록. record_type(최고/최저/최신/누적) 적용, 점수가 바뀔 때만 score_achieved_at 갱신. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_submit_score(text,numeric,text,jsonb) from public, anon;
grant execute on function public.ts_leaderboard_submit_score(text,numeric,text,jsonb) to authenticated;

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
             'extra_data',     r.extra_data,
             'rotation_count', r.rotation_count,
             'data',           %s) order by r.rnk), '[]'::jsonb)
    from (
      select s.account_id, s.user_id, s.score, s.extra_data, s.rotation_count,
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
    'extra_data',     s.extra_data,
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
  p_extra_data     text  default null,
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
    set extra_data = coalesce($1, s.extra_data),
        updated_at = now() %s
    from jsonb_populate_record(null::public.leaderboard_scores, coalesce($2, '{}'::jsonb)) d
    where s.table_code = $3 and s.rotation_count = $4 and s.account_id = $5
  $f$, v_set)
  using p_extra_data, p_data, t.code, v_rot, v_uid;

  get diagnostics v_n = row_count;
  if v_n = 0 then
    raise exception 'leaderboard_score_not_found';
  end if;
end;
$$;

comment on function public.ts_leaderboard_set_player_data(text,text,jsonb,int) is
  '본인 추가 데이터·등록 컬럼 수정. 점수는 바꾸지 않는다. SECURITY DEFINER.';

revoke all on function public.ts_leaderboard_set_player_data(text,text,jsonb,int) from public, anon;
grant execute on function public.ts_leaderboard_set_player_data(text,text,jsonb,int) to authenticated;

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

  v_rot := coalesce(p_rotation_count, t.rotation_count);

  delete from public.leaderboard_scores
  where table_code = t.code and rotation_count = v_rot and account_id = v_uid;
end;
$$;

comment on function public.ts_leaderboard_delete_my_score(text,int) is
  '본인 기록 삭제. 없으면 no-op. SECURITY DEFINER.';

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
  p_tz              text        default 'Asia/Seoul',
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
     v_anchor, coalesce(nullif(btrim(p_tz), ''), 'Asia/Seoul'), v_next, p_ends_at, coalesce(p_is_active, true))
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
  p_extra_data     text  default null,
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
    set extra_data = coalesce($1, s.extra_data),
        updated_at = now() %s
    from jsonb_populate_record(null::public.leaderboard_scores, coalesce($2, '{}'::jsonb)) d
    where s.table_code = $3 and s.rotation_count = $4 and s.account_id = $5
  $f$, v_set)
  using p_extra_data, p_data, t.code, v_rot, p_account_id;

  get diagnostics v_n = row_count;
  if v_n = 0 then
    raise exception 'leaderboard_score_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_leaderboard_set_player_data(text,uuid,text,jsonb,int) is
  '운영이 특정 플레이어의 추가 데이터·등록 컬럼을 수정(어드민).';

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
as $$
declare
  colname        text := nullif(btrim(p_colname), '');
  coltype        text := nullif(btrim(p_coltype), '');
  default_sql    text := nullif(btrim(p_default_sql), '');
  notnull_sql    text;
  default_clause text;
  v_reserved     text[] := array['id','table_code','rotation_count','account_id','user_id','server_id',
                                 'score','extra_data','score_achieved_at','first_recorded_at',
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
as $$
declare
  colname     text := nullif(btrim(p_colname), '');
  default_sql text := nullif(btrim(p_default_sql), '');
  v_reserved  text[] := array['id','table_code','rotation_count','account_id','user_id','server_id',
                              'score','extra_data','score_achieved_at','first_recorded_at',
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
as $$
declare
  colname    text := nullif(btrim(p_colname), '');
  v_used     text;
  v_reserved text[] := array['id','table_code','rotation_count','account_id','user_id','server_id',
                             'score','extra_data','score_achieved_at','first_recorded_at',
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
revoke all on function public.ts_admin_leaderboard_set_player_data(text,uuid,text,jsonb,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_delete_score(text,uuid,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_add_column(text,text,boolean,text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_update_column(text,boolean,text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_drop_column(text) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_attach_column(text,text,int) from public, anon, authenticated;
revoke all on function public.ts_admin_leaderboard_detach_column(text,text) from public, anon, authenticated;

notify pgrst, 'reload schema';
