-- 서버에서 플레이어에게 아이템·메시지를 전달하는 우편함 기능을 제공합니다.
-- 수령(ts_claim_mail_items), 삭제(ts_delete_mail_for_user),
-- 만료 정리(ts_cleanup_expired_mails) RPC를 포함합니다.
-- 클라이언트 직접 쓰기는 차단되며 모든 우편 조작은 RPC를 통해서만 가능합니다.
--
-- =============================================================================
-- 우편함 (mails) — 테이블 + RLS + RPC
-- 선행: 01_servers.sql, 02_profiles.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- mails
-- ---------------------------------------------------------------------------
create table if not exists public.mails (
  id uuid primary key default gen_random_uuid(),
  account_id uuid references auth.users (id) on delete set null,
  user_id text not null,
  sender_type text not null default 'system',
  title text not null default '',
  content text not null default '',
  expires_at timestamptz not null,
  created_at timestamptz not null default now(),
  items jsonb null,
  items_claimed_at timestamptz null,
  deleted_at timestamptz null,
  category text not null default 'default'
);

alter table public.mails add column if not exists account_id uuid;
alter table public.mails add column if not exists user_id text;
alter table public.mails add column if not exists sender_type text;
alter table public.mails add column if not exists title text;
alter table public.mails add column if not exists content text;
alter table public.mails add column if not exists expires_at timestamptz;
alter table public.mails add column if not exists created_at timestamptz;
alter table public.mails add column if not exists items jsonb;
alter table public.mails add column if not exists items_claimed_at timestamptz;
alter table public.mails add column if not exists deleted_at timestamptz;
alter table public.mails add column if not exists category text not null default 'default';

-- is_read 폐지: items_claimed_at 단일 축(수령/미수령)으로 통일합니다.
-- 기존에 열람됐던 텍스트 메일을 수령 상태로 보존한 뒤 컬럼을 제거합니다(재실행 안전).
do $$
begin
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'mails' and column_name = 'is_read'
  ) then
    update public.mails set items_claimed_at = now()
      where items_claimed_at is null and is_read is true;
    alter table public.mails drop column is_read;
  end if;
end $$;

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
comment on column public.mails.category is
  '분류 파티션 키(자유 텍스트, 기본값 default). RPC p_category 파라미터로 필터링. 카탈로그 테이블 없음.';

create index if not exists mails_account_id_created_idx on public.mails (account_id, created_at desc)
  where account_id is not null and deleted_at is null;
create index if not exists mails_account_id_expires_idx on public.mails (account_id, expires_at)
  where account_id is not null;
create index if not exists mails_user_id_created_idx on public.mails (user_id, created_at desc);
create index if not exists mails_expires_at_idx on public.mails (expires_at);
create index if not exists mails_account_id_category_created_idx on public.mails (account_id, category, created_at desc)
  where account_id is not null and deleted_at is null;

-- 위 인덱스는 전부 부분 인덱스(account_id is not null, deleted_at is null)라 게임 클라이언트 조회 전용이다.
-- 어드민 우편 내역은 계정으로 좁히지 않고 삭제된 우편도 보므로 그 인덱스들을 탈 수 없어, 전체 정렬용을 따로 둔다.
create index if not exists mails_created_at_idx on public.mails (created_at desc);

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

  -- 첨부 없는 텍스트 메일은 열람 시 수령 처리(items_claimed_at). 보상 메일은 수령 시에만 처리.
  if no_attachment and m.items_claimed_at is null then
    update public.mails set items_claimed_at = now() where id = p_mail_id;
  end if;

  return (select to_jsonb(t) from public.mails t where t.id = p_mail_id);
end;
$$;

comment on function public.ts_view_mail_for_user(uuid) is
  '본인·프로필 서버 일치 메일 1건 JSON. 보상 items 없으면 열람 시 items_claimed_at(수령) 처리. SECURITY DEFINER.';

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
  set items_claimed_at = now()
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
  '본인·프로필 서버 일치 메일 보상 전부 수령(items_claimed_at). items 비면 [] 반환(no-op). SECURITY DEFINER.';

