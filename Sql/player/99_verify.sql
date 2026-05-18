-- 플레이어 스키마의 설치 상태를 확인합니다.
-- 테이블·함수·인덱스별로 applied / rls_off / missing 상태를 반환합니다.
-- 모든 SQL 파일 실행 후 마지막으로 이 파일을 실행해 결과를 확인하세요.
--
-- =============================================================================
-- 플레이어 스키마 — 반영 확인용 SELECT
-- 선행: 위 파일 전부 (01_servers.sql ~ 09_cron_jobs.sql)
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 실행 결과 (SQL Editor Result 탭에서 반영 여부 확인)
-- status: applied = 생성·RLS 반영됨 | rls_off = 테이블만 있고 RLS 미적용 | missing = 없음
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
      ('purchases'),
      ('withdrawal_delete_queue')
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
      ('ts_delete_read_mails_for_user'),
      ('ts_cleanup_expired_mails'),
      ('ts_mail_inbox_counts'),
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
) r
order by r.category, r.object_name;
