# 인앱 결제 API

소모품 결제를 초기화하고 서버 영수증 검증까지 연결합니다.

| 메서드 | 설명 |
|--------|------|
| [`SupabaseIAP.CreateIAPAsync`](/guide/iap/usage) | 플랫폼 자동 감지 IAP 생성·초기화 |
| [`SupabaseIAP.CreateGooglePlayIAPAsync`](/guide/iap/usage) | Google Play 전용 IAP 생성·초기화 |
| [`SupabaseIAP.CreateAppleIAPAsync`](/guide/iap/usage) | Apple 전용 IAP 생성·초기화 |

::: info
영수증 검증은 SDK가 내부에서 자동으로 수행합니다. 중복 지급 방지·결제 금액 기록은 [더 알아보기](/guide/iap/advanced)를 참고하세요.
:::
