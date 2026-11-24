# 우편함

서버가 플레이어에게 아이템·메시지를 전달하는 시스템 우편함입니다. 발송은 운영 콘솔에서만 하고, 클라이언트는 조회·수령·삭제만 합니다.

## 분류 {#category}

우편은 분류를 뜻하는 `category` 값으로 나뉩니다. 기본값은 `default`이며, 클라이언트는 발송자가 지정한 문자열을 그대로 조회 키로 씁니다. 공지·이벤트·보상처럼 소수의 고정된 값으로 운영하는 것을 권장합니다.

목록·전체 수령·수령한 우편 일괄 삭제·카운트 조회 메서드는 모두 `category` 파라미터를 받습니다. 생략하거나 `null`을 넘기면 전체 분류를 대상으로 동작합니다.

::: warning 정확히 일치하는 비교
분류는 대소문자를 구분하는 정확 일치 비교입니다. `Event`와 `event`는 서로 다른 분류로 취급되니, 발송·조회에서 같은 표기를 쓰세요.
:::

## 서버가 지급하는 보상 {#server-grant}

보상 키가 아래 꼴이면 서버가 수령과 함께 [유저 데이터](/guide/user-data/)에 더하고, 게임 안의 값에도 바로 반영됩니다. 받음 표시와 지급이 한 처리라 도중에 앱이 꺼져도 보상이 사라지거나 두 번 지급되지 않습니다.

| 보상 키 | 서버가 더하는 곳 |
|---------|------------------|
| `gold` | 숫자 데이터 필드 `gold` |
| `gems.red` | 딕셔너리 데이터 필드 `gems`의 키 `red`. 키가 없으면 새로 만듭니다 |
| `slots[2]` | 리스트 데이터 필드 `slots`의 세 번째 칸. 번호는 0부터 셉니다 |

수량은 정수이고, 더하는 곳은 `int`·`long`·`short`·`float`·`double`·`decimal` 숫자여야 합니다. 리스트·딕셔너리는 게임의 유저 데이터 클래스에서 원소가 이 숫자인 필드만 되고, 딕셔너리 키는 글자·정수·열거형이어야 합니다. 열거형 키는 이름을 대소문자까지 같게 씁니다. 그 밖의 원소나 리스트에 없는 칸을 가리키면 그 우편은 수령되지 않고 `SupabaseReason.MailItemHandlerMissing`으로 실패하며, [전체 수령](/guide/mailbox/claim-all)은 그 우편만 남기고 나머지를 받습니다. 더하기만으로 안 되는 보상만 [아이템 핸들러](/guide/mailbox/item-handler)를 등록하세요.

데이터 필드가 이미 나간 게임 빌드에 들어 있으면, 운영 콘솔 **아이템 카탈로그**에 새 아이템을 등록해 앱 업데이트 없이 보낼 수 있습니다. 카탈로그는 리스트·딕셔너리 원소를 받을 수 있는지 운영 콘솔 **데이터 필드**에서 정한 [타입](/guide/user-data/class-gen#collection-type)으로 판정합니다. 리스트·딕셔너리 원소는 그 빌드가 SDK 0.4.0 이상일 때만 됩니다. 리스트 원소는 플레이어 데이터의 리스트에 그 칸이 있어야 받습니다.

::: warning 리스트·딕셔너리 원소는 SDK 0.4.0부터
SDK 0.2.1~0.3.4로 만든 빌드는 원소 꼴 키가 든 우편을 받지 못하고 `SupabaseReason.MailItemHandlerMissing`으로 실패합니다. 우편은 남아 있어 새 빌드에서 받을 수 있습니다. 그보다 앞선 판은 처리기가 없는 보상을 지급 없이 받음 처리합니다.
:::

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`GetMailsAsync`](/guide/mailbox/list) | 우편함 목록 조회, 분류 필터 |
| [`GetMailAsync`](/guide/mailbox/detail) | 우편 상세 조회 |
| [`ClaimMailItemsAsync`](/guide/mailbox/claim) | 우편 1건 보상 수령 |
| [`ClaimAllMailItemsAsync`](/guide/mailbox/claim-all) | 보상 일괄 수령, 분류 필터 |
| [`DeleteMailAsync`](/guide/mailbox/delete) | 우편 1건 삭제 |
| [`DeleteClaimedMailsAsync`](/guide/mailbox/delete-claimed) | 수령한 우편 일괄 삭제, 분류 필터 |
| [`GetUnclaimedMailCountAsync`](/guide/mailbox/counts-unclaimed) | 미수령 우편 수 |
| [`GetMailInboxCountsAsync`](/guide/mailbox/counts-detail) | 미수령 수 + 분류별 세부 내역 |
| [`GetMailSettingsAsync`](/guide/mailbox/settings) | 만료 우편 보관 일수 |
| [`RegisterMailItemHandlers`](/guide/mailbox/item-handler) | 보상 지급 함수 등록 |
| [`SendMailToSelfAsync`](/guide/mailbox/send-to-self) | 본인에게 우편 보내기 |
