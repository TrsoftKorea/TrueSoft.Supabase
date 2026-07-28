-- 쿠폰 기능을 제공합니다.
-- 쿠폰 정의·발급은 운영(service_role)이 하고, 게임은 코드를 보내 사용하는 것만 합니다.
-- 보상은 게임에 직접 주지 않고 우편(mails)으로 넣습니다 — 실제 지급은 기존 IMailItemHandler 경로를 그대로 씁니다.
--
-- 쿠폰은 두 종류입니다.
--   normal  : 발급 수량만큼 랜덤 코드를 만들고, 코드 1개당 1회만 쓸 수 있습니다.
--   keyword : 운영자가 코드를 직접 정하고, 최대 횟수까지 여러 명이 씁니다(한 플레이어는 1회).
--
-- =============================================================================
-- 쿠폰 — coupons + coupon_codes + coupon_redemptions + RPC
-- 선행: 02_profiles.sql(user_profiles), 06_mails.sql(mails), 12_admin_mail.sql(game_items)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- coupons — 쿠폰 정의. 보상·유효기간·사용 규칙이 모두 이 행의 데이터다.
--   expires_at 이 null 이면 무기한, max_uses 가 null 이면 무제한(keyword 전용).
-- ---------------------------------------------------------------------------
create table if not exists public.coupons (
  id                uuid        primary key default gen_random_uuid(),
  kind              text        not null default 'normal',
  title             text        not null default '',
  content           text        not null default '',
  category          text        not null default 'default',
  items             jsonb,
  localized         jsonb,
  expires_at        timestamptz,
  mail_expires_days int         not null default 7,
  max_uses          int,
  used_count        int         not null default 0,
  is_active         boolean     not null default true,
  created_by        text,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

alter table public.coupons add column if not exists kind              text        not null default 'normal';
alter table public.coupons add column if not exists title             text        not null default '';
alter table public.coupons add column if not exists content           text        not null default '';
alter table public.coupons add column if not exists category          text        not null default 'default';
alter table public.coupons add column if not exists items             jsonb;
alter table public.coupons add column if not exists localized         jsonb;
alter table public.coupons add column if not exists expires_at        timestamptz;
alter table public.coupons add column if not exists mail_expires_days int         not null default 7;
alter table public.coupons add column if not exists max_uses          int;
alter table public.coupons add column if not exists used_count        int         not null default 0;
alter table public.coupons add column if not exists is_active         boolean     not null default true;
alter table public.coupons add column if not exists created_by        text;
alter table public.coupons add column if not exists created_at        timestamptz not null default now();
alter table public.coupons add column if not exists updated_at        timestamptz not null default now();

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'coupons_kind_chk') then
    alter table public.coupons
      add constraint coupons_kind_chk check (kind in ('normal', 'keyword'));
  end if;
  if not exists (select 1 from pg_constraint where conname = 'coupons_mail_expires_days_chk') then
    alter table public.coupons
      add constraint coupons_mail_expires_days_chk check (mail_expires_days > 0);
  end if;
end $$;

create index if not exists coupons_active_idx  on public.coupons (is_active, expires_at);
create index if not exists coupons_created_idx on public.coupons (created_at desc);

comment on table  public.coupons is
  '쿠폰 정의. 보상은 items(jsonb 배열 [{key,count}])이며 사용 시 mails 로 지급된다. kind=normal 은 코드 1회, keyword 는 1인 1회.';
comment on column public.coupons.expires_at is '쿠폰 사용 마감. null 이면 무기한.';
comment on column public.coupons.max_uses   is 'keyword 전용 최대 사용 횟수. null 이면 무제한.';
comment on column public.coupons.mail_expires_days is '지급 우편의 수령 기한(일). 쿠폰 만료와 별개다.';

-- ---------------------------------------------------------------------------
-- coupon_codes — 발급된 코드. normal 은 수량만큼, keyword 는 1개.
--   코드는 항상 대문자로 정규화해 저장한다(입력도 대문자로 맞춰 비교).
-- ---------------------------------------------------------------------------
create table if not exists public.coupon_codes (
  code       text        primary key,
  coupon_id  uuid        not null references public.coupons(id) on delete cascade,
  created_at timestamptz not null default now()
);

alter table public.coupon_codes add column if not exists created_at timestamptz not null default now();

create index if not exists coupon_codes_coupon_idx on public.coupon_codes (coupon_id);

comment on table public.coupon_codes is '발급된 쿠폰 코드. 대문자 정규화. 쿠폰 삭제 시 함께 삭제된다.';

