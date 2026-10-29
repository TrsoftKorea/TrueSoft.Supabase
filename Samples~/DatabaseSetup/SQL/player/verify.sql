-- 플레이어 스키마의 설치 상태를 확인합니다.
-- 테이블·함수·인덱스 생성 여부와 클라이언트 권한(anon·authenticated) 상태를 함께 반환합니다.
-- 모든 SQL 파일 실행 후 마지막으로 이 파일을 실행해 결과를 확인하세요.
--
-- =============================================================================
-- 플레이어 스키마 — 반영 확인용 SELECT
-- 선행: install.sql
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 실행 결과 (SQL Editor Result 탭에서 반영 여부 확인)
--
-- 생성 확인
--   applied         = 생성·RLS 반영됨
--   rls_off         = 테이블만 있고 RLS 미적용
--   missing         = 없음
--
-- 권한 확인 (install.sql 마지막 절 반영 여부)
--   applied         = 기대한 권한과 정확히 일치
--   mismatch        = 권한이 기대와 다름 (detail 의 expected/actual 비교)
--   missing_grant   = 있어야 할 권한이 없음 → 클라이언트 기능이 깨진다
--   over_privileged = 없어야 할 권한이 남아 있음 → 15번 파일을 실행할 것
--
-- over_privileged 가 하나라도 보이면 install.sql 을 다시 실행하세요.
-- -----------------------------------------------------------------------------
select
  r.category,
  r.object_name,
  r.status,
  r.detail
