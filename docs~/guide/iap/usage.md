# 사용법

소모품 결제를 초기화하고 서버 영수증 검증까지 한 번에 연결합니다.

```csharp
Task<IAPFacade> SupabaseIAP.CreateIAPAsync(
    string[]                              productIds,
    Func<string, bool, bool, Task<bool>>  onGrant,
    Action<IAPPurchaseFailedInfo>          onFailed  = null,
    int                                   timeoutMs = 10_000)
```

Unity IAP를 초기화하고 서버 영수증 검증 파이프라인을 연결합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `productIds` | 등록할 소모품 ID 목록 |
| `onGrant` | 서버 검증 완료 후 아이템 지급 콜백. `(productId, isResuming, alreadyVerified)` — `true` 반환 시 SDK가 소비(Confirm) 처리 |
| `onFailed` | 구매 실패 콜백. `IAPPurchaseFailedInfo.ProductId` / `.FailureReason` |
| `timeoutMs` | 초기화 대기 최대 시간 ms (기본값: `10_000`) |

`onGrant`의 `isResuming` · `alreadyVerified` 파라미터는 [중복 지급 방지](/guide/iap/advanced#duplicate-grant)를 참고하세요.
