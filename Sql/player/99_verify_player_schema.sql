-- =============================================================================
-- 플레이어 스키마 — 반영 확인용 SELECT
-- 선행: 위 파일 전부
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
      ('game_servers'),
      -- ('user_saves') -- 제거됨. 커스텀 세이브 테이블은 프로젝트별로 직접 추가.
      ('display_names'),
      ('user_sessions'),
      ('anonymous_recovery_tokens'),
      ('account_closures'),
      ('remote_config'),
      ('mails')
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