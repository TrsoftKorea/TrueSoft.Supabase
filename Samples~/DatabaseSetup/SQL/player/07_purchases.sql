-- 인앱 결제 영수증 서버 검증 결과를 기록합니다.
-- INSERT는 Edge Function(service_role)만 수행하며, 클라이언트는 본인 내역 조회만 가능합니다.
-- purchase_token UNIQUE 제약으로 동일 영수증의 이중 지급을 DB 수준에서 차단합니다.
--
-- =============================================================================
-- 인앱 구매 영수증 검증 기록 테이블 (Google Play + Apple App Store)
-- 선행: 없음 (auth.users 참조만 필요)
--
-- 동일 purchase_token 이중 처리를 DB UNIQUE 제약으로 차단합니다.
-- INSERT는 purchase-verify-google / purchase-verify-apple Edge Function만 수행합니다.
-- Edge Function은 service_role 키로 실행되므로 RLS를 우회합니다.
-- 클라이언트에는 SELECT(본인 내역)만 허용합니다.
-- =============================================================================

create table if not exists public.purchases (
  id              bigint generated always as identity primary key,
  account_id      uuid references auth.users(id) on delete set null,
  user_id         text,
  product_id      text        not null,
  purchase_token  text        not null unique,  -- Google: purchaseToken / Apple: transaction_id
  order_id        text,                         -- Google: orderId / Apple: transaction_id
  package_name    text        not null,         -- Google: packageName / Apple: bundleId
  store           text        not null default 'google_play',  -- 'google_play' | 'apple_app_store'
  verified_at     timestamptz not null default now()
);

-- 기존 테이블이 있는 경우 컬럼 추가 (기존 데이터 영향 없음)
alter table public.purchases
  add column if not exists store          text   not null default 'google_play',
  add column if not exists price_amount     bigint,     -- 결제 금액(micros = 주 단위 ×1,000,000, price_currency 기준). 내부용
  add column if not exists price_currency   text,       -- ISO 4217 통화 코드 (예: "KRW", "USD")
  add column if not exists price_amount_krw bigint;     -- KRW 환산 금액(원, 정수). 매출 확인용 — 구매 시점 환율 적용

-- 검증 함수는 완료된 구매만 기록하므로 상태 컬럼은 사용하지 않는다(기존 설치본 정리).
alter table public.purchases drop column if exists purchase_state;

alter table public.purchases enable row level security;

-- 본인 구매 내역 조회만 허용. INSERT는 purchase-verify Edge Function이 service_role로만 처리한다.
-- (유저 직접 INSERT 정책을 두지 않아 가짜 결제 기록·total_paid_krw 조작을 차단)
drop policy if exists "users_insert_own_purchases" on public.purchases;
drop policy if exists "users_read_own_purchases" on public.purchases;
create policy "users_read_own_purchases"
  on public.purchases for select
  using (account_id = auth.uid());

-- =============================================================================
-- 결제 완료 시 user_profiles.total_paid_krw 원자적 증분 트리거
-- 선행: 02_profiles.sql (user_profiles.total_paid_krw 컬럼)
-- =============================================================================

create or replace function public.ts_after_purchase_insert()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if new.price_amount_krw is not null and new.price_amount_krw > 0 then
    update public.user_profiles
       set total_paid_krw = total_paid_krw + new.price_amount_krw
     where account_id = new.account_id;
  end if;
  return new;
end;
$$;

comment on function public.ts_after_purchase_insert() is
  'purchases INSERT 후 user_profiles.total_paid_krw를 price_amount_krw만큼 증분. SECURITY DEFINER.';

drop trigger if exists tr_after_purchase_insert on public.purchases;
create trigger tr_after_purchase_insert
  after insert on public.purchases
  for each row execute function public.ts_after_purchase_insert();

-- 기존 결제 데이터 초기화 (마이그레이션 1회 실행, 이후 불필요)
-- UPDATE public.user_profiles p
--    SET total_paid_krw = COALESCE((
--      SELECT SUM(pu.price_amount_krw)
--      FROM public.purchases pu
--      WHERE pu.account_id = p.account_id
--        AND pu.price_amount_krw IS NOT NULL
--    ), 0);
