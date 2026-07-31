---
name: ""
description: "클라 우편함은 기존 완비, 어드민 발송만 신규(12_admin_mail.sql + Retool). Retool 리소스=postgres role."
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
---

게임 우편함(mailbox). **클라이언트 계층(조회·수령·삭제·만료 자동삭제)은 원래부터 완비**: `06_mails.sql` 테이블+RPC(`ts_view_mail_for_user`·`ts_claim_mail_items`·`ts_claim_all_mail_items`·`ts_delete_mail_for_user`·`ts_delete_read_mails_for_user`·`ts_mail_inbox_counts`·`ts_cleanup_expired_mails`+cron 03:00) + SDK `SupabaseSDK` API + 게임의 `IMailItemHandler`(key별 지급). mails.items = jsonb `[{key,count}]`. 클라 발송은 차단(모든 쓰기 SECURITY DEFINER RPC).

**신규(2026-07-09): 어드민 발송만 추가** — `Samples~/DatabaseSetup/SQL/player/12_admin_mail.sql`(양 프로젝트 적용):
- `game_items`(아이템 카탈로그, key PK), `mail_batches`(발송 캠페인 그룹, recipient_count 스냅샷), `mails.batch_id`(on delete set null → cron 하드삭제돼도 이력 보존).
- `ts_admin_send_mail(p_target_mode, p_title, p_expires_at, p_account_ids jsonb, p_server_id, p_content, p_sender_name, p_items jsonb, p_created_by, p_skip_item_validation)` → `{batch_id,recipient_count}`. 대상 all/server/players(**account_id jsonb 배열**, uuid[] 바인딩 불확실해서 jsonb 선택). 탈퇴(withdrawn_at) 제외. items는 game_items 검증(우회 플래그). 검증은 batch INSERT 전에 수행(orphan batch 없음).
- `ts_admin_upsert_game_item`·`ts_admin_delete_game_item`·`ts_admin_count_recipients`. 어드민 RPC 4개 EXECUTE = **service_role 전용**(authenticated/anon revoke).

**Retool**(방식 B 앱 `b518a11a-…`, 게시 commit 684ede89): `/backend/mails/`(sendMail·getServers·listGameItems·upsert/deleteGameItem·getMailBatches·getMailBatchDetail), `/frontend/pages/Mails.tsx`(발송 폼 3모드+플레이어 다중선택+아이템 피커+이력)·`GameItems.tsx`(카탈로그 CRUD), App.tsx 라우트·Layout.tsx NAV. 훅은 폴더별 자동생성 → **`../hooks/backend/mails`**(파일별 아님).

**핵심: Retool DB 리소스는 postgres(owner)로 접속** → grant·RLS를 우회. 그래서 DevilSlayer가 service_role/authenticated SELECT 없이도 조회 동작. admin RPC(service_role grant)도 owner라 실행 가능. **service_role 테이블 grant는 Retool 기능과 무관** → 프로젝트 동일화 위해 mail 테이블 3개의 service_role DML을 revoke함. 이번엔 Retool 페이지(Mails·GameItems)도 만들어 게시했으나, RPC는 Retool 종속성 없는 범용 함수라 어느 클라이언트에서도 호출 가능. 관련: [[feedback_projects_identical_structure]] [[project_retool_project_switching_methodB]]
