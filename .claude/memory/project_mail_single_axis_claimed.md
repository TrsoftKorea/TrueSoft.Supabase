---
name: project_mail_single_axis_claimed
description: "우편 상태는 items_claimed_at 단일 축(수령/미수령). is_read 폐지, 텍스트=열람 시 수령."
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f192039-fca3-497f-bd1f-9da182660d0d
  modified: 2026-07-22T02:27:09.272Z
---

우편 상태를 **`items_claimed_at` 단일 축(수령/미수령)**으로 통일했다(2026-07-22). `is_read` 컬럼·개념은 완전 폐지 — 다시 도입하지 말 것.

- **미수령** = `items_claimed_at is null`, **수령** = not null, **삭제** = `deleted_at`.
- **텍스트 우편**(첨부 없음)은 `ts_view_mail_for_user` 열람 시 수령 처리. **보상 우편**은 수령 시에만.
- 배지는 **미수령 단일 집계**. `ts_mail_inbox_counts` → `{unclaimed, by_category:{unclaimed}}`.
- 리네임: SQL `ts_delete_read_mails_for_user`→`ts_delete_claimed_mails_for_user`; SDK `DeleteReadMailsAsync`→`DeleteClaimedMailsAsync`, `GetUnreadMailCountAsync` 제거(→`GetUnclaimedMailCountAsync` 단일), `Mail.IsRead`→`Mail.IsClaimed`, `MailInboxCounts.Unread/UnclaimedMails`→`.Unclaimed`, 로그태그 `MailboxDeleteReadBulk`→`MailboxDeleteClaimedBulk`(UnreadCount 제거).
- **양 프로젝트 DB 적용 완료**(마왕 wxivrmvtpufeczltward·데슬 owumqjyctqhuyailqutd): 함수 재정의 + is_read 백필(열람 텍스트→수령) 후 컬럼 드롭.
- Retool: `getMailRecords.ts` 상태 CASE(삭제/수령/미수령), `getMailBatchDetail.ts` read_count 제거, `MailRecords.tsx` statusOf·필터. [[project_mailbox_category]] · [[project_mail_localized]]
