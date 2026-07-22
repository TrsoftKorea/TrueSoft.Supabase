-- 어드민 우편 발송 인프라를 제공합니다.
-- 아이템 카탈로그(game_items) + 발송 캠페인 그룹(mail_batches) + mails.batch_id +
-- 발송/카탈로그 관리 RPC. 클라이언트 우편 계층(06_mails)은 건드리지 않습니다.
-- 발송·카탈로그 쓰기는 모두 SECURITY DEFINER RPC(service_role)로만 수행합니다.
--
-- =============================================================================
-- 어드민 우편 발송 — game_items + mail_batches + mails.batch_id + RPC
-- 선행: 01_servers.sql, 02_profiles.sql, 06_mails.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- game_items — 아이템 카탈로그(발송 선택 편의·표시명). 실제 지급은 게임 IMailItemHandler.
-- ---------------------------------------------------------------------------
create table if not exists public.game_items (
  key          text primary key,
  display_name text not null default '',
  created_at   timestamptz not null default now()
);
alter table public.game_items add column if not exists display_name text not null default '';
alter table public.game_items add column if not exists created_at   timestamptz not null default now();
alter table public.game_items drop column if exists description;
alter table public.game_items drop column if exists category;
alter table public.game_items drop column if exists sort_order;
alter table public.game_items drop column if exists is_active;
alter table public.game_items drop column if exists updated_at;

drop index if exists public.game_items_category_sort_idx;
drop index if exists public.game_items_active_idx;

comment on table public.game_items is '어드민 우편 발송용 아이템 카탈로그. mails.items[].key 선택 편의·표시명. 실제 지급은 게임 IMailItemHandler.';

alter table public.game_items enable row level security;
drop policy if exists "game_items_select_all" on public.game_items;
create policy "game_items_select_all" on public.game_items for select using (true);
revoke all on table public.game_items from anon;
revoke insert, update, delete on table public.game_items from authenticated;
grant select on table public.game_items to authenticated;
-- service_role DML 제거(모든 쓰기는 RPC). 프로젝트 기본 권한 차이와 무관하게 동일 상태 보장.
revoke select, insert, update, delete on table public.game_items from service_role;

-- ---------------------------------------------------------------------------
-- mail_batches — 한 번의 발송(캠페인) 그룹. 이력·집계 스냅샷 보존.
-- ---------------------------------------------------------------------------
create table if not exists public.mail_batches (
  id              uuid primary key default gen_random_uuid(),
  target_mode     text not null check (target_mode in ('all','server','players')),
  server_id       uuid null references public.game_servers (id) on delete set null,
  title           text not null default '',
  content         text not null default '',
  sender_name     text not null default '',
  items           jsonb null,
  expires_at      timestamptz not null,
  recipient_count int not null default 0,
  created_by      text null,
  created_at      timestamptz not null default now(),
  category        text not null default 'default'
);
alter table public.mail_batches add column if not exists target_mode     text;
alter table public.mail_batches add column if not exists server_id       uuid;
alter table public.mail_batches add column if not exists title           text;
alter table public.mail_batches add column if not exists content         text;
alter table public.mail_batches add column if not exists sender_name     text;
alter table public.mail_batches add column if not exists items           jsonb;
alter table public.mail_batches add column if not exists expires_at      timestamptz;
alter table public.mail_batches add column if not exists recipient_count int not null default 0;
alter table public.mail_batches add column if not exists created_by      text;
alter table public.mail_batches add column if not exists created_at      timestamptz not null default now();
alter table public.mail_batches add column if not exists category        text not null default 'default';

create index if not exists mail_batches_created_idx on public.mail_batches (created_at desc);

comment on table public.mail_batches is '어드민 우편 발송 캠페인 그룹. recipient_count는 발송 시점 스냅샷(권위값).';
comment on column public.mail_batches.category is '발송 캠페인 분류(자유 텍스트, 기본값 default). 수신자 mails.category와 동일 값.';

