# 우편함 API

| 메서드 | 설명 |
|--------|------|
| [`GetMyMailsAsync`](/guide/mailbox/list) | 목록 조회(분류 필터) |
| [`GetMailDetailAsync`](/guide/mailbox/detail) | 상세 조회 |
| [`ClaimMailItemsAsync`](/guide/mailbox/claim) | 보상 수령 |
| [`ClaimAllMailItemsAsync`](/guide/mailbox/claim-all) | 보상 일괄 수령(분류 필터) |
| [`DeleteMailAsync`](/guide/mailbox/delete) | 삭제 |
| [`DeleteReadMailsAsync`](/guide/mailbox/delete-read) | 읽은 우편 일괄 삭제(분류 필터) |
| [`GetUnreadMailCountAsync`](/guide/mailbox/counts-unread) | 미읽음 수 |
| [`GetUnclaimedItemMailCountAsync`](/guide/mailbox/counts-unclaimed) | 미수령 보상 메일 수 |
| [`GetMailInboxCountsAsync`](/guide/mailbox/counts-detail) | 미읽음·미수령 + 분류별 세부 내역 |
| [`RegisterMailItemHandler`](/guide/mailbox/item-handler) | 보상 지급 핸들러 등록 |

언어별 제목·본문은 [다국어 메시지](/guide/mailbox/localized)의 `Mail.TitleFor`·`ContentFor`로 읽습니다.

::: tip
보상이 첨부된 우편을 수령하려면 [아이템 핸들러 등록](/guide/mailbox/item-handler)이 필요합니다.
:::
