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
grant select, insert, update         on table public.user_profiles to authenticated;
grant select, insert, update, delete on table public.user_sessions to authenticated;
grant select, insert, update, delete on table public.user_data     to authenticated;

-- 클라이언트 권한을 남기지 않는 테이블(정책 0개 = RLS 가 이미 전면 차단):
--   account_closures, anonymous_recovery_tokens, mail_batches, mail_categories,
--   mail_schedules, user_ban_messages, user_data_logs, withdrawal_delete_queue

-- ---------------------------------------------------------------------------
-- 3. 함수 — 전부 회수 후 SDK 가 실제로 호출하는 RPC 만 되돌려 준다
--    트리거 함수는 트리거가 실행하므로 EXECUTE 권한이 필요 없어 제외 대상이다.
-- ---------------------------------------------------------------------------
do $$
declare r record;
begin
  for r in
    select p.oid::regprocedure as sig
      from pg_proc p join pg_namespace n on n.oid = p.pronamespace
     where n.nspname = 'public' and p.prorettype <> 'trigger'::regtype
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
grant execute on function public.ts_anon_recovery_get_refresh_token(text, text)              to anon, authenticated;
grant execute on function public.ts_anon_recovery_delete_by_fingerprint(text, text)          to anon, authenticated;
grant execute on function public.ts_anon_recovery_upsert_refresh_token(text, text, uuid, text) to anon, authenticated;

-- 로그인 사용자 전용 RPC
grant execute on function public.ts_ensure_my_profile(text, text)          to authenticated;
grant execute on function public.ts_ensure_my_row(text, text)              to authenticated;
grant execute on function public.ts_my_server_id()                         to authenticated;
grant execute on function public.ts_my_withdrawal_status()                 to authenticated;
grant execute on function public.ts_request_withdrawal(integer)            to authenticated;
grant execute on function public.ts_transfer_my_server(text, text)         to authenticated;
grant execute on function public.ts_delete_my_anon_recovery_tokens()       to authenticated;
grant execute on function public.ts_view_mail_for_user(uuid)               to authenticated;
grant execute on function public.ts_claim_mail_items(uuid)                 to authenticated;
grant execute on function public.ts_claim_all_mail_items(text)             to authenticated;
grant execute on function public.ts_delete_mail_for_user(uuid)             to authenticated;
grant execute on function public.ts_delete_claimed_mails_for_user(text)    to authenticated;
grant execute on function public.ts_mail_inbox_counts()                    to authenticated;

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

-- 클라이언트에 열지 않는 함수(운영·cron·트리거 전용). postgres·service_role 로만 호출합니다:
--   admin_add_user_data_column / admin_drop_user_data_column / admin_update_user_data_column
--   ts_protect_field / ts_unprotect_field  ← SECURITY DEFINER 로 CHECK 제약을 조작한다.
--                                            열려 있으면 플레이어가 재화 최소값 보호를 해제할 수 있다.
--   ts_run_due_mail_schedules / ts_withdrawal_cleanup_batch / ts_cleanup_expired_mails
--   ts_admin_* (우편 발송·카탈로그·리더보드), rls_auto_enable, ts_mail_schedule_next_run
--   ts_leaderboard_rotate_due / ts_leaderboard_next_rotation_at / ts_leaderboard_columns_of
--   ts_admin_leaderboard_*  ← 리더보드 정의·점수 정정·컬럼 DDL. 열리면 클라이언트가
--                             리더보드를 지우거나 점수를 조작할 수 있다.

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
--  where n.nspname = 'public' and p.prorettype <> 'trigger'::regtype
--    and (has_function_privilege('anon', p.oid, 'EXECUTE')
--         or has_function_privilege('authenticated', p.oid, 'EXECUTE'))
--  order by anon desc, p.proname;
--
-- 3) 기본 권한에서 anon·authenticated 가 빠졌는지 (postgres 행에 둘이 없어야 함)
-- select defaclrole::regrole::text as grantor, defaclobjtype, defaclacl::text
--   from pg_default_acl where defaclnamespace::regnamespace::text = 'public';
