-- =============================================================================
-- 크론 잡 — 만료 정리·탈퇴 배치 (pg_cron)
-- 선행: CREATE EXTENSION IF NOT EXISTS pg_cron; + 12_mails.sql, 09_withdrawal.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- withdrawal_delete_queue — 탈퇴 완료 계정 auth 삭제 대기 목록
-- ---------------------------------------------------------------------------
-- ts_withdrawal_cleanup_batch가 참조하므로 함수보다 먼저 생성합니다.
-- 관리자가 주기적으로 처리. 클라이언트 접근 없음 (RLS 활성화 + policy 없음).
-- ---------------------------------------------------------------------------
create table if not exists public.withdrawal_delete_queue (
  user_id      uuid        primary key references auth.users(id) on delete cascade,
  queued_at    timestamptz not null default now(),
  processed    boolean     not null default false,
  processed_at timestamptz null
);

comment on table public.withdrawal_delete_queue is
  '탈퇴 완료 계정의 auth 삭제 대기 목록. 관리자가 주기적으로 처리.';

alter table public.withdrawal_delete_queue enable row level security;
-- [의도적] 정책 없음 — service_role 전용.

-- ---------------------------------------------------------------------------
-- ts_withdrawal_cleanup_batch — 탈퇴 예약 만료 계정 일괄 처리
-- ---------------------------------------------------------------------------
-- withdrawn_at <= now()인 계정을 account_closures에 기록하고
-- withdrawal_delete_queue에 삭제 대기 등록합니다.
-- auth.admin.deleteUser()는 SQL에서 직접 불가 — 실제 삭제는 별도 워크플로우.
-- ---------------------------------------------------------------------------
create or replace function public.ts_withdrawal_cleanup_batch(p_batch int default 100)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  n   int := 0;
  rec record;
begin
  if p_batch is null or p_batch < 1 then p_batch := 100; end if;
  if p_batch > 500 then p_batch := 500; end if;

  for rec in
    select user_id, account_id
    from public.user_profiles
    where withdrawn_at is not null
      and withdrawn_at <= now()
    limit p_batch
  loop
    -- account_closures 기록: user_id = 영구 플레이어 ID, account_id = auth UUID
    insert into public.account_closures (user_id, account_id, closed_at, note)
    values (rec.user_id, rec.account_id, now(), 'withdrawal_cleanup')
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

revoke all on function public.ts_withdrawal_cleanup_batch(int) from public;
-- pg_cron은 내부 실행이므로 authenticated grant 불필요

-- ---------------------------------------------------------------------------
-- 크론 잡 등록 (멱등: 기존 잡이 있으면 교체)
-- ---------------------------------------------------------------------------

-- 1. 메일 만료 정리 — 매일 새벽 3시 (batch 500)
do $$
begin
  if exists (select 1 from cron.job where jobname = 'cleanup-expired-mails') then
    perform cron.unschedule('cleanup-expired-mails');
  end if;
end $$;
select cron.schedule(
  'cleanup-expired-mails',
  '0 3 * * *',
  'select public.ts_cleanup_expired_mails(500)'
);

-- 2. 탈퇴 계정 정리 — 매일 새벽 2시 (메일 정리 1시간 전, batch 100)
do $$
begin
  if exists (select 1 from cron.job where jobname = 'withdrawal-cleanup') then
    perform cron.unschedule('withdrawal-cleanup');
  end if;
end $$;
select cron.schedule(
  'withdrawal-cleanup',
  '0 2 * * *',
  'select public.ts_withdrawal_cleanup_batch(100)'
);

-- ---------------------------------------------------------------------------
-- 관리용 명령어 (필요 시 SQL Editor에서 직접 실행)
-- ---------------------------------------------------------------------------

-- cron job 목록 확인
-- select * from cron.job;

-- job 실행 로그 확인
-- select * from cron.job_run_details
-- where jobname in ('cleanup-expired-mails', 'withdrawal-cleanup')
-- order by start_time desc limit 20;

-- job 삭제 (필요 시)
-- select cron.unschedule('cleanup-expired-mails');
-- select cron.unschedule('withdrawal-cleanup');
