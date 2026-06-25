# Unity IAP 버전별 차이 {#iap-versions}

SDK는 Unity IAP **v4와 v5를 모두 지원**합니다. 게임 코드는 동일하며, 내부 영수증 처리 방식만 다릅니다.

필요 버전: `com.unity.purchasing` **4.x** 또는 **5.2.1 이상**. v5 엔진이 5.2.1의 API를 요구하므로 **5.0–5.2.0은 지원되지 않습니다**.

| 항목 | v4 (4.x) | v5 (5.2.1+) |
|------|----------|-------------|
| iOS 영수증 형식 | SK1 (base64 receipt) | SK2 JWS (iOS 15+) / SK1 폴백 (iOS 14 이하 또는 `forceStoreKit1`) |
| iOS 서버 검증 함수 | `purchase-verify-apple-legacy` | `purchase-verify-apple` (SK2) + `purchase-verify-apple-legacy` (SK1 폴백) |
| iOS 가격 자동 추출 | ✗ | ✔ (SK2 경로만 해당) |
| PlayNanoo IAP 연동 | ✔ | ✔ (`forceStoreKit1` 자동 설정) |

::: warning 지원되지 않는 버전
`com.unity.purchasing` **5.0.0 ~ 5.2.0**은 v5 엔진 API가 없어 SDK IAP가 동작하지 않습니다. **4.x** 또는 **5.2.1 이상**을 사용하세요.  
PlayNanoo와 함께 쓴다면 SK1 강제가 필요한데, 5.2.1 이상은 `forceStoreKit1`을 지원합니다.
:::
