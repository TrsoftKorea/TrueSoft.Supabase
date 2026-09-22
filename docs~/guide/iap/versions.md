# Unity IAP 버전 {#iap-versions}

SDK는 `com.unity.purchasing` **5.0.0 이상**이 필요합니다. 그보다 낮은 버전에서는 결제 기능만 통째로 빠지고 나머지 SDK는 그대로 동작합니다.

::: tip 권장 버전
**5.1 이상의 최신 버전**을 권장합니다 — iOS SK1 강제를 포함한 모든 기능을 지원합니다.
:::

## 왜 5.0 이상인가 {#why-v5}

구글 플레이가 2026년 8월 31일부터 신규 앱과 기존 앱 업데이트 모두에 **Play Billing Library 8 이상**을 요구하고, 유니티는 2026년 6월 8일자로 **Unity IAP 4 지원을 종료**했습니다. IAP 4는 앞으로 Billing Library 갱신을 받지 않습니다.

## iOS 결제 방식 {#ios}

| 상황 | 영수증 형식 | 서버 검증 함수 |
|------|-------------|----------------|
| iOS&nbsp;15&nbsp;이상 | StoreKit 2 JWS | `purchase-verify-apple` |
| iOS&nbsp;14&nbsp;이하 | StoreKit 1 base64 | `purchase-verify-apple-legacy` |

가격 정보는 StoreKit 2 경로에서만 자동으로 채워집니다.

::: warning SK1을 강제하려면 5.1 이상
iOS 14 이하를 지원하거나, SK1 영수증만 받는 [플레이나누 검증](/guide/migration/iap)을 쓰려면 `forceStoreKit1`이 필요하고 이는 **Unity IAP 5.1 이상**입니다. 5.0.x에서는 SK1을 강제할 수 없습니다.
:::