-- ---------------------------------------------------------------------------
-- coupon_redemptions — 사용 이력 전체.
--   중복 사용 차단을 이 테이블의 부분 유니크 인덱스로 표현한다. 종류마다 규칙이 다르므로
--   kind 를 비정규화해 둔다.
--     normal  : 코드 1개는 한 번만       → unique (code)
--     keyword : 한 플레이어는 1회         → unique (coupon_id, account_id)
--   normal 은 같은 쿠폰의 다른 코드를 여러 장 받았다면 각각 쓸 수 있다(코드 = 개별 선물).
-- ---------------------------------------------------------------------------
create table if not exists public.coupon_redemptions (
  id          bigserial   primary key,
  coupon_id   uuid        not null references public.coupons(id) on delete cascade,
  code        text        not null,
  kind        text        not null,
  account_id  uuid        not null,
  user_id     text        not null default '',
  mail_id     uuid,
  redeemed_at timestamptz not null default now()
);

alter table public.coupon_redemptions add column if not exists kind        text        not null default 'normal';
alter table public.coupon_redemptions add column if not exists user_id     text        not null default '';
alter table public.coupon_redemptions add column if not exists mail_id     uuid;
alter table public.coupon_redemptions add column if not exists redeemed_at timestamptz not null default now();

create unique index if not exists coupon_redemptions_code_uidx
  on public.coupon_redemptions (code) where kind = 'normal';
create unique index if not exists coupon_redemptions_keyword_uidx
  on public.coupon_redemptions (coupon_id, account_id) where kind = 'keyword';

create index if not exists coupon_redemptions_coupon_idx  on public.coupon_redemptions (coupon_id, redeemed_at desc);
create index if not exists coupon_redemptions_account_idx on public.coupon_redemptions (account_id, redeemed_at desc);

comment on table public.coupon_redemptions is
  '쿠폰 사용 이력. 부분 유니크 인덱스가 중복 사용을 막는다(normal=코드 1회, keyword=1인 1회).';

-- ---------------------------------------------------------------------------
-- 권한 — 세 테이블 모두 클라이언트 직접 접근 없음. 사용은 ts_coupon_redeem RPC 로만.
-- ---------------------------------------------------------------------------
alter table public.coupons            enable row level security;
alter table public.coupon_codes       enable row level security;
alter table public.coupon_redemptions enable row level security;

revoke all on table public.coupons            from anon, authenticated;
revoke all on table public.coupon_codes       from anon, authenticated;
revoke all on table public.coupon_redemptions from anon, authenticated;


-- =============================================================================
-- 클라이언트 RPC
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_coupon_redeem — 쿠폰 사용. 성공하면 보상 우편이 생성된다.
--   실패 사유: coupon_not_found · coupon_inactive · coupon_expired
--              coupon_already_used · coupon_exhausted
-- ---------------------------------------------------------------------------
drop function if exists public.ts_coupon_redeem(text);

create or replace function public.ts_coupon_redeem(p_code text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid    uuid := auth.uid();
  v_user   text;
  v_code   text;
  c        public.coupons%rowtype;
  v_red_id bigint;
  v_mail   uuid;
begin
  if v_uid is null then
    raise exception 'not_authenticated';
  end if;

  v_code := upper(btrim(coalesce(p_code, '')));
  if v_code = '' then
    raise exception 'coupon_not_found';
  end if;

  select cp.* into c
    from public.coupon_codes cc
    join public.coupons cp on cp.id = cc.coupon_id
   where cc.code = v_code;
  if not found then
    raise exception 'coupon_not_found';
  end if;

  if not c.is_active then
    raise exception 'coupon_inactive';
  end if;
  if c.expires_at is not null and c.expires_at <= now() then
    raise exception 'coupon_expired';
  end if;

  select p.user_id into v_user from public.user_profiles p where p.account_id = v_uid;
  v_user := coalesce(v_user, v_uid::text);

  -- 이력을 먼저 넣어 중복 사용을 유니크 인덱스로 잡는다.
  -- 뒤에서 예외가 나면 함수 전체가 롤백되므로 이력만 남는 일은 없다.
  begin
    insert into public.coupon_redemptions (coupon_id, code, kind, account_id, user_id)
    values (c.id, v_code, c.kind, v_uid, v_user)
    returning id into v_red_id;
  exception when unique_violation then
    raise exception 'coupon_already_used';
  end;

  -- 키워드는 최대 횟수를 원자적으로 확인·증가한다(동시 요청에서 초과 지급 방지).
  if c.kind = 'keyword' then
    update public.coupons
       set used_count = used_count + 1, updated_at = now()
     where id = c.id
       and (max_uses is null or used_count < max_uses);
    if not found then
      raise exception 'coupon_exhausted';
    end if;
  else
    update public.coupons
       set used_count = used_count + 1, updated_at = now()
     where id = c.id;
  end if;

  -- 발신자 이름은 비워 둔다. sender_type='system' 이면 게임이 자기 로케일로 표기하므로
  -- DB에 특정 언어 문자열을 박으면 다른 언어권 플레이어에게 그대로 나간다.
  insert into public.mails
    (account_id, user_id, sender_type, sender_name, title, content,
     expires_at, created_at, items, category, localized)
  values
    (v_uid, v_user, 'system', '', c.title, c.content,
     now() + make_interval(days => c.mail_expires_days), now(), c.items, c.category, c.localized)
  returning id into v_mail;

  update public.coupon_redemptions set mail_id = v_mail where id = v_red_id;

  return jsonb_build_object('mail_id', v_mail);
end;
$$;

comment on function public.ts_coupon_redeem(text) is
  '쿠폰 사용. 코드 검증 후 보상을 우편으로 지급하고 이력을 남긴다. 중복 사용은 부분 유니크 인덱스로 차단.';

revoke all on function public.ts_coupon_redeem(text) from public, anon;
grant execute on function public.ts_coupon_redeem(text) to authenticated;


-- =============================================================================
-- 운영자 RPC (service_role 전용)
-- =============================================================================

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_create — 쿠폰 생성. kind 로 분기한다.
--   normal  : p_prefix + 랜덤 p_random_len 자리로 p_quantity 개 생성
--   keyword : p_code 1개 생성, p_max_uses 로 최대 횟수 지정(null=무제한)
--   랜덤 문자셋에서 혼동하기 쉬운 0·O·1·I 는 뺀다.
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_coupon_create(text,text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text);
drop function if exists public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text);

