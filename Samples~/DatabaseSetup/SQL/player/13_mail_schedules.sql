-- 우편 예약·반복 발송 인프라를 제공합니다.
-- 대기 발송 테이블(mail_schedules) + 러너(ts_run_due_mail_schedules) + pg_cron(매분).
-- 러너는 만기 스케줄마다 ts_admin_send_mail(12_admin_mail)을 호출합니다.
-- 즉시 발송은 스케줄 없이 ts_admin_send_mail을 바로 쓰므로 여기 대상이 아닙니다.
--
-- =============================================================================
-- 우편 예약 발송 — mail_schedules + 러너 + cron
-- 선행: 09_cron_jobs.sql(pg_cron), 12_admin_mail.sql(ts_admin_send_mail)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- mail_schedules — 예약(1회)·반복(매일 시각) 발송 대기 목록
-- ---------------------------------------------------------------------------
create table if not exists public.mail_schedules (
  id            uuid primary key default gen_random_uuid(),
  schedule_type text not null check (schedule_type in ('scheduled','repeat')),
  target_mode   text not null check (target_mode in ('all','server','players')),
  server_id     uuid null references public.game_servers (id) on delete set null,
  account_ids   jsonb null,
  title         text not null default '',
  content       text not null default '',
  sender_name   text not null default '',
  items         jsonb null,
  localized     jsonb null,
  category      text not null default 'default',
  expires_days  int  not null default 7 check (expires_days >= 1),
  scheduled_at  timestamptz null,                       -- scheduled(1회) 대상 시각
  repeat_time   time null,                              -- repeat 대상 시각
  repeat_tz     text not null default 'Asia/Seoul',
  repeat_unit   text not null default 'day' check (repeat_unit in ('day','week','month')),
  repeat_dow    int null,                               -- week: 0=일 ~ 6=토
  repeat_dom    int null,                               -- month: 1~31 (말일 초과 시 말일)
  next_run_at   timestamptz not null,                   -- 러너 판단 기준(두 타입 통일)
  is_active     boolean not null default true,
  last_run_at   timestamptz null,
  run_count     int not null default 0,
  created_by    text null,
  created_at    timestamptz not null default now()
);
alter table public.mail_schedules add column if not exists account_ids  jsonb;
alter table public.mail_schedules add column if not exists items        jsonb;
alter table public.mail_schedules add column if not exists localized    jsonb;
alter table public.mail_schedules add column if not exists category     text not null default 'default';
alter table public.mail_schedules add column if not exists expires_days int not null default 7;
alter table public.mail_schedules add column if not exists scheduled_at timestamptz;
alter table public.mail_schedules add column if not exists repeat_time  time;
alter table public.mail_schedules add column if not exists repeat_tz    text not null default 'Asia/Seoul';
alter table public.mail_schedules add column if not exists repeat_unit  text not null default 'day';
alter table public.mail_schedules add column if not exists repeat_dow   int;
alter table public.mail_schedules add column if not exists repeat_dom   int;
alter table public.mail_schedules add column if not exists last_run_at  timestamptz;
alter table public.mail_schedules add column if not exists run_count    int not null default 0;
alter table public.mail_schedules add column if not exists created_by   text;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'mail_schedules_repeat_unit_chk') then
    alter table public.mail_schedules add constraint mail_schedules_repeat_unit_chk check (repeat_unit in ('day','week','month'));
  end if;
end $$;

create index if not exists mail_schedules_due_idx on public.mail_schedules (next_run_at) where is_active;
create index if not exists mail_schedules_created_idx on public.mail_schedules (created_at desc);

comment on table public.mail_schedules is
  '우편 예약(scheduled 1회)·반복(repeat day/week/month) 발송 대기 목록. 러너가 next_run_at 만기 시 ts_admin_send_mail 호출.';

alter table public.mail_schedules enable row level security;
revoke all on table public.mail_schedules from anon, authenticated;
revoke select, insert, update, delete on table public.mail_schedules from service_role;

-- ---------------------------------------------------------------------------
-- ts_mail_schedule_next_run — 다음 실행 시각 계산(러너·생성 경로 공유)
--   scheduled : scheduled_at 그대로
--   repeat    : repeat_tz 기준 다음 발생
--               day   = 매일 repeat_time
--               week  = 매주 repeat_dow(0=일~6=토) repeat_time
--               month = 매월 repeat_dom(1~31, 말일 초과 시 말일) repeat_time
-- ---------------------------------------------------------------------------
drop function if exists public.ts_mail_schedule_next_run(text,timestamptz,time,text);

create or replace function public.ts_mail_schedule_next_run(
  p_type         text,
  p_scheduled_at timestamptz,
  p_repeat_time  time,
  p_repeat_tz    text default 'Asia/Seoul',
  p_repeat_unit  text default 'day',
  p_repeat_dow   int  default null,
  p_repeat_dom   int  default null
)
returns timestamptz
language plpgsql
stable
as $$
declare
  v_tz        text := coalesce(p_repeat_tz, 'Asia/Seoul');
  v_unit      text := coalesce(p_repeat_unit, 'day');
  v_now       timestamp;
  v_today     date;
  v_date      date;
  v_next      timestamp;
  v_cur_dow   int;
  v_delta     int;
  v_dim       int;
  v_firstnext date;
