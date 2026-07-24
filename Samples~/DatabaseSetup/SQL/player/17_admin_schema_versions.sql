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
      perform public.ts_admin_leaderboard_upsert_table(
        d.params->>'code', d.params->>'display_name',
        coalesce(d.params->>'scope','global'), coalesce(d.params->>'record_type','highest'),
        coalesce(d.params->>'sort_type','desc'), coalesce(d.params->>'rotation','none'),
        (d.params->>'period_seconds')::int, (d.params->>'anchor_at')::timestamptz,
        coalesce(d.params->>'tz','Asia/Seoul'), (d.params->>'ends_at')::timestamptz,
        coalesce((d.params->>'is_active')::boolean, true));

    elsif v_key = 'leaderboard_table.update' then
      select to_jsonb(t) into v_before from public.leaderboard_tables t where code = d.params->>'code';
      perform public.ts_admin_leaderboard_upsert_table(
        d.params->>'code', d.params->>'display_name',
        coalesce(d.params->>'scope','global'), coalesce(d.params->>'record_type','highest'),
        coalesce(d.params->>'sort_type','desc'), coalesce(d.params->>'rotation','none'),
        (d.params->>'period_seconds')::int, (d.params->>'anchor_at')::timestamptz,
        coalesce(d.params->>'tz','Asia/Seoul'), (d.params->>'ends_at')::timestamptz,
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
begin
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
    meta := coalesce(vj->'__meta','{}'::jsonb) - p_item_key;
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

notify pgrst, 'reload schema';