alter table public.mail_batches enable row level security;
revoke all on table public.mail_batches from anon, authenticated;
revoke select, insert, update, delete on table public.mail_batches from service_role;

-- ---------------------------------------------------------------------------
-- mails.batch_id — 발송 그룹 연결(만료 하드삭제돼도 batch 이력은 보존)
-- ---------------------------------------------------------------------------
alter table public.mails add column if not exists batch_id uuid;
do $$
begin
  if not exists (
    select 1 from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public' and t.relname = 'mails' and c.conname = 'mails_batch_id_fkey'
  ) then
    alter table public.mails
      add constraint mails_batch_id_fkey
      foreign key (batch_id) references public.mail_batches (id) on delete set null;
  end if;
end $$;
create index if not exists mails_batch_id_idx on public.mails (batch_id) where batch_id is not null;

-- mails 도 service_role DML 제거(발송은 ts_admin_send_mail RPC(owner) 경유). 프로젝트 간 동일 상태 보장.
revoke select, insert, update, delete on table public.mails from service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_send_mail — 대상(all/server/players) 해석 + 수신자별 mails INSERT
--   p_target_mode : 'all' | 'server' | 'players'
--   p_account_ids : players 모드에서 user_profiles.account_id 의 jsonb 배열 (["uuid", ...])
--   p_server_id   : server 모드 대상 서버
--   p_items       : 보상 배열 [{key,count}]. game_items 검증(우회 = p_skip_item_validation)
-- 반환: {batch_id, recipient_count}
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean);

