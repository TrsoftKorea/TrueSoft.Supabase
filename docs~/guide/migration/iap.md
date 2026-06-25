# 인앱 결제

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 변경 없이 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다.

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원하므로, `PlayNanooRuntime`은 `Awake`에서 SK1을 강제합니다. SDK IAP는 `com.unity.purchasing` **4.x 또는 5.2.1 이상**이 필요합니다 — **5.0~5.2.0은 지원되지 않으니** 이 구간을 피하세요. (5.2.1 이상은 `forceStoreKit1`을 지원합니다.)
:::
