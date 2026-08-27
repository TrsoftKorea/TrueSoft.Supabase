# 인앱 결제

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 플레이나누 유무와 무관하게 동일하게 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다.

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원하므로, `PlayNanooRuntime`은 `Awake`에서 `forceStoreKit1`로 SK1을 강제합니다. 필요한 Unity IAP 버전은 [버전별 차이](/guide/iap/versions#iap-versions)를 참고하세요.
:::
