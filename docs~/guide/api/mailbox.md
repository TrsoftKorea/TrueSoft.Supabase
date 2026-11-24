# 우편함 API

| 메서드 | 설명 |
|--------|------|
| [`GetMailsAsync`](/guide/mailbox/list) | 목록 조회 · 분류 필터 |
| [`GetMailAsync`](/guide/mailbox/detail) | 상세 조회 |
| [`ClaimMailItemsAsync`](/guide/mailbox/claim) | 보상 수령 |
| [`ClaimAllMailItemsAsync`](/guide/mailbox/claim-all) | 보상 일괄 수령 · 분류 필터 |
| [`DeleteMailAsync`](/guide/mailbox/delete) | 삭제 |
| [`DeleteClaimedMailsAsync`](/guide/mailbox/delete-claimed) | 수령한 우편 일괄 삭제 · 분류 필터 |
| [`GetUnclaimedMailCountAsync`](/guide/mailbox/counts-unclaimed) | 미수령 우편 수 |
| [`GetMailInboxCountsAsync`](/guide/mailbox/counts-detail) | 미수령 + 분류별 세부 내역 |
| [`GetMailSettingsAsync`](/guide/mailbox/settings) | 만료 우편 보관 일수 |
| [`RegisterMailItemHandlers`](/guide/mailbox/item-handler) | 보상 지급 함수 등록 |
| [`SendMailToSelfAsync`](/guide/mailbox/send-to-self) | 본인에게 우편 보내기 |

`Mail.Title`·`Content`는 기기 언어의 제목·본문입니다. 게임 안에서 고른 언어로 읽으려면 [다국어 메시지](/guide/mailbox/localized)를 참고하세요.

::: tip
보상 키가 숫자 데이터 필드 이름이거나 리스트·딕셔너리 데이터 필드의 원소를 가리키면 서버가 수령과 함께 지급합니다. 키 꼴은 [서버가 지급하는 보상](/guide/mailbox/#server-grant)을 참고하세요. 그 밖의 보상만 [아이템 핸들러](/guide/mailbox/item-handler)를 등록하세요.
:::
