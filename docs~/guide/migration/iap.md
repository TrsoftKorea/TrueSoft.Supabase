# 인앱 결제

## 결제 처리 순서 {#flow}

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 플레이나누 유무와 무관하게 동일하게 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다. 이때 플레이나누가 준 거절 사유가 `[PlayNanooRuntime] PlayNANOO Android 결제 검증 실패`(iOS는 `iOS 결제 검증 실패`) 로그에 상품 ID·ErrorCode·메시지까지 함께 남습니다.

::: warning 플레이나누에는 영수증 원문을 넘깁니다
플레이나누 결제 검증 API는 Android·iOS 모두 **Unity IAP 영수증 원문**을 받습니다. Android의 구매 토큰이나 iOS 영수증 안의 Payload만 넘기면 서버가 영수증을 찾지 못해 `NotFoundReceiptException`으로 거절합니다. `PlayNanooRuntime`이 인터셉터에서 영수증 원문을 넘기도록 되어 있으니, `NanooIAPAndroid`·`NanooIAPIOS`를 재정의할 때 이 인자를 그대로 전달하세요.
:::

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원하므로, `PlayNanooRuntime`은 `Awake`에서 `forceStoreKit1`로 SK1을 강제합니다. 필요한 Unity IAP 버전은 [Unity IAP 버전](/guide/iap/versions#iap-versions)를 참고하세요.
:::

## iOS 결제 진단 {#ios-trace}

iOS 결제가 실패하면 먼저 `[Supabase.IAP] iOS 검증 경로` 로그를 봅니다. 플레이나누 검증은 SK1 경로에서만 불리므로, `SK2(JWS)`로 나오면 플레이나누를 거치지 않은 것입니다. 이 로그는 `SupabaseSettings`의 **API 결과 로그 사용**을 따릅니다.

더 자세히 보려면 `PlayNanooRuntime` 인스펙터에서 [진단 › Nanoo Trace](./sync#trace)를 켭니다. 결제 한 건마다 `[PlayNanooTrace] iOS 결제 1/3~3/3` 로그로 플레이나누에 넘긴 영수증의 모양, 플레이나누 응답, SDK 검증 결과가 차례로 남습니다. 영수증 내용은 남기지 않고 길이와 필드만 남깁니다.
