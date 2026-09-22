# 인앱 결제

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 플레이나누 유무와 무관하게 동일하게 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다. 이때 플레이나누가 준 거절 사유가 `[PlayNanooRuntime] PlayNANOO Android 결제 검증 실패` 로그에 상품 ID·ErrorCode·메시지까지 함께 남습니다.

::: warning 플레이나누에는 영수증 원문을 넘깁니다
플레이나누 결제 검증 API는 구매 토큰이 아니라 **Unity IAP 영수증 원문**을 받습니다. 토큰만 넘기면 서버가 영수증을 찾지 못해 `NotFoundReceiptException`으로 거절합니다. `PlayNanooRuntime`이 인터셉터에서 영수증 원문을 넘기도록 되어 있으니, `NanooIAPAndroid`를 재정의할 때 이 인자를 그대로 전달하세요.
:::

::: warning 플레이나누를 읽지 못하면 동기화를 건너뜁니다
플레이나누 스토리지를 읽지 못한 로그인에서는 어느 쪽이 최신인지 판단할 수 없으므로, SDK가 **가져오지도 덮어쓰지도 않고** 그 로그인 동안 플레이나누 쓰기를 막습니다. 다음 로그인에서 다시 맞춥니다. 예전에는 읽기 실패를 "플레이나누가 낡음"으로 판단해 원본을 덮어썼습니다.
:::

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원하므로, `PlayNanooRuntime`은 `Awake`에서 `forceStoreKit1`로 SK1을 강제합니다. 필요한 Unity IAP 버전은 [Unity IAP 버전](/guide/iap/versions#iap-versions)를 참고하세요.
:::
