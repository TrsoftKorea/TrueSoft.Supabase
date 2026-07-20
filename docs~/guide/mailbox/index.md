# 우편함

서버가 플레이어에게 아이템·메시지를 전달하는 시스템 우편함입니다. 발송은 어드민(Retool)에서만 하고, 클라이언트는 조회·수령·삭제만 합니다.

## 분류 {#category}

메일은 `category`(분류) 값으로 나뉩니다. 기본값은 `default`이며, 클라이언트는 발송자가 지정한 문자열을 그대로 조회 키로 씁니다. 공지·이벤트·보상처럼 소수의 고정된 값으로 운영하는 것을 권장합니다.

목록·전체 수령·읽은 우편 일괄 삭제·카운트 조회 메서드는 모두 `category` 파라미터를 받습니다. 생략하거나 `null`을 넘기면 전체 분류를 대상으로 동작합니다.

::: warning 정확히 일치하는 비교
분류는 대소문자를 구분하는 정확 일치 비교입니다. `Event`와 `event`는 서로 다른 분류로 취급되니, 발송·조회에서 같은 표기를 쓰세요.
:::

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`GetMailsAsync`](/guide/mailbox/list) | 우편함 목록 조회(분류 필터) |
| [`GetMailAsync`](/guide/mailbox/detail) | 우편 상세 조회 |
| [`ClaimMailItemsAsync`](/guide/mailbox/claim) | 우편 1건 보상 수령 |
| [`ClaimAllMailItemsAsync`](/guide/mailbox/claim-all) | 보상 일괄 수령(분류 필터) |
| [`DeleteMailAsync`](/guide/mailbox/delete) | 우편 1건 삭제 |
| [`DeleteReadMailsAsync`](/guide/mailbox/delete-read) | 읽은 우편 일괄 삭제(분류 필터) |
| [`GetUnreadMailCountAsync`](/guide/mailbox/counts-unread) | 미읽음 수 |
| [`GetUnclaimedMailCountAsync`](/guide/mailbox/counts-unclaimed) | 미수령 보상 메일 수 |
| [`GetMailInboxCountsAsync`](/guide/mailbox/counts-detail) | 미읽음·미수령 수 + 분류별 세부 내역 |

::: tip
보상이 첨부된 우편을 수령하려면 먼저 [아이템 핸들러 등록](/guide/mailbox/item-handler)을 완료하세요.
:::
