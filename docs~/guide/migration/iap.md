# 인앱 결제

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 플레이나누 유무와 무관하게 동일하게 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다.

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원하므로, `PlayNanooRuntime`은 `Awake`에서 `forceStoreKit1`로 SK1을 강제합니다. 이는 Unity IAP **5.1 이상**에서만 가능하므로 **`com.unity.purchasing` 4.x 또는 5.1 이상**을 사용하세요. **5.0.x는 SK1 강제가 불가**해 iOS 15+에서 플레이나누 IAP가 작동하지 않습니다.
:::
