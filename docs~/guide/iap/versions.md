# Unity IAP 버전별 차이 {#iap-versions}

SDK는 Unity IAP **v4와 v5를 모두 지원**합니다. 게임 코드는 동일하며, 내부 영수증 처리 방식만 다릅니다.

| 항목 | v4 (4.x) | v5 (5.x) |
|------|----------|----------|
| iOS 영수증 형식 | SK1 (base64 receipt blob) | SK2 JWS (iOS 15+) / SK1 폴백 (iOS 14 이하, 또는 `forceStoreKit1`) |
| iOS 서버 검증 함수 | `purchase-verify-apple-legacy` | `purchase-verify-apple` (SK2) + `purchase-verify-apple-legacy` (SK1 폴백) |
| iOS 가격 자동 추출 | ✗ | ✔ (SK2 경로만 해당) |
| PlayNanoo IAP 연동 | ✔ | ✔ (v5.1+ 에서 `forceStoreKit1` 자동 설정) |

::: warning Unity IAP 5.0.x + PlayNanoo
Unity IAP 5.0.x는 iOS 15 미만 기기에서 SK1을 강제할 수 없습니다.  
PlayNanoo IAP는 SK1만 지원하므로, PlayNanoo와 함께 사용한다면 **Unity IAP 5.1 이상**으로 업그레이드하세요.  
5.0.x에서 `PlayNanooRuntime`을 씬에 배치하면 콘솔에 오류가 출력됩니다.
:::