create or replace function public.ts_admin_coupon_create(
  p_kind              text,
  p_title             text,
  p_content           text    default '',
  p_category          text    default 'default',
  p_items             jsonb   default null,
  p_localized         jsonb   default null,
  p_expires_at        timestamptz default null,
  p_mail_expires_days int     default 7,
  p_is_active         boolean default true,
  p_code              text    default null,
  p_max_uses          int     default null,
  p_prefix            text    default '',
  p_random_len        int     default 6,
  p_quantity          int     default 1,
  p_created_by        text    default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_charset  constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  v_id       uuid;
  v_key      text;
  v_cnt      text;
  v_prefix   text;
  v_code     text;
  v_made     int := 0;
  v_attempts int := 0;
  v_max_try  int;
  i          int;
begin
  if p_kind not in ('normal', 'keyword') then
    raise exception 'invalid_coupon_kind: %', p_kind;
  end if;
  if coalesce(btrim(p_title), '') = '' then
    raise exception 'title_empty';
  end if;
  if coalesce(p_mail_expires_days, 0) <= 0 then
    raise exception 'invalid_mail_expires_days';
  end if;

  -- 보상 아이템은 카탈로그에 있는 키만 허용한다(우편 발송과 같은 규칙).
  if p_items is not null then
    if jsonb_typeof(p_items) <> 'array' then
      raise exception 'items_not_array';
    end if;
    for v_key, v_cnt in
      select e->>'key', e->>'count' from jsonb_array_elements(p_items) e
    loop
      if coalesce(btrim(v_key), '') = '' then
        raise exception 'item_key_empty';
      end if;
      if v_cnt !~ '^[0-9]+$' or v_cnt::bigint <= 0 then
        raise exception 'item_count_invalid: %', v_key;
      end if;
      if not exists (select 1 from public.game_items g where g.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  insert into public.coupons
    (kind, title, content, category, items, localized,
     expires_at, mail_expires_days, max_uses, is_active, created_by)
  values
    (p_kind, btrim(p_title), coalesce(p_content, ''),
     coalesce(nullif(btrim(p_category), ''), 'default'), p_items, p_localized,
     p_expires_at, p_mail_expires_days,
     case when p_kind = 'keyword' then p_max_uses else null end,
     coalesce(p_is_active, true), p_created_by)
  returning id into v_id;

  if p_kind = 'keyword' then
    v_code := upper(btrim(coalesce(p_code, '')));
    if v_code = '' then
      raise exception 'coupon_code_required';
    end if;
    if v_code !~ '^[A-Z0-9_-]+$' then
      raise exception 'invalid_coupon_code: %', v_code;
    end if;
    if exists (select 1 from public.coupon_codes where code = v_code) then
      raise exception 'coupon_code_taken: %', v_code;
    end if;

    insert into public.coupon_codes (code, coupon_id) values (v_code, v_id);
    return jsonb_build_object('coupon_id', v_id, 'code', v_code, 'issued', 1);
  end if;

  -- normal — 접두사 + 랜덤
  if coalesce(p_quantity, 0) <= 0 then
    raise exception 'invalid_quantity';
  end if;
  if coalesce(p_random_len, 0) <= 0 then
    raise exception 'invalid_random_len';
  end if;

  v_prefix := upper(btrim(coalesce(p_prefix, '')));
  if v_prefix <> '' and v_prefix !~ '^[A-Z0-9_-]+$' then
    raise exception 'invalid_coupon_prefix: %', v_prefix;
  end if;

  -- 충돌로 무한 반복하지 않도록 시도 횟수를 제한한다.
  v_max_try := greatest(p_quantity * 20, 1000);

  while v_made < p_quantity and v_attempts < v_max_try loop
    v_code := v_prefix;
    for i in 1..p_random_len loop
      v_code := v_code || substr(v_charset, 1 + floor(random() * length(v_charset))::int, 1);
    end loop;

    insert into public.coupon_codes (code, coupon_id)
    values (v_code, v_id)
    on conflict (code) do nothing;

    if found then
      v_made := v_made + 1;
    end if;
    v_attempts := v_attempts + 1;
  end loop;

  if v_made < p_quantity then
    raise exception 'coupon_code_space_exhausted: made % of %', v_made, p_quantity;
  end if;

  return jsonb_build_object('coupon_id', v_id, 'issued', v_made);
end;
$$;

comment on function public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text) is
  '쿠폰 생성(어드민). normal 은 접두사+랜덤으로 수량만큼, keyword 는 지정 코드 1개. items 는 game_items 검증.';

revoke all on function public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_create(text,text,text,text,jsonb,jsonb,timestamptz,int,boolean,text,int,text,int,int,text) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_update — 쿠폰 수정. 코드는 바꾸지 않는다(이미 배포됐을 수 있음).
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_coupon_update(uuid,text,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean);
drop function if exists public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean);