create or replace function public.ts_admin_send_mail(
  p_target_mode          text,
  p_title                text,
  p_expires_at           timestamptz,
  p_account_ids          jsonb   default null,
  p_server_id            uuid    default null,
  p_content              text    default '',
  p_sender_name          text    default '',
  p_items                jsonb   default null,
  p_created_by           text    default null,
  p_skip_item_validation boolean default false,
  p_category             text    default 'default',
  p_localized            jsonb   default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_batch_id    uuid;
  v_count       int;
  v_item        jsonb;
  v_key         text;
  v_account_ids uuid[];
  v_category    text;
begin
  if p_target_mode not in ('all','server','players') then
    raise exception 'invalid_target_mode: %', p_target_mode;
  end if;
  if p_title is null or btrim(p_title) = '' then
    raise exception 'title_empty';
  end if;
  if p_expires_at is null or p_expires_at <= now() then
    raise exception 'invalid_expires_at';
  end if;
  if p_target_mode = 'server' and p_server_id is null then
    raise exception 'server_id_required';
  end if;

  v_category := coalesce(nullif(btrim(p_category), ''), 'default');

  if p_account_ids is not null and jsonb_typeof(p_account_ids) = 'array' then
    select array_agg((value)::uuid) into v_account_ids
    from jsonb_array_elements_text(p_account_ids) as t(value);
  end if;

  if p_target_mode = 'players' and (v_account_ids is null or array_length(v_account_ids, 1) is null) then
    raise exception 'account_ids_required';
  end if;

  -- items 검증(있을 때): 배열·각 key non-empty·count 양의 정수·카탈로그 존재
  if p_items is not null then
    if jsonb_typeof(p_items) <> 'array' then
      raise exception 'items_not_array';
    end if;
    for v_item in select value from jsonb_array_elements(p_items) as t(value) loop
      v_key := v_item->>'key';
      if v_key is null or btrim(v_key) = '' then
        raise exception 'item_key_empty';
      end if;
      if (v_item->>'count') is null
         or (v_item->>'count') !~ '^[0-9]+$'
         or (v_item->>'count')::int <= 0 then
        raise exception 'item_count_invalid: %', v_key;
      end if;
      if not p_skip_item_validation
         and not exists (select 1 from public.game_items gi where gi.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  insert into public.mail_batches
    (target_mode, server_id, title, content, sender_name, items, expires_at, created_by, category, localized)
  values
    (p_target_mode,
     case when p_target_mode = 'server' then p_server_id else null end,
     p_title, coalesce(p_content, ''), coalesce(p_sender_name, ''),
     p_items, p_expires_at, p_created_by, v_category, p_localized)
  returning id into v_batch_id;

  insert into public.mails
    (account_id, user_id, sender_type, sender_name, title, content,
     expires_at, created_at, items, batch_id, category, localized)
  select p.account_id, p.user_id, 'system', coalesce(p_sender_name, ''),
         p_title, coalesce(p_content, ''), p_expires_at, now(), p_items, v_batch_id, v_category, p_localized
  from public.user_profiles p
  where p.account_id is not null
    and p.withdrawn_at is null
    and (
      p_target_mode = 'all'
      or (p_target_mode = 'server'  and p.server_id = p_server_id)
      or (p_target_mode = 'players' and p.account_id = any (v_account_ids))
    );

  get diagnostics v_count = row_count;
  update public.mail_batches set recipient_count = v_count where id = v_batch_id;

  return jsonb_build_object('batch_id', v_batch_id, 'recipient_count', v_count);
end;
$$;

comment on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean,text,jsonb) is
  '어드민 우편 발송. 대상 all/server/players(account_id jsonb 배열) 해석(탈퇴 제외) → 수신자별 mails INSERT + mail_batches 스냅샷. items는 game_items 검증(우회 플래그). p_category 비었거나 null이면 default. p_localized는 언어별 제목·본문 오버라이드(없으면 base fallback).';

-- ---------------------------------------------------------------------------
-- ts_admin_upsert_game_item / ts_admin_delete_game_item — 카탈로그 관리
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_upsert_game_item(text,text,text,text,int);

create or replace function public.ts_admin_upsert_game_item(
  p_key          text,
  p_display_name text
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_key is null or btrim(p_key) = '' then
    raise exception 'key_empty';
  end if;
  insert into public.game_items (key, display_name)
  values (btrim(p_key), coalesce(p_display_name, ''))
  on conflict (key) do update set
    display_name = excluded.display_name;
end;
$$;

comment on function public.ts_admin_upsert_game_item(text,text) is
  '아이템 카탈로그 upsert(어드민). 발송된 mails.items 에는 영향 없음.';

create or replace function public.ts_admin_delete_game_item(p_key text)
returns void
language sql
security definer
set search_path = public
as $$
  delete from public.game_items where key = p_key;
$$;

comment on function public.ts_admin_delete_game_item(text) is
  '아이템 카탈로그 삭제(어드민). 카탈로그만 제거, 발송 이력 불변.';

-- ---------------------------------------------------------------------------
-- ts_admin_count_recipients — 발송 전 대상 수 프리뷰(all/server)
-- ---------------------------------------------------------------------------
create or replace function public.ts_admin_count_recipients(
  p_target_mode text,
  p_server_id   uuid default null
)
returns int
language sql
stable
security definer
set search_path = public
as $$
  select count(*)::int
  from public.user_profiles p
  where p.account_id is not null
    and p.withdrawn_at is null
    and (p_target_mode = 'all' or (p_target_mode = 'server' and p.server_id = p_server_id));
$$;

comment on function public.ts_admin_count_recipients(text,uuid) is
  '발송 전 대상 수 프리뷰(all/server). 탈퇴 제외.';

-- ---------------------------------------------------------------------------
-- 어드민 RPC EXECUTE — service_role 전용(클라이언트 금지)
-- ---------------------------------------------------------------------------
revoke all on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean,text,jsonb) from public, anon, authenticated;
grant execute on function public.ts_admin_send_mail(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean,text,jsonb) to service_role;
revoke all on function public.ts_admin_upsert_game_item(text,text) from public, anon, authenticated;
grant execute on function public.ts_admin_upsert_game_item(text,text) to service_role;
revoke all on function public.ts_admin_delete_game_item(text) from public, anon, authenticated;
grant execute on function public.ts_admin_delete_game_item(text) to service_role;
revoke all on function public.ts_admin_count_recipients(text,uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_count_recipients(text,uuid) to service_role;

notify pgrst, 'reload schema';
