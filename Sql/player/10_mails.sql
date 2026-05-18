-- 서버에서 플레이어에게 아이템·메시지를 전달하는 우편함 기능을 제공합니다.
-- 수령(ts_claim_mail_items), 삭제(ts_delete_mail_for_user),
-- 만료 정리(ts_cleanup_expired_mails) RPC를 포함합니다.
--
-- =============================================================================
-- 플레이어 스키마 — mails (우편함) + RLS + 수령·삭제·만료 정리 RPC
-- 선행: 02_profiles.sql (auth_user_server_id), 01_game_servers.sql
-- 서버 스코프: mails.server_id 컬럼 없음 — profiles.server_id + auth_user_server_id() 조인
-- 선택: PostgREST 직접 변조 축소 — Sql/player/11_mails_hardening.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- mails
-- ---------------------------------------------------------------------------
create table if not exists public.mails (
  id uuid primary key default gen_random_uuid(),
  account_id uuid references auth.users (id) on delete set null,
  user_id text not null,
  sender_type text not null default 'system',
  sender_name text not null default '',
  title text not null default '',
  content text not null default '',
  is_read boolean not null default false,
  expires_at timestamptz not null,
  created_at timestamptz not null default now(),
  items jsonb null,
  items_claimed_at timestamptz null,
  deleted_at timestamptz null
);

alter table public.mails add column if not exists account_id uuid;
alter table public.mails add column if not exists user_id text;
alter table public.mails add column if not exists sender_type text;
alter table public.mails add column if not exists sender_name text;
alter table public.mails add column if not exists title text;
alter table public.mails add column if not exists content text;
alter table public.mails add column if not exists is_read boolean;
alter table public.mails add column if not exists expires_at timestamptz;
alter table public.mails add column if not exists created_at timestamptz;
alter table public.mails add column if not exists items jsonb;
alter table public.mails add column if not exists items_claimed_at timestamptz;
alter table public.mails add column if not exists deleted_at timestamptz;

do $$
begin
  if not exists (
    select 1 from pg_constraint c
    join pg_class t on c.conrelid = t.oid
    join pg_namespace n on t.relnamespace = n.oid
    where n.nspname = 'public' and t.relname = 'mails' and c.conname = 'mails_account_id_fkey'
  ) then
    alter table public.mails
      add constraint mails_account_id_fkey
      foreign key (account_id) references auth.users (id) on delete set null;
  end if;
end $$;

comment on table public.mails is '시스템 우편. RLS는 account_id + profiles.server_id 조인.';
comment on column public.mails.items is '보상 배열 [{key,count}, ...]. NULL/[] 는 텍스트 전용.';
comment on column public.mails.deleted_at is '플레이어 소프트 삭제(숨김).';

create index if not exists mails_account_id_created_idx on public.mails (account_id, created_at desc)
  where account_id is not null and deleted_at is null;
create index if not exists mails_account_id_expires_idx on public.mails (account_id, expires_at)
  where account_id is not null;
create index if not exists mails_user_id_created_idx on public.mails (user_id, created_at desc);
create index if not exists mails_expires_at_idx on public.mails (expires_at);

alter table public.mails enable row level security;

drop policy if exists "mails_select_own" on public.mails;
drop policy if exists "mails_update_own" on public.mails;

-- 본인 + profiles.user_id 일치 + 현재 세션 서버 일치 + 숨김 아님
create policy "mails_select_own"
on public.mails for select
using (
  account_id is not null
  and account_id = auth.uid()
  and deleted_at is null
  and exists (
    select 1
    from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = mails.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  )
);

create policy "mails_update_own"
on public.mails for update
using (
  account_id is not null
  and account_id = auth.uid()
  and exists (
    select 1
    from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = mails.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  )
)
with check (
  account_id is not null
  and account_id = auth.uid()
);

