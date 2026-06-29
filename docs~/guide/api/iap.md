# 인앱 결제 API

대부분의 게임은 **`CreateIAPAsync` 하나만** 쓰면 됩니다. Android/iOS를 자동 감지해 알맞은 결제·검증을 연결합니다.

| 메서드 | 설명 |
|--------|------|
| [`SupabaseIAP.CreateIAPAsync`](/guide/iap/usage) | 플랫폼 자동 감지 IAP 생성·초기화 (권장) |

::: info
영수증 검증은 SDK가 내부에서 자동으로 수행합니다. 중복 지급 방지·결제 금액 기록은 [더 알아보기](/guide/iap/advanced)를 참고하세요.
:::

::: details 플랫폼 전용 생성
구체 파사드 타입(`GooglePlayIAPFacade` / `AppleIAPFacade`)이 필요한 경우에만 사용합니다. 일반적인 크로스플랫폼 게임은 위 `CreateIAPAsync`로 충분합니다.

| 메서드 | 설명 |
|--------|------|
| `SupabaseIAP.CreateGooglePlayIAPAsync` | Google Play 전용 |
| `SupabaseIAP.CreateAppleIAPAsync` | Apple 전용 |
:::
