-- 14_purchases.sql
-- 인앱 구매 영수증 검증 기록 테이블 (Google Play + Apple App Store)
-- 동일 purchase_token 이중 처리를 DB UNIQUE 제약으로 차단합니다.

create table if not exists purchases (
  id              bigint generated always as identity primary key,
  account_id      uuid references auth.users(id) on delete set null,
  user_id         text,
  product_id      text not null,
  purchase_token  text not null unique,   -- Google: purchaseToken / Apple: transaction_id
  order_id        text,                   -- Google: orderId / Apple: transaction_id
  package_name    text not null,          -- Google: packageName / Apple: bundleId
  purchase_state  int,                    -- 0=purchased, 1=cancelled(Google), 2=pending(Google)
  store           text not null default 'google_play',  -- 'google_play' | 'apple_app_store'
  verified_at     timestamptz not null default now()
);

-- 이미 테이블이 존재하는 경우 store 컬럼만 추가
alter table purchases
  add column if not exists store text not null default 'google_play';

alter table purchases enable row level security;

create policy "users_read_own_purchases"
  on purchases for select
  using (account_id = auth.uid());

create policy "users_insert_own_purchases"
  on purchases for insert
  with check (account_id = auth.uid());
