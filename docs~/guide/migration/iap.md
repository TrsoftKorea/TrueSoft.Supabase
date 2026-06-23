# 인앱 결제

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 변경 없이 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다.

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원합니다. `PlayNanooRuntime`은 `Awake`에서 자동으로 SK1을 강제합니다.  
Unity IAP **5.0.x**는 SK1 강제가 불가능하므로 iOS 15+에서 플레이나누 IAP가 작동하지 않습니다. **4.x 또는 5.1 이상**을 사용하세요.
:::
