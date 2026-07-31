# Memory Index

작업 방식·코드 스타일처럼 **모든 프로젝트에 적용되는 규칙은 전역 `~/.claude/CLAUDE.md`**에 있다. 여기에는 이 프로젝트에서만 참인 것만 둔다.

## 작업 방식

- [게임 반영은 요청 시에만](feedback_game_changes_on_request_only.md) — 게임 프로젝트는 사용자가 직접. 평소엔 변경점 안내만.
- [SQL 변경 시 DB 직접 적용](feedback_sql_apply.md) — SQL 파일 수정 시 Supabase MCP로 DB에도 적용.
- [모든 프로젝트 내부 구조 동일](feedback_projects_identical_structure.md) — 프로젝트별 분기 SQL 금지. 캐노니컬 SQL로 수렴.

## 코드 전달·파일 정리

- [코드 전달 체크리스트](feedback_code_delivery.md) — Retool 전달 방식. 신규/삭제=스레드, 게시는 사용자.
- [파일 삭제 규칙](feedback_file_delete.md) — Runtime 삭제 전 게임 참조 확인, Samples~는 SDK 소스에서, 이전·삭제 안내 문구 금지.
- [Changelog 불필요](feedback_no_changelog.md) — SDK 미완성이므로 changelog 생성 금지.

## 문서·네이밍

- [문서 작성 스타일·구조 규칙](feedback_doc_style.md) — 헤딩 괄호·혼용 금지, 기능별 페이지 분리, 코드 우선 구조 등.
- [SDK 이름 간결화](feedback_sdk_naming_datetimeoffset.md) — 공개 식별자에서 중복 수식어 제거. DateTimeOffset 규칙은 SdkAudit R11이 검사.
- [Retool UI 문구 평이하게](feedback_retool_ui_copy.md) — 내부 SDK 용어 금지. 처음 보는 운영자 기준.

## Retool 운영

- [스레드/게시 위생](project_retool_thread_publish_hygiene.md) — 오래된 main 기준 스레드를 게시하면 다른 작업이 되돌아감. 신규 파일 먼저 게시.
- [게시 1회당 대화 1개](project_retool_publish_needs_new_thread.md) — 게시된 대화에서 수정하면 Publish가 잠김. 후속 수정은 새 대화로.
- [라이브 프로젝트 전환(방식 B)](project_retool_project_switching_methodB.md) — `backend/lib/resources.ts` 레지스트리 + `getTarget()`.
- [리소스 바인딩/데슬 가드](project_retool_resource_bindings.md) — 데슬은 `supabaseDevilslayer` 필수. 데슬 DDL은 RPC 패턴으로.
- [환경 전환 제약 + pooler 함정](project_retool_env_switching.md) — React 앱은 `?_environment` 불가. pooler는 Username 접미사로 프로젝트 구분.
- [pg 래퍼는 $N을 등장 순서로 바인딩](project_retool_pg_param_appearance_order.md) — placeholder 번호를 SQL 등장 순서와 일치시킬 것.
- [운영자 스키마 변경 버전관리](project_operator_schema_versioning.md) — 스테이징→게시→롤백. 화면은 라이브만, 대기 변경은 PendingChanges 패널로.
- [페이지 헤더 통일](project_retool_page_header.md) — 제목·부제목은 PageHeader 컴포넌트로.
- [표 로딩 UI 통일 진행상황](project_retool_table_status_row_rollout.md) — TableStatusRow 롤아웃. RemoteConfig·ColumnManagementTab 미적용 확인(2026-07-27).

## SDK 설계 결정

- [SDK 정기 점검 진행 상태](project_sdk_audit_progress.md) — 끝난 축·남은 축·결정 대기 항목. 세션이 끊기면 여기서 이어감.
- [SupabaseResult Reason + ErrorCode](project_supabaseresult_reason_failcode.md) — Reason=enum 분기용, ErrorCode=원문 문자열. 상수·enum·map 3자 동기화.
- [신규 유저 초기화 + 로드 전 fallback](project_staticusersave_onfirstload.md) — IsNewUser 플래그 + 컬렉션 fallback 병합(SQL NULL만).
- [내 프로필은 로그인 result로만](project_signin_result_profile.md) — MyProfile 제거. 로그인 파사드가 SupabaseSignInResult 반환.
- [2D 컬렉션 지연 프록시](project_autolist2d_row_design.md) — grid[i]가 RowRef 프록시 반환. class 프록시 필수(struct는 CS1612).
- [DataColumn 단일 소스](project_datacolumn_single_source.md) — DataColumnAttribute/DataSavePriority는 Core에만. Unity 미러 재추가 금지.
- [RemoteConfig 타입 8종 한정](project_remoteconfig_type_set.md) — string·int·long·bool·float·double·DateTime·json. 늘리지 말 것.
- [IAP는 v4+v5 모두 지원](project_iap_v4_v5_support.md) — Unity IAP 4.x와 5.2.1+ 둘 다. 5.0~5.2.0 미지원. v4 엔진 삭제 금지.
- [PlayNANOO 로그인 병렬화](project_playnanoo_parallel_login.md) — 로그인 3경로만 Task.WhenAll. 링크는 순차 유지.
- [본인 세이브 삭제](project_user_data_delete.md) — PlayerSave.DeleteAsync(). 로컬 리셋 먼저 → 서버 DELETE(안 그러면 자동저장이 되살림).
- [DefenceR는 SDK를 GitHub UPM으로 참조](project_defencer_consumes_sdk_via_github.md) — 로컬 수정은 커밋+푸시해야 반영됨.
- [SupabaseSettings "No script asset"](project_supabasesettings_noscript_libraryfix.md) — 컴파일 에러 없으면 Library 캐시 문제. Library 삭제·재실행.

## 우편함·DB·인증

- [우편함 어드민 발송 구조](project_mailbox_admin_send.md) — 어드민 발송만 신규(12_admin_mail.sql). Retool 리소스=postgres role.
- [우편함 분류(category)](project_mailbox_category.md) — mails.category 컬럼 파티션. RPC 3개 p_category. 양 DB 적용됨.
- [우편함 관리 UI](project_mailbox_admin_ui.md) — 개별 내역·예약/반복 발송 게시됨. 발송 내역(MailBatches)은 폐기.
- [다국어 우편 메시지](project_mail_localized.md) — mails.localized jsonb + TitleFor/ContentFor. Retool 발송폼 통합.
- [우편 상태 단일 축(수령/미수령)](project_mail_single_axis_claimed.md) — is_read 폐지, 열람=수령. 재도입 금지.
- [닉네임 3원 통일](project_nickname_unification.md) — display_names·user_metadata·Retool 일치. 닉네임 유니크=전역.
- [클라이언트 테이블 권한 최소화](project_client_grants_hardening.md) — anon·authenticated ALL 제거. 테이블 추가 후 15번 SQL 재실행.
- [Apple Services ID 네이밍](project_apple_services_id_naming.md) — `번들ID.Services`. Client IDs는 Services ID를 맨 앞에.
