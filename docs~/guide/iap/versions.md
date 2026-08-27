# Unity IAP 버전별 차이 {#iap-versions}

SDK는 Unity IAP **v4와 v5를 모두 지원**합니다 — `com.unity.purchasing` **4.0.0 이상**. 게임 코드는 동일하며, 내부 영수증 처리 방식만 다릅니다.

::: tip 권장 버전
새 프로젝트는 **5.1 이상의 최신 버전**을 권장합니다 — iOS SK1 강제를 포함한 모든 기능을 지원합니다. v4는 레거시 프로젝트 호환용입니다.
:::

| 항목 | v4 | v5 |
|------|----|----|
| iOS&nbsp;영수증&nbsp;형식 | SK1 base64 receipt | iOS 15+는 SK2 JWS, 이하는 SK1 폴백 |
| iOS&nbsp;서버&nbsp;검증&nbsp;함수 | `purchase-verify-apple-legacy` | SK2는 `purchase-verify-apple`, SK1은 `purchase-verify-apple-legacy` |
| iOS&nbsp;가격&nbsp;자동&nbsp;추출 | ✗ | SK2 경로만 ✔ |
| iOS&nbsp;SK1&nbsp;강제(`forceStoreKit1`) | 네이티브라 불필요 | **5.1 이상 필요** |

::: warning SK1이 필요하면 5.1 이상
v5에서 StoreKit 1을 강제해야 하는 경우 — iOS 14 이하 지원, 또는 SK1 영수증만 지원하는 PlayNANOO 영수증 검증을 쓰는 경우 — 은 `forceStoreKit1`이 필요하며, 이는 **Unity IAP 5.1 이상**입니다. **5.0.x는 SK1 강제가 불가**하므로 이 용도라면 **v4 또는 5.1 이상**을 사용하세요.
:::
