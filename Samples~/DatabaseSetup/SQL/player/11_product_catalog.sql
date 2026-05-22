-- 인앱 상품 카탈로그 테이블
-- Retool 등 관리 도구에서 상품 표시명·가격을 등록·수정합니다.
-- purchases 테이블과 product_id로 JOIN해 구매 내역에 상품명을 표시합니다.
-- 카탈로그에 없는 product_id로 구매 요청이 와도 purchases 기록은 정상 저장됩니다.
-- 추후 카탈로그에 등록하면 기존 구매 내역에도 자동으로 이름이 연결됩니다.
--
-- =============================================================================
-- 인앱 상품 카탈로그 (Retool 관리용)
-- 선행: 07_purchases.sql
-- =============================================================================

create table if not exists public.product_catalog (
  product_id    text        primary key,
  product_name  text        not null,             -- 상품 표시명 (예: "코인 100개")
  price_krw     int,                              -- 정가 (원화 정수, KRW)
  store         text        not null default 'all', -- 'all' | 'google_play' | 'apple_app_store'
  is_active     boolean     not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

-- 수정 시 updated_at 자동 갱신
create or replace function public.set_product_catalog_updated_at()
returns trigger language plpgsql as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists trg_product_catalog_updated_at on public.product_catalog;
create trigger trg_product_catalog_updated_at
  before update on public.product_catalog
  for each row execute function public.set_product_catalog_updated_at();

-- RLS: 클라이언트는 활성 상품 조회만 가능 / 관리자(service_role)는 모든 작업 가능
alter table public.product_catalog enable row level security;

drop policy if exists "clients_read_active_products" on public.product_catalog;
create policy "clients_read_active_products"
  on public.product_catalog for select
  using (is_active = true);

-- ============================================================
-- Retool에서 purchases + product_catalog JOIN 조회 예시:
--
-- SELECT
--   p.id,
--   p.account_id,
--   p.user_id,
--   p.product_id,
--   c.product_name,
--   p.price_amount,
--   c.price_krw,
--   p.store,
--   p.purchase_state,
--   p.verified_at
-- FROM purchases p
-- LEFT JOIN product_catalog c ON p.product_id = c.product_id
-- ORDER BY p.verified_at DESC;
-- ============================================================
