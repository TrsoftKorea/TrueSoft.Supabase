# 사용법

## 초기화

```csharp
Task<SupabaseResult<IAPFacade>> SupabaseIAP.CreateIAPAsync(
    string[]                       productIds,
    Func<string, bool, Task<bool>> onGrant,
    Action<IAPPurchaseFailedInfo>  onFailed  = null,
    int                            timeoutMs = 10_000)
```

Unity IAP를 초기화하고 서버 영수증 검증 파이프라인을 연결합니다. Android/iOS를 자동 감지하므로 대부분의 게임은 이 메서드 하나면 됩니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `productIds` | 등록할 소모품 ID 목록 |
| `onGrant` | 서버 검증 완료 후 아이템 지급 콜백. `(productId, alreadyGranted)` — `true` 반환 시 SDK가 결제를 소비 처리. [자세히](#duplicate-grant) |
| `onFailed` | 구매 실패 콜백. `IAPPurchaseFailedInfo.ProductId` / `.FailureReason` (기본값: `null`) |
| `timeoutMs` | 초기화 대기 최대 시간 ms (기본값: `10_000`) |

**반환**

초기화 성공 시 `.Data`에 `IAPFacade`가 담깁니다. 이후 `iap.Purchase(productId)`로 결제창을 엽니다.

```csharp
var result = await SupabaseIAP.CreateIAPAsync(productIds, async (productId, alreadyGranted) =>
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

::: warning 로그인 완료 후 호출
`CreateIAPAsync`는 로그인 완료 후에 호출하세요. 자동 로그인 경로에서도 동일합니다. 초기화 도중 스토어에 남아있는 미처리 주문이 있으면 [구매 처리 흐름](#purchase-flow)이 곧바로 재시작되는데, 이때 세션이 없으면 서버 검증이 실패해 불필요한 재시도가 한 번 낍니다.
:::

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

서버 검증이 실패하거나 `onGrant`가 `false`를 반환한 주문은 스토어에 **Pending 상태로 남아** 다음 `CreateIAPAsync` 호출 때 자동으로 다시 처리됩니다 — 보통 다음 앱 실행입니다.

## 중복 지급 방지 {#duplicate-grant}

`alreadyGranted`는 이 주문을 **서버**가 이미 지급 완료로 기록해 뒀는지를 나타냅니다. `onGrant`가 `true`를 반환하면 SDK가 소비 처리 직전에 서버에 자동으로 기록하므로, 게임이 직접 기록을 관리할 필요는 없습니다.

이전 결제가 지급까지는 성공했는데 그 직후 앱이 꺼져서 소비 처리가 안 된 경우, 재처리 시 `alreadyGranted = true`로 옵니다 — 소모품은 "이미 가지고 있는지"로 판단할 수 없으므로(골드처럼 쌓이는 재화는 항상 가지고 있음) 이 값만 보고 재지급 여부를 정합니다.

```csharp
onGrant: async (productId, alreadyGranted) =>
{
    if (alreadyGranted)
        return true;  // 이미 지급됨 → 소비만 완료

    await MyInventory.GiveItemAsync(productId);
    return true;
}
```

::: info 다른 계정의 영수증
다른 계정이 검증한 영수증을 보내면 `alreadyGranted`가 아니라 검증 자체가 거부됩니다. 이 플래그는 항상 "내 계정이 이미 지급한 것"만을 뜻합니다.

Google Play는 결제 시점의 계정도 서버가 대조합니다. 결제는 됐지만 아직 한 번도 검증되지 않은 주문을 다른 계정으로 검증하려 해도 거부됩니다 — 결제 시점 계정을 스토어에 심어 대조하기 때문입니다. Apple은 이 대조를 지원하는 플랫폼 API에 알려진 버그가 있어 아직 적용하지 못했습니다 — 앱이 강제 종료되는 등으로 검증 전에 계정을 바꾸면 미처리 주문이 새 계정으로 넘어갈 수 있습니다.
:::

구매창을 열기 전에 가격을 표시하려면 [상품 정보 조회](/guide/iap/product-info)를 참고하세요.

## 정리 {#cleanup}

`OnDestroy`에서 `Dispose()`를 호출해 이벤트 핸들러를 정리합니다.

::: tip 계정을 바꿀 때
로그아웃하거나 다른 계정으로 로그인하면 SDK가 등록된 IAP 파사드를 자동으로 `Dispose()`합니다. 이전 파사드가 남긴 이벤트 핸들러가 다음 계정 세션으로 미처리 주문을 처리하는 것을 막기 위해서입니다. 다시 로그인한 뒤에는 `CreateIAPAsync` 등으로 새로 만드세요.
:::
