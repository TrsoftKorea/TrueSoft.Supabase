---
name: project_mailbox_category
description: "우편함 분류(category) 기능 — mails.category 컬럼 파티션 방식, PlayNANOO tableCode 대응"
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
---

우편함(mails)에 분류 기능을 추가했다(2026-07-13). PlayNANOO Inbox의 `tableCode`에 대응하는 개념으로, **물리적 별도 테이블이 아니라 `mails.category text not null default 'default'` 컬럼**으로 파티션한다. 자유 텍스트, 카탈로그 테이블 없음(대소문자 구분 정확 일치). [[project_mailbox_admin_send]]의 후속.

**구조:**
- DB: `mails.category` + `mail_batches.category` 컬럼, `mails_account_id_category_created_idx` 인덱스. RPC 3개가 `p_category` 파라미터 받음 — `ts_claim_all_mail_items(p_category)`·`ts_delete_read_mails_for_user(p_category)`(둘 다 오버로드 변경이라 drop 후 재생성)·`ts_admin_send_mail(..., p_category)`(인자 맨 뒤 추가). `ts_mail_inbox_counts()`는 `language sql`→`plpgsql`로 바꿔 `{unread, unclaimed_mails, by_category:{"<cat>":{unread,unclaimed_mails}}}` 반환(활성 메일 있는 분류만 키). null 넘기면 전체 분류(하위호환).
- C#: `Mail.Category`, 신규 `MailCategoryCounts`, `MailInboxCounts.ByCategory`. `GetMailsAsync`/`ClaimAllMailItemsRpcAsync`/`DeleteReadMailsForUserRpcAsync`에 `category` 파라미터. 4계층(Service→Facade→SDK Try*→Supabase 파사드) 모두 threading. 신규 `Supabase.GetMailInboxCountsAsync()`.
- 파일: `06_mails.sql`·`12_admin_mail.sql`(SDK 샘플), Runtime/Core/Data/{SupabaseMailboxService,SupabaseMailModels}.cs, Runtime/Unity/{MailboxFacade,SupabaseSDK,Supabase}.cs. 문서 `docs~/guide/mailbox/`(9 메서드 페이지+item-handler+index), `api/mailbox.md`. 샘플 `SampleMailbox.cs`(키 1~8). Retool sendMail.ts·getMailBatches.ts·Mails.tsx.

**적용 상태:** DB는 ProjectR·DevilSlayer 양쪽 마이그레이션 적용+검증 완료(세 RPC 오버로드 1개씩). SDK SQL 파일도 수정 완료(커밋은 사용자). Retool 3파일은 채팅으로 전체코드 제공, 사용자가 붙여넣기+게시 예정.

**배포 순서 주의:** DB 먼저, SDK 나중. 신버전 SDK가 구버전 스키마 만나면 깨짐(select에 없는 category, RPC 옛 0-인자 시그니처). 구버전 SDK는 신 스키마에서 정상.

**미검증:** Unity 컴파일(자동 러너 없음, brace/paren 균형만 확인). Play Mode는 `ts_admin_send_mail`이 service_role 전용이라 클라 자가 테스트 불가 — Retool/SQL Editor로 자기 계정에 발송 후 SampleMailbox 키로 확인 필요.

**분류 사전 등록(2026-07-14):** 자유 텍스트 대신 미리 등록한 분류만 발송 폼 드롭다운에서 선택. 신규 테이블 `mail_categories(key pk, display_name, sort_order, created_at)` — 양 프로젝트 적용, `default`("기본") 시드, RLS on+anon/authenticated revoke(Retool postgres 역할만). SQL 소스 `14_mail_categories.sql`, 99_verify에 추가. 서버 검증은 안 둠(UI 강제). Retool: 백엔드 3개(getMailCategories/upsertMailCategory/deleteMailCategory, 스레드 `mail-categories`로 생성→훅 자동생성) + 전용 관리 페이지 `MailCategories.tsx`(GameItems 패턴, default 삭제 금지) + App.tsx 라우트 `/mail-categories` + Layout 우편함 그룹에 "우편 분류" + Mails.tsx 분류 input→드롭다운. 게시 순서: 스레드 먼저→App/Layout/Mails 나중.