revoke all on function public.ts_claim_mail_items(uuid) from public;
grant execute on function public.ts_claim_mail_items(uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- RPC: 우편함 전체 일괄 수령 — 반환 [{mail_id, items:[{index,key,count},...]}, ...]
-- ---------------------------------------------------------------------------
drop function if exists public.ts_claim_all_mail_items();

create or replace function public.ts_claim_all_mail_items(p_category text default null)
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
  v_category text;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  v_category := nullif(btrim(p_category), '');

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
      and (v_category is null or m.category = v_category)
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
    set items_claimed_at = now()
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

comment on function public.ts_claim_all_mail_items(text) is
  '미수령 보상 메일 전부 일괄 수령(items_claimed_at). p_category=null이면 전체 분류. SECURITY DEFINER.';

revoke all on function public.ts_claim_all_mail_items(text) from public;
grant execute on function public.ts_claim_all_mail_items(text) to authenticated;

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
-- RPC: 수령한 우편 일괄 소프트 삭제 (미수령 = items_claimed_at is null 은 제외)
-- ---------------------------------------------------------------------------
drop function if exists public.ts_delete_read_mails_for_user();
drop function if exists public.ts_delete_read_mails_for_user(text);
drop function if exists public.ts_delete_claimed_mails_for_user();

create or replace function public.ts_delete_claimed_mails_for_user(p_category text default null)
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  n int;
  v_category text;
begin
  if auth.uid() is null then
    raise exception 'not_authenticated';
  end if;

  v_category := nullif(btrim(p_category), '');

  with victims as (
    select m.id
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.items_claimed_at is not null
      and (v_category is null or m.category = v_category)
      and exists (
        select 1 from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
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

comment on function public.ts_delete_claimed_mails_for_user(text) is
  '수령한(items_claimed_at) 메일만 일괄 숨김. 미수령은 제외. p_category=null이면 전체 분류. 반환: 처리 행 수. SECURITY DEFINER.';

revoke all on function public.ts_delete_claimed_mails_for_user(text) from public;
grant execute on function public.ts_delete_claimed_mails_for_user(text) to authenticated;

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
-- 우편함 배지: 미수령 메일 수 (items_claimed_at is null — 미열람 텍스트 + 미수령 보상)
-- ---------------------------------------------------------------------------
create or replace function public.ts_mail_inbox_counts()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
  v_unclaimed int;
  v_by_category jsonb;
begin
  if auth.uid() is null then
    return null;
  end if;

  with my_mails as (
    select m.category, m.items_claimed_at
    from public.mails m
    where m.account_id = auth.uid()
      and m.deleted_at is null
      and m.expires_at > now()
      and exists (
        select 1
        from public.user_profiles p
        where p.account_id = auth.uid()
          and p.user_id = m.user_id
          and p.server_id is not null
          and p.server_id = public.auth_user_server_id()
      )
  ),
  per_category as (
    select category,
           count(*) filter (where items_claimed_at is null)::int as unclaimed
    from my_mails
    group by category
  )
  select
    coalesce(sum(unclaimed), 0)::int,
    coalesce(
      jsonb_object_agg(category, jsonb_build_object('unclaimed', unclaimed)),
      '{}'::jsonb
    )
  into v_unclaimed, v_by_category
  from per_category;

  return jsonb_build_object(
    'unclaimed', v_unclaimed,
    'by_category', v_by_category
  );
end;
$$;

comment on function public.ts_mail_inbox_counts() is
  '미수령 메일 개수(items_claimed_at is null, 전체 집계) + by_category 분류별 세부 내역. SECURITY DEFINER.';

revoke all on function public.ts_mail_inbox_counts() from public;
grant execute on function public.ts_mail_inbox_counts() to authenticated;

-- ---------------------------------------------------------------------------
-- 우편함 쓰기 권한 강화 — 클라이언트(authenticated) 직접 INSERT/UPDATE/DELETE 차단
-- ---------------------------------------------------------------------------
-- 우편 변경은 RPC(ts_view_mail_for_user, ts_claim_*, ts_delete_*)만 쓰도록 좁힙니다.
-- [시스템 메일 발송은 service_role / SQL Editor / Edge Function만 사용해야 합니다]
-- 롤백: grant insert, update, delete on table public.mails to authenticated;

-- 직접 UPDATE 경로 제거 (읽음·수령·삭제는 전부 SECURITY DEFINER RPC)
drop policy if exists "mails_update_own" on public.mails;

revoke insert, update, delete on table public.mails from authenticated;

-- anon 은 보통 mails 미사용. 목록 REST가 anon 이면 아래 한 줄 대신 grant select 만 조정.
revoke all on table public.mails from anon;

grant select on table public.mails to authenticated;
