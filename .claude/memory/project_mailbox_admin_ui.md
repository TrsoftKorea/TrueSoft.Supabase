---
name: project_mailbox_admin_ui
description: 우편함 관리 UI 3기능(개별 내역·발송 내역·예약/반복 발송) — Retool + DB 예약 서브시스템
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
  modified: 2026-07-27T09:01:57.633Z
---

PlayNANOO 콘솔 참고로 트루베이스 Retool 어드민에 우편함 관리 3기능 추가(2026-07-13). [[project_mailbox_category]]·[[project_mailbox_admin_send]]의 후속.

**① 개별 우편 내역** — `mails` 테이블을 플레이어별로 조회(상태·플레이어·분류·날짜 필터). getMailRecords.ts + MailRecords.tsx (`/mail-records`). DB 추가 없음(RLS 우회 postgres 리소스).
**② 운영자 발송 내역** — 기존 Mails.tsx 하단 발송 이력을 별도 페이지 MailBatches.tsx(`/mail-batches`)로 분리(운영자·페이지네이션·검색). Mails.tsx는 발송 폼만 남김.
**③ 예약·반복 발송** — 신규 서브시스템. DB `13_mail_schedules.sql`: mail_schedules 테이블 + ts_run_due_mail_schedules(러너, cron 매분) + ts_mail_schedule_next_run(다음 시각 계산, Asia/Seoul). scheduled=1회 소진, repeat=매일 지정 시각. 러너가 ts_admin_send_mail 호출. Retool: getMailSchedules/createMailSchedule/setMailScheduleActive/deleteMailSchedule + MailSchedules.tsx(`/mail-schedules`, 즉시/예약/반복 폼 + 목록 활성토글·삭제). 반복은 매일 시각만(주간/cron식은 범위 밖).

**적용 상태:** DB(13_mail_schedules + 99_verify에 mail_schedules·ts_run_due_mail_schedules·ts_mail_schedule_next_run 추가)는 ProjectR·DevilSlayer 양쪽 적용+검증 완료(러너 실발 테스트 통과 — 0명 대상으로 안전 확인 후 잔여물 정리). SDK SQL 파일 작성됨(커밋 사용자).

**Retool 적용 상태(2026-07-27 Layout.tsx 직접 확인):** ①개별 내역(`/mail-records`)·③예약(`/mail-schedules`)은 **게시 완료**, 네비 "우편함" 그룹에 우편 발송·우편 내역·예약 목록·우편 분류로 존재. **②발송 내역(MailBatches.tsx)은 폐기됨** — 파일이 0바이트로 남아 있고 라우트·네비에 없음(죽은 파일, 지워도 무방). 신규 파일은 스레드 도구로 생성, 게시는 사용자 직접([[feedback_code_delivery]] · [[project_retool_thread_publish_hygiene]]).

cron `run-mail-schedules` 매분 실행 중.
