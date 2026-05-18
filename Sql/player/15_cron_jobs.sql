-- =============================================================================
-- 크론 잡 — 만료 정리·탈퇴 배치 (pg_cron)
-- 선행: CREATE EXTENSION IF NOT EXISTS pg_cron; + 12_mails.sql, 09_withdrawal.sql
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. 메일 만료 정리 (cleanup-expired-mails Edge Function 대체)
-- -----------------------------------------------------------------------------
-- 이미 ts_cleanup_expired_mails RPC가 있음 (11_mails.sql)
-- pg_cron에서 직접 호출 (HTTP/Edge Function 불필요)

-- 매일 새벽 3시 실행 (기본 batch 500)
select cron.schedule(
    'cleanup-expired-mails',
    '0 3 * * *',
    'select ts_cleanup_expired_mails(500)'
);

-- -----------------------------------------------------------------------------
-- 2. 탈퇴 계정 정리 (withdrawal-cleanup Edge Function 대체)
-- -----------------------------------------------------------------------------
-- 주의: auth.admin.deleteUser()는 SQL에서 직접 불가
-- account_closures 기록 + 마킹만 SQL에서 처리, 실제 삭제는 별도 워크플로우

create or replace function public.ts_withdrawal_cleanup_batch(p_batch int default 100)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  n int := 0;
  rec record;
begin
  if p_batch is null or p_batch < 1 then p_batch := 100; end if;
  if p_batch > 500 then p_batch := 500; end if;

  for rec in
    select account_id
    from public.user_profiles
    where withdrawn_at is not null
      and withdrawn_at <= now()
    limit p_batch
  loop
    -- account_closures 기록
    insert into public.account_closures (user_id, account_id, closed_at, note)
    values (rec.account_id, rec.account_id, now(), 'withdrawal_cleanup')
    on conflict (user_id) do update
    set closed_at = now(), note = 'withdrawal_cleanup';

    -- 삭제 대기 큐에 추가 (별도 관리자 처리용)
    insert into public.withdrawal_delete_queue (user_id, queued_at, processed)
    values (rec.account_id, now(), false)
    on conflict (user_id) do nothing;

    n := n + 1;
  end loop;

  return jsonb_build_object('processed', n, 'queued_for_deletion', n);
end;
$$;

-- 삭제 대기 큐 테이블 (없으면 생성)
create table if not exists public.withdrawal_delete_queue (
    user_id uuid primary key references auth.users(id) on delete cascade,
    queued_at timestamptz not null default now(),
    processed boolean not null default false,
    processed_at timestamptz null
);

comment on table public.withdrawal_delete_queue is 
    '탈퇴 완료 계정의 auth 삭제 대기 목록. 관리자가 주기적으로 처리.';

revoke all on function public.ts_withdrawal_cleanup_batch(int) from public;
-- pg_cron은 내부 실행이므로 grant 불필요

-- 매일 새벽 2시 실행 (메일 삭제 1시간 전)
select cron.schedule(
    'withdrawal-cleanup',
    '0 2 * * *',
    'select ts_withdrawal_cleanup_batch(100)'
);

-- -----------------------------------------------------------------------------
-- 관리용 명령어
-- -----------------------------------------------------------------------------

-- cron job 목록 확인
-- select * from cron.job;

-- job 실행 로그 확인
-- select * from cron.job_run_details 
-- where jobname in ('cleanup-expired-mails', 'withdrawal-cleanup')
-- order by start_time desc
-- limit 20;

-- job 삭제 (필요시)
-- select cron.unschedule('cleanup-expired-mails');
-- select cron.unschedule('withdrawal-cleanup');