begin
  if p_type = 'scheduled' then
    return p_scheduled_at;
  elsif p_type <> 'repeat' then
    raise exception 'invalid_schedule_type: %', p_type;
  end if;

  if p_repeat_time is null then
    raise exception 'repeat_time_required';
  end if;

  v_now   := now() at time zone v_tz;
  v_today := v_now::date;

  if v_unit = 'day' then
    v_next := v_today + p_repeat_time;
    if v_next <= v_now then
      v_next := (v_today + 1) + p_repeat_time;
    end if;

  elsif v_unit = 'week' then
    if p_repeat_dow is null then
      raise exception 'repeat_dow_required';
    end if;
    v_cur_dow := extract(dow from v_today)::int;         -- 0=일 ~ 6=토
    v_delta   := ((p_repeat_dow - v_cur_dow) % 7 + 7) % 7;
    v_date    := v_today + v_delta;
    v_next    := v_date + p_repeat_time;
    if v_next <= v_now then
      v_next := (v_date + 7) + p_repeat_time;
    end if;

  elsif v_unit = 'month' then
    if p_repeat_dom is null then
      raise exception 'repeat_dom_required';
    end if;
    v_dim  := extract(day from (date_trunc('month', v_today::timestamp) + interval '1 month' - interval '1 day'))::int;
    v_date := date_trunc('month', v_today::timestamp)::date + (least(p_repeat_dom, v_dim) - 1);
    v_next := v_date + p_repeat_time;
    if v_next <= v_now then
      v_firstnext := (date_trunc('month', v_today::timestamp) + interval '1 month')::date;
      v_dim  := extract(day from (date_trunc('month', v_firstnext::timestamp) + interval '1 month' - interval '1 day'))::int;
      v_date := v_firstnext + (least(p_repeat_dom, v_dim) - 1);
      v_next := v_date + p_repeat_time;
    end if;

  else
    raise exception 'invalid_repeat_unit: %', v_unit;
  end if;

  return v_next at time zone v_tz;
end;
$$;

comment on function public.ts_mail_schedule_next_run(text,timestamptz,time,text,text,int,int) is
  '예약/반복 다음 실행 시각. repeat_unit=day(매일)/week(repeat_dow 0=일~6=토)/month(repeat_dom 1~31, 말일 초과 시 말일). repeat_tz 기준.';

-- ---------------------------------------------------------------------------
-- ts_run_due_mail_schedules — 만기 스케줄 실행(cron 매분). SECURITY DEFINER.
--   각 스케줄마다 ts_admin_send_mail 호출 후 scheduled=소진 / repeat=다음 시각 갱신.
--   한 건 실패해도 나머지는 계속 진행.
-- ---------------------------------------------------------------------------
create or replace function public.ts_run_due_mail_schedules()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  s record;
  n int := 0;
begin
  for s in
    select *
    from public.mail_schedules
    where is_active
      and next_run_at <= now()
    order by next_run_at asc
    for update skip locked
  loop
    begin
      perform public.ts_admin_send_mail(
        s.target_mode,
        s.title,
        now() + make_interval(days => s.expires_days),
        s.account_ids,
        s.server_id,
        s.content,
        s.sender_name,
        s.items,
        s.created_by,
        false,
        s.category,
        s.localized
      );

      if s.schedule_type = 'scheduled' then
        update public.mail_schedules
        set is_active = false,
            last_run_at = now(),
            run_count = run_count + 1
        where id = s.id;
      else
        update public.mail_schedules
        set next_run_at = public.ts_mail_schedule_next_run('repeat', null, s.repeat_time, s.repeat_tz, s.repeat_unit, s.repeat_dow, s.repeat_dom),
            last_run_at = now(),
            run_count = run_count + 1
        where id = s.id;
      end if;

      n := n + 1;
    exception
      when others then
        raise warning '[ts_run_due_mail_schedules] schedule % failed: %', s.id, sqlerrm;
    end;
  end loop;

  return n;
end;
$$;

comment on function public.ts_run_due_mail_schedules() is
  '만기 우편 스케줄을 발송(ts_admin_send_mail). scheduled=소진, repeat=다음 시각 갱신. cron 매분.';

revoke all on function public.ts_run_due_mail_schedules() from public;

-- ---------------------------------------------------------------------------
-- cron 등록 (멱등: 기존 잡 교체) — 매분
-- ---------------------------------------------------------------------------
do $$
begin
  if exists (select 1 from cron.job where jobname = 'run-mail-schedules') then
    perform cron.unschedule('run-mail-schedules');
  end if;
end $$;
select cron.schedule(
  'run-mail-schedules',
  '* * * * *',
  'select public.ts_run_due_mail_schedules()'
);

notify pgrst, 'reload schema';
