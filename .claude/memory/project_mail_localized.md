---
name: project_mail_localized
description: 다국어 우편 메시지 — mails.localized jsonb + Mail.TitleFor/ContentFor + Retool 발송폼
metadata: 
  node_type: memory
  type: project
  originSessionId: d45aaca4-bd38-46df-bc94-8fc98446089c
---

우편 다국어 메시지 풀 지원 추가(2026-07-14). base title/content=기본 언어(fallback), 언어별 오버라이드는 `localized` jsonb. 언어 선택은 **디바이스 자동감지 아님** — 게임 개발자가 `mail.TitleFor("ja")`/`ContentFor("ja")`로 명시, 없으면 base 반환.

**DB(양 프로젝트 적용됨):** `mails`·`mail_batches`·`mail_schedules`에 `localized jsonb null`. `ts_admin_send_mail`에 `p_localized jsonb`(12번째, 맨 끝) 추가 → 시그니처 `(text,text,timestamptz,jsonb,uuid,text,text,jsonb,text,boolean,text,jsonb)`. 런너 `ts_run_due_mail_schedules`가 `s.localized`를 12번째 인자로 전달. SQL 소스: 12_admin_mail.sql·13_mail_schedules.sql.

**SDK:** `MailLocalizedText {Title,Content}` + `Mail.Localized`(IReadOnlyDictionary) + `TitleFor/ContentFor(lang)`. `MailSelectColumns`에 localized 추가, `MailRestRow.Localized`(Dictionary<string,MailLocalizedText>), MapRow. 문서 mailbox/list.md.

**Retool:** `/mails`(Mails.tsx)를 즉시·예약·반복 통합 폼으로 승격(예약 폼을 이식) + **CSV account_id 대량 발송**(UUID 파싱, 검색선택과 union) + **다국어 메시지 행**(언어 드롭다운 LANGS 13종). `/mail-schedules`(MailSchedules.tsx)는 목록 전용으로 축소. 백엔드 sendMail.ts(`$12::jsonb`)·createMailSchedule.ts(`$17::jsonb`, 등장순서 규칙 [[project_retool_pg_param_appearance_order]]).

**미완:** SDK 커밋·푸시+DefenceR 패키지 갱신은 사용자([[project_defencer_consumes_sdk_via_github]]). Retool 4파일(sendMail·createMailSchedule·Mails·MailSchedules 전체코드 채팅 전달)은 사용자가 붙여넣기+게시. 우편함 관리 UI 3기능 [[project_mailbox_admin_ui]], 분류 [[project_mailbox_category]].
