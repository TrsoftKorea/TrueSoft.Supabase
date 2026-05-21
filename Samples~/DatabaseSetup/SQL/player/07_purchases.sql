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
  purchase_state  int,                          -- 0=purchased, 1=cancelled(Google), 2=pending(Google)
  store           text        not null default 'google_play',  -- 'google_play' | 'apple_app_store'
  verified_at     timestamptz not null default now()
);

-- 기존 테이블이 있는 경우 store 컬럼만 추가 (기존 데이터 영향 없음)
alter table public.purchases
  add column if not exists store text not null default 'google_play';

alter table public.purchases enable row level security;

-- 본인 구매 내역 조회만 허용 (INSERT는 Edge Function이 service_role로 처리)
drop policy if exists "users_read_own_purchases" on public.purchases;
create policy "users_read_own_purchases"
  on public.purchases for select
  using (account_id = auth.uid());
