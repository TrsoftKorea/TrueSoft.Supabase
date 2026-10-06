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
| iOS&nbsp;14&nbsp;이하·SK1&nbsp;강제 | StoreKit 1 base64 | `purchase-verify-apple-legacy` |

가격 정보는 StoreKit 2 경로에서만 자동으로 채워집니다.

두 함수 모두 영수증의 번들 ID를 [시크릿](/guide/start/database-setup#secrets) `APPLE_BUNDLE_ID`와 대조합니다. 비워 두면 iOS 결제가 전부 거부됩니다 — 애플 영수증은 어느 앱의 것이든 진짜로 검증되므로, 대조하지 않으면 다른 앱에서 산 영수증으로 이 게임의 상품을 받을 수 있습니다.

StoreKit 1 경로는 서버가 애플에 영수증을 확인할 때 [시크릿](/guide/start/database-setup#secrets) `APPLE_SHARED_SECRET`도 씁니다. App Store Connect의 **앱 정보 › 앱 전용 공유 암호** 값입니다. 없으면 결제는 끝나지만 검증이 `server_config_error`로 실패해 아이템이 지급되지 않고 대기로 남습니다. iOS 14 이하를 지원하거나, [플레이나누 병행](/guide/migration/iap)처럼 SK1을 강제하면 반드시 등록하세요.

::: warning SK1을 강제하려면 5.1 이상
iOS 14 이하를 지원하거나, SK1 영수증만 받는 [플레이나누 검증](/guide/migration/iap)을 쓰려면 `forceStoreKit1`이 필요하고 이는 **Unity IAP 5.1 이상**입니다. 5.0.x에서는 SK1을 강제할 수 없습니다.
:::
