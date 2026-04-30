-- 14_purchases.sql
-- 구글 플레이 인앱 구매 영수증 검증 기록 테이블
-- 동일 purchase_token 이중 처리를 DB UNIQUE 제약으로 차단합니다.

create table if not exists purchases (
  id              bigint generated always as identity primary key,
  account_id      uuid references auth.users(id) on delete set null,
  user_id         text,
  product_id      text not null,
  purchase_token  text not null unique,
  order_id        text,
  package_name    text not null,
  purchase_state  int,    -- 0=purchased, 1=cancelled, 2=pending
  verified_at     timestamptz not null default now()
);

alter table purchases enable row level security;

create policy "users_read_own_purchases"
  on purchases for select
  using (account_id = auth.uid());
