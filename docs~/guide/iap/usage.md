# 사용법

```csharp
Task<SupabaseResult<IAPFacade>> SupabaseIAP.CreateIAPAsync(
    string[]                              productIds,
    Func<string, bool, bool, Task<bool>>  onGrant,
    Action<IAPPurchaseFailedInfo>          onFailed  = null,
    int                                   timeoutMs = 10_000)
```

Unity IAP를 초기화하고 서버 영수증 검증 파이프라인을 연결합니다. Android/iOS를 자동 감지하므로 대부분의 게임은 이 메서드 하나면 됩니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `productIds` | 등록할 소모품 ID 목록 |
| `onGrant` | 서버 검증 완료 후 아이템 지급 콜백. `(productId, isResuming, alreadyVerified)` — `true` 반환 시 SDK가 소비(Confirm) 처리 |
| `onFailed` | 구매 실패 콜백. `IAPPurchaseFailedInfo.ProductId` / `.FailureReason` |
| `timeoutMs` | 초기화 대기 최대 시간 ms (기본값: `10_000`) |

**반환**

초기화 성공 시 `.Data`에 `IAPFacade`가 담깁니다. 이후 `iap.Purchase(productId)`로 결제창을 엽니다.

```csharp
var result = await SupabaseIAP.CreateIAPAsync(productIds, OnGrant);
if (!result.IsSuccess) return;
var iap = result.Data;
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.IapProductIdsEmpty` | `productIds`가 비어 있음 |
| `SupabaseReason.IapServicesInitFailed` | Unity Services 초기화 실패 |
| `SupabaseReason.IapInitTimeout` | 제한 시간 내 초기화 미완료 |
| `SupabaseReason.IapInitFailed` | 스토어 연결·상품 조회 실패 |

`onGrant`의 `isResuming` · `alreadyVerified` 파라미터는 [중복 지급 방지](/guide/iap/advanced#duplicate-grant)를 참고하세요.