from (
  select
    'table+rls'::text as category,
    exp.name as object_name,
    case
      when pub.oid is null then 'missing'
      when not pub.relrowsecurity then 'rls_off'
      else 'applied'
    end as status,
    null::text as detail
  -- install.sql 이 만드는 표 전부, 만드는 순서대로. 표를 새로 만들면 여기도 넣는다(빠지면 SDK 검사가 잡는다).
  from (
    values
      ('game_servers'),
      ('game_settings'),
      ('user_profiles'),
      ('display_names'),
      ('banned_words'),
      ('user_sessions'),
      ('user_daily_activity'),
      ('anonymous_recovery_tokens'),
      ('user_data'),
      ('ts_protected_fields'),
      ('account_closures'),
      ('apple_auth_tokens'),
      ('mails'),
      ('mail_settings'),
      ('purchases'),
      ('fx_rates'),
      ('purchase_refund_sync'),
      ('purchase_refund_events'),
      ('server_config_services'),
      ('remote_config'),
      ('withdrawal_sweep_state'),
      ('ccu_snapshots'),
      ('ccu_daily'),
      ('user_ban_messages'),
      ('request_limit_settings'),
      ('request_limit_counters'),
      ('request_limit_blocks'),
      ('user_data_logs'),
      ('game_items'),
      ('mail_batches'),
      ('mail_schedules'),
      ('mail_categories'),
      ('leaderboard_tables'),
      ('leaderboard_scores'),
      ('leaderboard_table_columns'),
      ('ts_schema_draft'),
      ('ts_schema_version'),
      ('ts_admin_operators'),
      ('ts_admin_passwords'),
      ('ts_admin_audit_logs'),
      ('ts_admin_console_settings'),
      ('coupons'),
      ('coupon_codes'),
      ('coupon_redemptions'),
      ('chat_channels'),
      ('chat_messages'),
      ('chat_daily_stats'),
      ('chat_mutes'),
      ('match_reward_configs'),
      ('match_results'),
      ('friend_requests'),
      ('friend_settings'),
      ('match_lobbies'),
      ('match_lobby_members'),
      ('match_lobby_settings')
  ) as exp(name)
  left join lateral (
    select c.oid, c.relrowsecurity
    from pg_class c
    join pg_namespace n on n.oid = c.relnamespace and n.nspname = 'public'
    where c.relkind = 'r'
      and c.relname = exp.name
    limit 1
  ) pub on true

  union all

  select
    'function'::text,
    exp.name,
    case when pub.oid is not null then 'applied' else 'missing' end,
    case
      when pub.oid is not null then pg_get_function_identity_arguments(pub.oid)
      else null
    end
  from (
    values
      ('set_updated_at'),
      ('ts_update_last_activity_at'),
      ('admin_add_user_data_column'),
      ('admin_update_user_data_column'),
      ('admin_drop_user_data_column'),
      ('ts_anon_recovery_get_refresh_token'),
      ('ts_anon_recovery_upsert_refresh_token'),
      ('ts_anon_recovery_delete_by_fingerprint'),
      ('ts_delete_my_anon_recovery_tokens'),
      ('auth_user_server_id'),
      ('ts_default_server_id'),
      ('ts_profiles_coalesce_server_id'),
      ('ts_ensure_my_profile'),
      ('ts_ensure_my_row'),
      ('ts_my_server_id'),
      ('ts_sync_server_id_by_account'),
      ('ts_transfer_my_server'),
      ('ts_admin_transfer_user_server'),
      ('_ts_transfer_user_server_core'),
      ('ts_view_mail_for_user'),
      ('ts_claim_mail_items'),
      ('ts_claim_all_mail_items'),
      ('ts_delete_mail_for_user'),
      ('ts_delete_claimed_mails_for_user'),
      ('ts_cleanup_expired_mails'),
      ('ts_mail_inbox_counts'),
      ('ts_mail_client_settings'),
      ('ts_admin_send_mail'),
      ('ts_admin_upsert_game_item'),
      ('ts_admin_delete_game_item'),
      ('ts_admin_count_recipients'),
      ('ts_mail_schedule_next_run'),
      ('ts_run_due_mail_schedules'),
      ('ts_server_now'),
      ('ts_request_withdrawal'),
      ('ts_my_withdrawal_status'),
      ('ts_after_sign_in'),
      ('ts_withdrawal_cancel_redeem'),
      ('ts_withdrawal_cancel_secret'),
      ('ts_withdrawal_due_accounts'),
      ('ts_withdrawal_sweep_try_start'),
      ('ts_withdrawal_deadline'),
      ('ts_game_time_zone'),
      ('ts_game_today'),
      ('ts_withdrawal_sweep_kick'),
      ('ts_withdrawal_overdue_check'),
      ('ts_session_is_online'),
      ('ts_session_heartbeat'),
      ('ts_record_daily_activity'),
      ('ts_ccu_now'),
      ('ts_ccu_daily_refresh'),
      ('ts_ccu_snapshot'),
      ('ts_leaderboard_next_rotation_at'),
      ('ts_leaderboard_columns_of'),
      ('ts_leaderboard_columns_meta'),
      ('ts_leaderboard_list_meta'),
      ('ts_leaderboard_tables'),
      ('ts_leaderboard_table'),
      ('ts_leaderboard_submit_score'),
      ('ts_leaderboard_range'),
      ('ts_leaderboard_player'),
      ('ts_leaderboard_set_player_data'),
      ('ts_leaderboard_delete_my_score'),
      ('ts_leaderboard_rotate_due'),
      ('ts_admin_leaderboard_upsert_table'),
      ('ts_admin_leaderboard_delete_table'),
      ('ts_admin_leaderboard_rotate'),
      ('ts_admin_leaderboard_set_score'),
      ('ts_admin_leaderboard_set_player_data'),
      ('ts_admin_leaderboard_delete_score'),
      ('ts_admin_leaderboard_add_column'),
      ('ts_admin_leaderboard_update_column'),
      ('ts_admin_leaderboard_drop_column'),
      ('ts_admin_leaderboard_attach_column'),
      ('ts_admin_leaderboard_detach_column')
  ) as exp(name)
  left join lateral (
    select p.oid
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace and n.nspname = 'public'
    where p.proname = exp.name
    order by p.oid
    limit 1
  ) pub on true

  union all

  select
    'index'::text,
    'profiles_withdrawn_at_idx'::text,
    case
      when exists (
        select 1
        from pg_indexes i
        where i.schemaname = 'public'
          and i.indexname = 'profiles_withdrawn_at_idx'
      ) then 'applied'
      else 'missing'
    end,
    'on profiles'::text

  union all

  -- ---------------------------------------------------------------------------
  -- 테이블 권한 — 있어야 할 권한이 정확히 있는지
  -- ---------------------------------------------------------------------------
  select
    'grant:table'::text,
    exp.tbl || ' → ' || exp.grantee,
    case
      when coalesce(act.privs, '') = exp.privs then 'applied'
      when coalesce(act.privs, '') = ''        then 'missing_grant'
      else 'mismatch'
    end,
    'expected: ' || exp.privs || ' / actual: ' || coalesce(act.privs, '(none)')
  from (
    values
      ('game_servers',  'anon',          'SELECT'),
      ('game_servers',  'authenticated', 'SELECT'),
      ('remote_config', 'anon',          'SELECT'),
      ('remote_config', 'authenticated', 'SELECT'),
      ('game_items',          'authenticated', 'SELECT'),
      ('mails',               'authenticated', 'SELECT'),
      ('purchases',           'authenticated', 'SELECT'),
      ('ts_protected_fields', 'authenticated', 'SELECT'),
      -- 스키마 노출 전용(RLS 정책 0개라 행 반환 없음). 클래스 생성기가 컬럼 타입을 읽는 데 필요
      ('leaderboard_scores',  'authenticated', 'SELECT'),
      ('display_names', 'authenticated', 'INSERT,SELECT,UPDATE'),
      ('user_profiles', 'authenticated', 'INSERT,SELECT,UPDATE'),
      ('user_sessions', 'authenticated', 'DELETE,INSERT,SELECT,UPDATE'),
      ('user_data',     'authenticated', 'DELETE,INSERT,SELECT,UPDATE')
  ) as exp(tbl, grantee, privs)
  left join lateral (
    select string_agg(g.privilege_type, ',' order by g.privilege_type) as privs
    from information_schema.role_table_grants g
    where g.table_schema = 'public'
      and g.table_name = exp.tbl
      and g.grantee = exp.grantee
  ) act on true

  union all

  -- ---------------------------------------------------------------------------
  -- 테이블 권한 — 없어야 할 권한이 남아 있는지 (TRUNCATE 는 RLS 를 우회한다)
  -- ---------------------------------------------------------------------------
  select
    'grant:table'::text,
    g.table_name || ' → ' || g.grantee,
    'over_privileged'::text,
    'unexpected: ' || string_agg(g.privilege_type, ',' order by g.privilege_type)
  from information_schema.role_table_grants g
  where g.table_schema = 'public'
    and g.grantee in ('anon', 'authenticated')
    and (g.table_name, g.grantee) not in (
      select v.tbl, v.grantee
      from (
        values
          ('game_servers','anon'), ('game_servers','authenticated'),
          ('remote_config','anon'), ('remote_config','authenticated'),
          ('game_items','authenticated'), ('mails','authenticated'),
          ('purchases','authenticated'), ('ts_protected_fields','authenticated'),
          ('display_names','authenticated'), ('user_profiles','authenticated'),
          ('user_sessions','authenticated'), ('user_data','authenticated'),
          ('leaderboard_scores','authenticated')
      ) as v(tbl, grantee)
    )
  group by g.table_name, g.grantee

  union all

  -- ---------------------------------------------------------------------------
  -- 함수 권한 — SDK 가 호출하는 RPC 가 기대한 롤에만 열려 있는지
  -- ---------------------------------------------------------------------------
  select
    'grant:function'::text,
    exp.fn || ' → ' || exp.roles,
    case
      when pub.oid is null then 'missing'
      when act.roles = exp.roles then 'applied'
      when act.roles = 'none' then 'missing_grant'
      else 'mismatch'
    end,
    'expected: ' || exp.roles || ' / actual: ' || coalesce(act.roles, '(none)')
  from (
    values
      -- 로그인 전에도 호출해야 하는 함수
      ('auth_user_server_id',                    'anon+auth'),
      ('ts_default_server_id',                   'anon+auth'),
      ('ts_server_now',                          'anon+auth'),
      ('ts_is_display_name_available',           'anon+auth'),
      ('ts_anon_recovery_get_refresh_token',     'anon+auth'),
      ('ts_anon_recovery_upsert_refresh_token',  'anon+auth'),
      ('ts_anon_recovery_delete_by_fingerprint', 'anon+auth'),
      -- 요청 제한 사전 검사 — 빠지면 그 역할의 요청이 전부 실패한다
      ('ts_request_limit_check',                 'anon+auth'),
      ('ts_request_limit_ping',                  'auth'),
      -- 로그인 사용자 전용 RPC
      ('ts_ensure_my_profile',            'auth'),
      ('ts_ensure_my_row',                'auth'),
      ('ts_my_server_id',                 'auth'),
      ('ts_my_withdrawal_status',         'auth'),
      ('ts_after_sign_in',                'auth'),
      ('ts_session_heartbeat',            'auth'),
      ('ts_request_withdrawal',           'auth'),
      ('ts_transfer_my_server',           'auth'),
      ('ts_delete_my_anon_recovery_tokens', 'auth'),
      ('ts_view_mail_for_user',           'auth'),
      ('ts_claim_mail_items',             'auth'),
      ('ts_claim_all_mail_items',         'auth'),
      ('ts_delete_mail_for_user',         'auth'),
      ('ts_delete_claimed_mails_for_user','auth'),
      ('ts_mail_inbox_counts',            'auth'),
      ('ts_mail_client_settings',         'auth'),
      -- 리더보드
      ('ts_leaderboard_tables',           'auth'),
      ('ts_leaderboard_table',            'auth'),
      ('ts_leaderboard_submit_score',     'auth'),
      ('ts_leaderboard_range',            'auth'),
      ('ts_leaderboard_player',           'auth'),
      ('ts_leaderboard_set_player_data',  'auth'),
      ('ts_leaderboard_delete_my_score',  'auth'),
      ('ts_leaderboard_columns_meta',      'anon+auth'),
      ('ts_leaderboard_list_meta',         'anon+auth')
  ) as exp(fn, roles)
  left join lateral (
    select p.oid
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace and n.nspname = 'public'
    where p.proname = exp.fn
    order by p.oid
    limit 1
  ) pub on true
  left join lateral (
    select case
             when pub.oid is null then null
             when has_function_privilege('anon', pub.oid, 'EXECUTE')
               and has_function_privilege('authenticated', pub.oid, 'EXECUTE') then 'anon+auth'
             when has_function_privilege('authenticated', pub.oid, 'EXECUTE') then 'auth'
             when has_function_privilege('anon', pub.oid, 'EXECUTE') then 'anon-only'
             else 'none'
           end as roles
  ) act on true

  union all

  -- ---------------------------------------------------------------------------
  -- 함수 권한 — 클라이언트에 열리면 안 되는 함수가 열려 있는지
  --   ts_protect_field·ts_unprotect_field·admin_* 은 SECURITY DEFINER 로 DDL 을 실행한다.
  -- ---------------------------------------------------------------------------
  select
    'grant:function'::text,
    p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')',
    'over_privileged'::text,
    'client-callable: ' ||
      case when has_function_privilege('anon', p.oid, 'EXECUTE') then 'anon+auth' else 'auth' end
  from pg_proc p
  join pg_namespace n on n.oid = p.pronamespace and n.nspname = 'public'
  -- 트리거 함수도 본다 — install.sql 22절이 트리거 함수 권한까지 회수한다(트리거 실행은 EXECUTE 를 보지 않는다).
  where (has_function_privilege('anon', p.oid, 'EXECUTE')
      or has_function_privilege('authenticated', p.oid, 'EXECUTE'))
    -- install.sql 이 authenticated·anon 에 grant 하는 함수 전부. 둘의 일치는 SdkAudit R17 이 본다.
    and p.proname not in (
      'auth_user_server_id', 'ts_after_sign_in', 'ts_anon_recovery_delete_by_fingerprint',
      'ts_anon_recovery_get_refresh_token', 'ts_anon_recovery_upsert_refresh_token',
      'ts_chat_channels', 'ts_chat_fetch_direct', 'ts_chat_fetch_lobby', 'ts_chat_fetch_many',
      'ts_chat_send', 'ts_chat_send_direct', 'ts_chat_send_lobby',
      'ts_claim_all_mail_items', 'ts_claim_mail_items', 'ts_coupon_redeem', 'ts_default_server_id',
      'ts_delete_claimed_mails_for_user', 'ts_delete_mail_for_user', 'ts_delete_my_anon_recovery_tokens',
      'ts_display_name_check', 'ts_ensure_my_profile', 'ts_ensure_my_row',
      'ts_friend_limits', 'ts_friend_remove', 'ts_friend_request_cancel', 'ts_friend_request_respond',
      'ts_friend_request_send', 'ts_friend_requests_list', 'ts_friend_search', 'ts_friends_list',
      'ts_is_display_name_available', 'ts_leaderboard_columns_meta', 'ts_leaderboard_delete_my_score',
      'ts_leaderboard_list_meta', 'ts_leaderboard_player', 'ts_leaderboard_range',
      'ts_leaderboard_set_player_data', 'ts_leaderboard_submit_score', 'ts_leaderboard_table',
      'ts_leaderboard_tables', 'ts_mail_client_settings', 'ts_mail_inbox_counts', 'ts_mark_purchase_granted',
      'ts_match_history', 'ts_match_lobby_cancel', 'ts_match_lobby_create', 'ts_match_lobby_invite',
      'ts_match_lobby_leave', 'ts_match_lobby_list_my', 'ts_match_lobby_respond',
      'ts_match_lobby_set_member_meta', 'ts_match_lobby_start', 'ts_match_report_result',
      'ts_match_report_solo_result', 'ts_match_stats', 'ts_my_server_id', 'ts_my_withdrawal_status',
      'ts_request_limit_check', 'ts_request_limit_ping',
      'ts_request_withdrawal', 'ts_server_now', 'ts_session_heartbeat', 'ts_transfer_my_server', 'ts_view_mail_for_user'
    )

  union all

  -- ---------------------------------------------------------------------------
  -- 요청 제한 사전 검사 — 모든 요청 앞에 끼므로 서버 키(service_role) 실행 권한이 빠지면 콘솔·엣지 함수의
  -- DB 요청이 전부 실패한다. 위 대조는 anon·authenticated 만 보므로 따로 본다. 연결 여부도 함께.
  -- ---------------------------------------------------------------------------
  select
    'pre_request'::text,
    'ts_request_limit_check → service_role',
    case
      when to_regprocedure('public.ts_request_limit_check()') is null then 'missing'
      when has_function_privilege('service_role', 'public.ts_request_limit_check()', 'EXECUTE') then 'applied'
      else 'missing_grant'
    end,
    'service_role 이 실행하지 못하면 콘솔·엣지 함수 요청이 전부 실패한다'
  union all
  select
    'pre_request'::text,
    'authenticator pgrst.db_pre_request',
    case when exists (
      select 1 from pg_db_role_setting s
      where s.setrole = 'authenticator'::regrole and s.setdatabase = 0
        and 'pgrst.db_pre_request=public.ts_request_limit_check' = any (s.setconfig)
    ) then 'applied' else 'missing' end,
    '비상시 끄기: alter role authenticator reset pgrst.db_pre_request; notify pgrst, ''reload config'';'

  union all

  -- ---------------------------------------------------------------------------
  -- 기본 권한 — 앞으로 만들 테이블·함수에 클라이언트 권한이 자동 부여되는지
  --   supabase_admin 소유 항목은 postgres 권한으로 수정할 수 없어 검사 대상에서 제외한다.
  -- ---------------------------------------------------------------------------
  select
    'grant:default'::text,
    'new ' || case d.defaclobjtype when 'r' then 'tables' else 'functions' end,
    case
      when d.defaclacl::text like '%anon=%'
        or d.defaclacl::text like '%authenticated=%' then 'over_privileged'
      else 'applied'
    end,
    d.defaclacl::text
  from pg_default_acl d
  where d.defaclnamespace::regnamespace::text = 'public'
    and d.defaclobjtype in ('r', 'f')
    and d.defaclrole::regrole::text = 'postgres'
) r
order by r.category, r.object_name;
