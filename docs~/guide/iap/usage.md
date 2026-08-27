# 사용법

## 초기화

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
| `onGrant` | 서버 검증 완료 후 아이템 지급 콜백. `(productId, isResuming, alreadyVerified)` — `true` 반환 시 SDK가 결제를 소비 처리. [자세히](#duplicate-grant) |
| `onFailed` | 구매 실패 콜백. `IAPPurchaseFailedInfo.ProductId` / `.FailureReason` (기본값: `null`) |
| `timeoutMs` | 초기화 대기 최대 시간 ms (기본값: `10_000`) |

**반환**

초기화 성공 시 `.Data`에 `IAPFacade`가 담깁니다. 이후 `iap.Purchase(productId)`로 결제창을 엽니다.

```csharp
var result = await SupabaseIAP.CreateIAPAsync(productIds, async (productId, isResuming, alreadyVerified) =>
{
    await MyInventory.GiveItemAsync(productId);
    return true;
});
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

## 구매 처리 흐름 {#purchase-flow}

`iap.Purchase(productId)`로 결제창을 연 뒤, 실제 지급까지는 이런 순서로 진행됩니다.

```
결제 완료
  └─ 서버 영수증 검증
       ├─ 실패 → 종료. 주문은 Pending 유지 → 다음 CreateIAPAsync(재초기화)에서 재시도
       └─ 성공 → onGrant 호출
            ├─ true 반환 → 소비(Confirm) 처리 → 완료
            └─ false 반환 또는 예외 → 소비 안 함. 주문은 Pending 유지 → 다음 재초기화에서 재시도
```

**소비 처리는 항상 `onGrant`가 끝난 뒤에만 일어납니다** — 지급이 확실히 됐다고 게임 코드가 `true`로 확인해 준 다음에야 SDK가 스토어에 "이 결제 끝났다"고 알립니다. 순서가 반대라면 소비 처리부터 해버린 뒤 지급이 실패했을 때 그 결제 기록을 되돌릴 방법이 없습니다.

서버 검증이 실패하거나 `onGrant`가 `false`를 반환한 주문은 스토어에 **Pending 상태로 남아** 다음 `CreateIAPAsync` 호출(보통 다음 앱 실행) 때 자동으로 다시 처리됩니다 — 이때 `onGrant`에 전달되는 `isResuming`이 `true`가 됩니다.

## 중복 지급 방지 {#duplicate-grant}

`onGrant`의 `isResuming`·`alreadyVerified`는 서로 다른 곳에서 독립적으로 매겨지는 값입니다.

- **`isResuming`** — 방금 결제한 새 주문인지, 이전 세션에서 못 끝낸 주문을 재처리하는 중인지를 **클라이언트**가 판단합니다.
- **`alreadyVerified`** — 이 영수증을 **서버**가 예전에 이미 처리한 적이 있는지를 판단합니다. `purchases` 테이블의 영수증 고유값 UNIQUE 제약으로 감지합니다.

두 값의 조합에 따라 실제 상황이 갈립니다.

| isResuming | alreadyVerified | 상황 |
|---|---|---|
| false | false | 방금 새로 결제함 — 가장 흔한 경우 |
| true | false | 이전 결제가 서버 검증까지도 못 가고 앱이 꺼짐 — 사실상 이번이 첫 지급 |
| **true** | **true** | 이전 결제가 서버 검증까지는 성공했는데 그 직후 앱이 꺼져서 지급이 안 됐거나, 지급 후 소비 처리 전에 꺼짐 — **중복 지급 위험 케이스** |
| false | true | 드물지만 새 구매 이벤트인데 서버엔 이미 같은 영수증이 있는 경우. 스토어가 영수증을 중복 전달하는 등 |

**`alreadyVerified = true`일 때만** 실제로 이미 지급했는지 확인하면 중복 지급을 막을 수 있습니다. `isResuming`은 지급 여부를 가르지 않고, 로그·UX 판단(재처리 중엔 "구매 완료!" 팝업을 안 띄우는 등)에 참고하는 정보입니다.

```csharp
onGrant: async (productId, isResuming, alreadyVerified) =>
{
    if (alreadyVerified)
    {
        // 서버에 이미 검증 기록이 있음 — 실제로 지급됐는지 확인
        bool alreadyGranted = await MyInventory.HasItemAsync(productId);
        if (alreadyGranted) return true;  // 이미 지급됨 → 소비만 완료
    }

    await MyInventory.GiveItemAsync(productId);
    return true;
}
```

::: info 다른 계정의 영수증
다른 계정이 검증한 영수증을 보내면 `alreadyVerified`가 아니라 검증 자체가 거부됩니다. 이 플래그는 항상 "내 계정이 이미 처리한 것"만을 뜻합니다.
:::

구매창을 열기 전에 가격을 표시하려면 [상품 정보 조회](/guide/iap/product-info)를 참고하세요.
