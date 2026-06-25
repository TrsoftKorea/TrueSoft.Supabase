# Unity IAP 버전별 차이 {#iap-versions}

SDK는 Unity IAP **v4와 v5를 모두 지원**합니다 — `com.unity.purchasing` **4.0.0 이상**. 게임 코드는 동일하며, 내부 영수증 처리 방식만 다릅니다.

| 항목 | v4 (4.x) | v5 (5.0+) |
|------|----------|-----------|
| iOS 영수증 형식 | SK1 (base64 receipt) | SK2 JWS (iOS 15+) / SK1 폴백 |
| iOS 서버 검증 함수 | `purchase-verify-apple-legacy` | `purchase-verify-apple` (SK2) + `purchase-verify-apple-legacy` (SK1) |
| iOS 가격 자동 추출 | ✗ | ✔ (SK2 경로만 해당) |
| iOS SK1 강제(`forceStoreKit1`) | 불필요(네이티브) | **5.1 이상 필요** |

::: warning SK1이 필요하면 5.1 이상
v5에서 StoreKit 1을 강제해야 하는 경우 — iOS 14 이하 지원, 또는 **PlayNANOO 영수증 검증**(SK1 영수증만 지원) — 은 `forceStoreKit1`이 필요하며, 이는 **Unity IAP 5.1 이상**입니다. **5.0.x는 SK1 강제가 불가**하므로 이 용도라면 **4.x 또는 5.1 이상**을 사용하세요.
:::
