-- =============================================================================
-- 범용 필드 보호 인프라
-- 선행: 04_user_saves.sql (admin_create_user_table, RLS 정책 패턴)
--
-- SDK 사용자가 원하는 테이블/컬럼에 클라이언트 증가 차단 및 최솟값 제약을
-- 한 번의 함수 호출로 적용할 수 있는 범용 헬퍼를 제공합니다.
--
-- 사용 예:
--   SELECT ts_protect_field('data_basic', 'coins');    -- 0 이상, 클라이언트 증가 불가
--   SELECT ts_protect_field('data_basic', 'gems');     -- 동일
--   SELECT ts_unprotect_field('data_basic', 'coins');  -- 해제
-- =============================================================================

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

create policy ts_protected_fields_select on public.ts_protected_fields
    for select to authenticated using (true);

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
