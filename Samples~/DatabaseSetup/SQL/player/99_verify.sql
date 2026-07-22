-- 플레이어 스키마의 설치 상태를 확인합니다.
-- 테이블·함수·인덱스 생성 여부와 클라이언트 권한(anon·authenticated) 상태를 함께 반환합니다.
-- 모든 SQL 파일 실행 후 마지막으로 이 파일을 실행해 결과를 확인하세요.
--
-- =============================================================================
-- 플레이어 스키마 — 반영 확인용 SELECT
-- 선행: 위 파일 전부 (01_servers.sql ~ 15_grants_hardening.sql)
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 실행 결과 (SQL Editor Result 탭에서 반영 여부 확인)
--
-- 생성 확인
--   applied         = 생성·RLS 반영됨
--   rls_off         = 테이블만 있고 RLS 미적용
--   missing         = 없음
--
-- 권한 확인 (15_grants_hardening.sql 반영 여부)
--   applied         = 기대한 권한과 정확히 일치
--   mismatch        = 권한이 기대와 다름 (detail 의 expected/actual 비교)
--   missing_grant   = 있어야 할 권한이 없음 → 클라이언트 기능이 깨진다
--   over_privileged = 없어야 할 권한이 남아 있음 → 15번 파일을 실행할 것
--
-- over_privileged 가 하나라도 보이면 15_grants_hardening.sql 을 실행하세요.
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
  from (
    values
      ('user_profiles'),
      ('user_data'),
      ('game_servers'),
      ('display_names'),
      ('user_sessions'),
      ('anonymous_recovery_tokens'),
      ('account_closures'),
      ('remote_config'),
      ('mails'),
      ('mail_batches'),
      ('mail_schedules'),
      ('mail_categories'),
      ('game_items'),
      ('purchases'),
      ('withdrawal_delete_queue'),
      ('user_ban_messages')
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
      ('ts_admin_send_mail'),
      ('ts_admin_upsert_game_item'),
      ('ts_admin_delete_game_item'),
      ('ts_admin_count_recipients'),
      ('ts_mail_schedule_next_run'),
      ('ts_run_due_mail_schedules'),
      ('ts_server_now'),
      ('ts_request_withdrawal'),
      ('ts_my_withdrawal_status'),
      ('ts_withdrawal_cancel_redeem'),
      ('ts_withdrawal_cleanup_batch')
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
          ('user_sessions','authenticated'), ('user_data','authenticated')
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
      -- 로그인 사용자 전용 RPC
      ('ts_ensure_my_profile',            'auth'),
      ('ts_ensure_my_row',                'auth'),
      ('ts_my_server_id',                 'auth'),
      ('ts_my_withdrawal_status',         'auth'),
      ('ts_request_withdrawal',           'auth'),
      ('ts_transfer_my_server',           'auth'),
      ('ts_delete_my_anon_recovery_tokens', 'auth'),
      ('ts_view_mail_for_user',           'auth'),
      ('ts_claim_mail_items',             'auth'),
      ('ts_claim_all_mail_items',         'auth'),
      ('ts_delete_mail_for_user',         'auth'),
      ('ts_delete_claimed_mails_for_user','auth'),
      ('ts_mail_inbox_counts',            'auth')
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
  where p.prorettype <> 'trigger'::regtype
    and (has_function_privilege('anon', p.oid, 'EXECUTE')
      or has_function_privilege('authenticated', p.oid, 'EXECUTE'))
    and p.proname not in (
      'auth_user_server_id', 'ts_default_server_id', 'ts_server_now',
      'ts_is_display_name_available', 'ts_anon_recovery_get_refresh_token',
      'ts_anon_recovery_upsert_refresh_token', 'ts_anon_recovery_delete_by_fingerprint',
      'ts_ensure_my_profile', 'ts_ensure_my_row', 'ts_my_server_id',
      'ts_my_withdrawal_status', 'ts_request_withdrawal', 'ts_transfer_my_server',
      'ts_delete_my_anon_recovery_tokens', 'ts_view_mail_for_user', 'ts_claim_mail_items',
      'ts_claim_all_mail_items', 'ts_delete_mail_for_user',
      'ts_delete_claimed_mails_for_user', 'ts_mail_inbox_counts'
    )

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