-- ---------------------------------------------------------------------------
-- RPC: 메일 상세 조회 — items 없음(NULL/비배열/빈 배열)이면 읽음 처리 후 반환
-- ---------------------------------------------------------------------------
create or replace function public.ts_view_mail_for_user(p_mail_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  m public.mails%rowtype;
  no_attachment boolean;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into m from public.mails where id = p_mail_id for update;
  if not found then
    raise exception 'mail_not_found';
  end if;

  if m.account_id is null or m.account_id <> auth.uid() then
    raise exception 'forbidden';
  end if;

  if not exists (
    select 1 from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = m.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  ) then
    raise exception 'forbidden_server';
  end if;

  if m.deleted_at is not null then
    raise exception 'mail_deleted';
  end if;

  no_attachment :=
    m.items is null
    or jsonb_typeof(m.items) <> 'array'
    or jsonb_array_length(m.items) = 0;

  if no_attachment then
    update public.mails set is_read = true where id = p_mail_id;
  end if;

  return (select to_jsonb(t) from public.mails t where t.id = p_mail_id);
end;
$$;

comment on function public.ts_view_mail_for_user(uuid) is
  '본인·프로필 서버 일치 메일 1건 JSON. 보상 items 없으면 is_read 갱신. SECURITY DEFINER.';

revoke all on function public.ts_view_mail_for_user(uuid) from public;
grant execute on function public.ts_view_mail_for_user(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 단일 메일 보상 일괄 수령 — 반환 jsonb 배열 [{index,key,count}, ...] (빈 배열 = no-op)
-- ---------------------------------------------------------------------------
create or replace function public.ts_claim_mail_items(p_mail_id uuid)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  m public.mails%rowtype;
  elem jsonb;
  items_out jsonb;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into m from public.mails where id = p_mail_id for update;
  if not found then
    raise exception 'mail_not_found';
  end if;

  if m.account_id is null or m.account_id <> auth.uid() then
    raise exception 'forbidden';
  end if;

  if not exists (
    select 1 from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = m.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  ) then
    raise exception 'forbidden_server';
  end if;

  if m.deleted_at is not null then
    raise exception 'mail_deleted';
  end if;

  if m.expires_at <= now() then
    raise exception 'mail_expired';
  end if;

  if m.items is null or jsonb_typeof(m.items) <> 'array' or jsonb_array_length(m.items) = 0 then
    return '[]'::jsonb;
  end if;

  if m.items_claimed_at is not null then
    raise exception 'already_claimed';
  end if;

  for elem in select * from jsonb_array_elements(m.items)
  loop
    if trim(coalesce(elem->>'key', '')) = '' then
      raise exception 'invalid_items_payload';
    end if;
    begin
      if (elem->>'count')::int is null or (elem->>'count')::int <= 0 then
        raise exception 'invalid_items_payload';
      end if;
    exception
      when others then
        raise exception 'invalid_items_payload';
    end;
  end loop;

  update public.mails
  set items_claimed_at = now(),
      is_read = true
  where id = p_mail_id;

  select coalesce(
    jsonb_agg(
      jsonb_build_object(
        'index', (t.ord::int - 1),
        'key', t.e->>'key',
        'count', (t.e->>'count')::int
      )
      order by t.ord
    ),
    '[]'::jsonb
  )
  into items_out
  from jsonb_array_elements(m.items) with ordinality as t(e, ord);

  return items_out;
end;
$$;

comment on function public.ts_claim_mail_items(uuid) is
  '본인·프로필 서버 일치 메일 보상 전부 수령(수령 시 읽음 처리). items 비면 [] 반환(no-op). SECURITY DEFINER.';

revoke all on function public.ts_claim_mail_items(uuid) from public;
grant execute on function public.ts_claim_mail_items(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 우편함 전체 일괄 수령 — 반환 [{mail_id, items:[{index,key,count},...]}, ...]
-- ---------------------------------------------------------------------------
create or replace function public.ts_claim_all_mail_items()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  r record;
  elem jsonb;
  items_out jsonb;
  acc jsonb := '[]'::jsonb;
  one_mail jsonb;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  -- 한 루프에서 검증·갱신·결과 누적(행 잠금 유지)
  for r in
    select m.id, m.items
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.expires_at > now()
      and m.items_claimed_at is null
      and m.items is not null
      and jsonb_typeof(m.items) = 'array'
      and jsonb_array_length(m.items) > 0
      and exists (
        select 1 from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
      )
    order by m.created_at asc
    for update of m
  loop
    for elem in select * from jsonb_array_elements(r.items)
    loop
      if trim(coalesce(elem->>'key', '')) = '' then
        raise exception 'invalid_items_payload';
      end if;
      begin
        if (elem->>'count')::int is null or (elem->>'count')::int <= 0 then
          raise exception 'invalid_items_payload';
        end if;
      exception
        when others then
          raise exception 'invalid_items_payload';
      end;
    end loop;

    update public.mails
    set items_claimed_at = now(),
        is_read = true
    where id = r.id;

    select coalesce(
      jsonb_agg(
        jsonb_build_object(
          'index', (t.ord::int - 1),
          'key', t.e->>'key',
          'count', (t.e->>'count')::int
        )
        order by t.ord
      ),
      '[]'::jsonb
    )
    into items_out
    from jsonb_array_elements(r.items) with ordinality as t(e, ord);

    one_mail := jsonb_build_object('mail_id', r.id::text, 'items', items_out);
    acc := acc || jsonb_build_array(one_mail);
  end loop;

  return acc;
end;
$$;

comment on function public.ts_claim_all_mail_items() is
  '미수령 보상 메일 전부 일괄 수령(수령 시 각 메일 읽음 처리). SECURITY DEFINER.';

revoke all on function public.ts_claim_all_mail_items() from public;
grant execute on function public.ts_claim_all_mail_items() to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 플레이어 소프트 삭제 (미수령 보상 있으면 거부)
-- ---------------------------------------------------------------------------
create or replace function public.ts_delete_mail_for_user(p_mail_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  m public.mails%rowtype;
  has_reward boolean;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  select * into m from public.mails where id = p_mail_id for update;
  if not found then
    raise exception 'mail_not_found';
  end if;

  if m.account_id is null or m.account_id <> auth.uid() then
    raise exception 'forbidden';
  end if;

  if not exists (
    select 1 from public.user_profiles p
    where p.account_id = auth.uid()
      and p.user_id = m.user_id
      and p.server_id is not null
      and p.server_id = public.auth_user_server_id()
  ) then
    raise exception 'forbidden_server';
  end if;

  if m.deleted_at is not null then
    return;
  end if;

  has_reward :=
    m.items is not null
    and jsonb_typeof(m.items) = 'array'
    and jsonb_array_length(m.items) > 0;

  if has_reward and m.items_claimed_at is null then
    raise exception 'cannot_delete_unclaimed';
  end if;

  update public.mails set deleted_at = now() where id = p_mail_id;
end;
$$;

comment on function public.ts_delete_mail_for_user(uuid) is
  '우편함에서 숨김. 보상 미수령이면 거부. SECURITY DEFINER.';

revoke all on function public.ts_delete_mail_for_user(uuid) from public;
grant execute on function public.ts_delete_mail_for_user(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 읽음 처리된 우편 일괄 소프트 삭제 (미수령 보상이 있는 메일은 제외)
-- ---------------------------------------------------------------------------
create or replace function public.ts_delete_read_mails_for_user()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  n int;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  with victims as (
    select m.id
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.is_read = true
      and exists (
        select 1 from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
      )
      and not (
        m.items is not null
        and jsonb_typeof(m.items) = 'array'
        and jsonb_array_length(m.items) > 0
        and m.items_claimed_at is null
      )
  )
  update public.mails u
  set deleted_at = now()
  from victims v
  where u.id = v.id;

  get diagnostics n = row_count;
  return coalesce(n, 0);
end;
$$;

comment on function public.ts_delete_read_mails_for_user() is
  'is_read 이고 삭제 가능한(미수령 보상 없음) 메일만 일괄 숨김. 반환: 처리 행 수. SECURITY DEFINER.';

revoke all on function public.ts_delete_read_mails_for_user() from public;
grant execute on function public.ts_delete_read_mails_for_user() to authenticated;

-- ---------------------------------------------------------------------------
-- 만료 메일 하드 삭제 (서비스 롤·cron 전용)
-- ---------------------------------------------------------------------------
create or replace function public.ts_cleanup_expired_mails(p_batch int default 500)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  n int;
begin
  if p_batch is null or p_batch < 1 then
    p_batch := 500;
  end if;
  if p_batch > 10000 then
    p_batch := 10000;
  end if;

  delete from public.mails
  where id in (
    select m.id
    from public.mails m
    where m.expires_at < now()
    limit p_batch
  );

  get diagnostics n = row_count;
  return n;
end;
$$;

comment on function public.ts_cleanup_expired_mails(int) is
  'expires_at < now() 인 메일을 배치 삭제. service_role 전용 호출 권장.';

revoke all on function public.ts_cleanup_expired_mails(int) from public;
grant execute on function public.ts_cleanup_expired_mails(int) to service_role;

-- ---------------------------------------------------------------------------
-- 우편함 배지: 미읽음 수 · 미수령 보상 메일 수
-- ---------------------------------------------------------------------------
create or replace function public.ts_mail_inbox_counts()
returns jsonb
language sql
security definer
set search_path = public
as $$
  select case
    when auth.uid() is null then null
    else jsonb_build_object(
      'unread',
      coalesce((
        select count(*)::int
        from public.mails m
        where m.account_id = auth.uid()
          and m.deleted_at is null
          and m.is_read = false
          and m.expires_at > now()
          and exists (
            select 1
            from public.user_profiles p
            where p.account_id = auth.uid()
              and p.user_id = m.user_id
              and p.server_id is not null
              and p.server_id = public.auth_user_server_id()
          )
      ), 0),
      'unclaimed_mails',
      coalesce((
        select count(*)::int
        from public.mails m
        where m.account_id = auth.uid()
          and m.deleted_at is null
          and m.items_claimed_at is null
          and m.expires_at > now()
          and m.items is not null
          and jsonb_typeof(m.items) = 'array'
          and jsonb_array_length(m.items) > 0
          and exists (
            select 1
            from public.user_profiles p
            where p.account_id = auth.uid()
              and p.user_id = m.user_id
              and p.server_id is not null
              and p.server_id = public.auth_user_server_id()
          )
      ), 0)
    )
  end;
$$;

comment on function public.ts_mail_inbox_counts() is
  '미읽음·미수령 보상 메일 개수. SECURITY DEFINER.';

revoke all on function public.ts_mail_inbox_counts() from public;
grant execute on function public.ts_mail_inbox_counts() to authenticated;
