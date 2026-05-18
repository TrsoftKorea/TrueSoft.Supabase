-- 우편함 테이블에 대한 클라이언트 직접 쓰기 권한을 제거합니다.
-- 적용 후 모든 우편 조작은 RPC를 통해서만 가능합니다.
-- 선택 적용: 보안 요구 수준에 따라 10_mails.sql 이후에 실행하세요.
--
-- =============================================================================
-- 우편함 RLS 강화 — 클라이언트(authenticated) 직접 INSERT/UPDATE/DELETE 차단
-- =============================================================================
-- 선행: Sql/player/10_mails.sql (RPC·RLS) 반영 완료
--
-- 목적
--   PostgREST로 mails에 PATCH/DELETE/INSERT 하면 RLS만으로는 컬럼 단위 제한이 어렵습니다.
--   우편 변경은 RPC(ts_view_mail_for_user, ts_claim_*, ts_delete_*)만 쓰도록 좁힙니다.
--
-- 적용 전 확인
--   • 시스템 메일 발송이 service_role / SQL Editor / Edge만인지 (authenticated로 INSERT 하면 깨짐)
--   • 마이그레이션·배치가 mails에 직접 쓰는지
--   • anon 이 mails 를 조회해야 하는지 (대부분 불필요)
--
-- 롤백 예시
--   grant insert, update, delete on table public.mails to authenticated;
--   (필요 시 mails_update_own 정책을 11_mails.sql 에서 다시 생성)
-- =============================================================================

-- 직접 UPDATE 경로 제거 (읽음·수령·삭제는 전부 SECURITY DEFINER RPC)
drop policy if exists "mails_update_own" on public.mails;

revoke insert, update, delete on table public.mails from authenticated;

-- anon 은 보통 mails 미사용. 목록 REST가 anon 이면 아래 한 줄 대신 grant select 만 조정.
revoke all on table public.mails from anon;

grant select on table public.mails to authenticated;