create or replace function public.ts_admin_coupon_update(
  p_id                uuid,
  p_title             text,
  p_content           text    default '',
  p_category          text    default 'default',
  p_items             jsonb   default null,
  p_localized         jsonb   default null,
  p_expires_at        timestamptz default null,
  p_mail_expires_days int     default 7,
  p_max_uses          int     default null,
  p_is_active         boolean default true
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_kind text;
  v_key  text;
  v_cnt  text;
begin
  select kind into v_kind from public.coupons where id = p_id;
  if not found then
    raise exception 'coupon_not_found';
  end if;
  if coalesce(btrim(p_title), '') = '' then
    raise exception 'title_empty';
  end if;
  if coalesce(p_mail_expires_days, 0) <= 0 then
    raise exception 'invalid_mail_expires_days';
  end if;

  if p_items is not null then
    if jsonb_typeof(p_items) <> 'array' then
      raise exception 'items_not_array';
    end if;
    for v_key, v_cnt in
      select e->>'key', e->>'count' from jsonb_array_elements(p_items) e
    loop
      if coalesce(btrim(v_key), '') = '' then
        raise exception 'item_key_empty';
      end if;
      if v_cnt !~ '^[0-9]+$' or v_cnt::bigint <= 0 then
        raise exception 'item_count_invalid: %', v_key;
      end if;
      if not exists (select 1 from public.game_items g where g.key = v_key) then
        raise exception 'unknown_item_key: %', v_key;
      end if;
    end loop;
  end if;

  update public.coupons
     set title             = btrim(p_title),
         content           = coalesce(p_content, ''),
         category          = coalesce(nullif(btrim(p_category), ''), 'default'),
         items             = p_items,
         localized         = p_localized,
         expires_at        = p_expires_at,
         mail_expires_days = p_mail_expires_days,
         max_uses          = case when v_kind = 'keyword' then p_max_uses else null end,
         is_active         = coalesce(p_is_active, true),
         updated_at        = now()
   where id = p_id;
end;
$$;

comment on function public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean) is
  '쿠폰 수정(어드민). 발급된 코드는 건드리지 않는다.';

revoke all on function public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_update(uuid,text,text,text,jsonb,jsonb,timestamptz,int,int,boolean) to service_role;

-- ---------------------------------------------------------------------------
-- ts_admin_coupon_delete — 쿠폰 삭제. 코드·이력도 함께 사라진다(cascade).
-- ---------------------------------------------------------------------------
drop function if exists public.ts_admin_coupon_delete(uuid);

create or replace function public.ts_admin_coupon_delete(p_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  delete from public.coupons where id = p_id;
  if not found then
    raise exception 'coupon_not_found';
  end if;
end;
$$;

comment on function public.ts_admin_coupon_delete(uuid) is
  '쿠폰 삭제(어드민). coupon_codes·coupon_redemptions 도 cascade 로 함께 삭제된다.';

revoke all on function public.ts_admin_coupon_delete(uuid) from public, anon, authenticated;
grant execute on function public.ts_admin_coupon_delete(uuid) to service_role;


notify pgrst, 'reload schema';
