# 인앱 결제

결제 후 서버에서 영수증을 검증하고 아이템을 안전하게 지급하는 기능입니다.  
클라이언트에서 직접 아이템을 주는 방식과 달리, 서버가 결제 사실을 확인한 뒤에만 지급합니다.  
Android (Google Play)와 iOS (App Store) 소모품 아이템을 하나의 코드로 처리합니다.

---

## 사전 준비

[Database Setup](./getting-started.md#database-setup) 절차를 먼저 완료하세요.

이후 Package Manager에서 `com.unity.purchasing` **4.x 이상**을 설치합니다.

---

## 사용법

```csharp
private IAPFacade _iapFacade;

private async void Start()
{
    _iapFacade?.Dispose();
    _iapFacade = await SupabaseIAP.CreateIAPAsync(
        productIds: new[] { "com.mygame.coins_100", "com.mygame.coins_500", "com.mygame.gems_10" },
        onGrant: async (productId, isResuming, alreadyVerified) =>
        {
            switch (productId)
            {
                case "com.mygame.coins_100": await GiveCoinsAsync(100); break;
                case "com.mygame.coins_500": await GiveCoinsAsync(500); break;
                case "com.mygame.gems_10":   await GiveGemsAsync(10);   break;
            }
            return true; // true → 소비(Confirm) 처리
        },
        onFailed:  info => Debug.LogWarning($"구매 실패: {info.ProductId} / {info.FailureReason}"),
        timeoutMs: 10_000); // 기본값 — 생략 가능
}

// 구매 버튼마다 호출
private void OnBuyCoins100Clicked() => _iapFacade?.Purchase("com.mygame.coins_100");
private void OnBuyCoins500Clicked() => _iapFacade?.Purchase("com.mygame.coins_500");
private void OnBuyGems10Clicked()   => _iapFacade?.Purchase("com.mygame.gems_10");

private void OnDestroy()
{
    _iapFacade?.Dispose();
}
```

| 파라미터 | 설명 |
|----------|------|
| `productIds` | 등록할 소모품 ID 목록 |
| `onGrant` | 서버 검증 완료 후 아이템 지급 콜백. `true` 반환 시 SDK가 소비 처리 |
| `onFailed` | 구매 실패 콜백 (선택) |
| `timeoutMs` | 초기화 대기 최대 시간 ms (기본 10초) |

`onGrant`의 `isResuming` · `alreadyVerified` 파라미터는 [중복 지급 방지](#중복-지급-방지)를 참고하세요.

---

## 주의사항

::: warning
- **로그인 후 초기화**: `CreateIAPAsync`는 반드시 로그인 완료 이후에 호출하세요. 자동 로그인 경로에서도 마찬가지입니다.
- **소모품 전용**: 비소모품(Non-Consumable)과 구독(Subscription)은 현재 지원하지 않습니다.
- **반드시 Dispose**: `OnDestroy`에서 `Dispose()`를 호출하지 않으면 이벤트 핸들러가 누수됩니다.
:::

---

## 더 알아보기 {#more}

### 중복 지급 방지

아이템을 지급한 직후 앱이 크래시되면, 다음 실행 시 같은 주문이 재처리됩니다.  
이때 `alreadyVerified = true`로 전달되며, 이 신호를 이용해 중복 지급을 막을 수 있습니다.

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

| 플래그 | 의미 |
|--------|------|
| `isResuming` | 이전 세션의 미처리 주문을 재처리 중 |
| `alreadyVerified` | 서버 DB에 이미 검증 기록이 있음 |

### 구매 실패 콜백

```csharp
_iapFacade = await SupabaseIAP.CreateIAPAsync(
    productIds: new[] { "com.mygame.coins_100" },
    onGrant: async (productId, _, _) =>
    {
        await GiveItemAsync(productId);
        return true;
    },
    onFailed:  info => Debug.LogWarning($"구매 실패: {info.ProductId} / {info.FailureReason}"),
        timeoutMs: 10_000);
```

### 결제 금액 자동 기록

구매 검증이 완료되면 `purchases` 테이블에 결제 금액 정보가 자동으로 저장됩니다.

| 컬럼 | 타입 | 내용 |
|------|------|------|
| `price_amount` | bigint | 결제 원금 (정수). Android는 `localizedPrice`에서 추출 |
| `price_currency` | text | ISO 4217 통화 코드 (예: `"KRW"`, `"USD"`) |
| `price_amount_krw` | bigint | KRW 환산 금액. 결제 시점 환율 기준 (frankfurter.app). 환산 실패 시 null |

- **Android**: 클라이언트가 Unity IAP `Product.metadata.localizedPrice` / `isoCurrencyCode`를 서버로 전달합니다.
- **iOS SK2** (Unity IAP v5, StoreKit 2): JWS 토큰에 가격 정보가 포함되어 있어 서버가 자동으로 추출합니다.
- **iOS SK1** (Unity IAP v4, 또는 SK1 강제 모드): 가격 정보가 영수증에 없으므로 `price_amount` / `price_currency`는 저장되지 않습니다.

::: tip Retool 집계
`price_amount_krw` 컬럼을 사용하면 해외 결제 포함 합산이 가능합니다.  
환산에 실패한 행은 `COALESCE(price_amount_krw, 0)`으로 처리하세요.
:::

---

## Unity IAP 버전별 차이 {#iap-versions}

SDK는 Unity IAP **v4와 v5를 모두 지원**합니다. 게임 코드는 동일하며, 내부 영수증 처리 방식만 다릅니다.

| 항목 | v4 (4.x) | v5 (5.x) |
|------|----------|----------|
| iOS 영수증 형식 | SK1 (base64 receipt blob) | SK2 JWS (iOS 15+) / SK1 폴백 (iOS 14 이하, 또는 `forceStoreKit1`) |
| iOS 서버 검증 함수 | `purchase-verify-apple-legacy` | `purchase-verify-apple` (SK2) + `purchase-verify-apple-legacy` (SK1 폴백) |
| iOS 가격 자동 추출 | ✗ | ✔ (SK2 경로만 해당) |
| PlayNanoo IAP 연동 | ✔ | ✔ (v5.1+ 에서 `forceStoreKit1` 자동 설정) |

::: warning Unity IAP 5.0.x + PlayNanoo
Unity IAP 5.0.x는 iOS 15 미만 기기에서 SK1을 강제할 수 없습니다.  
PlayNanoo IAP는 SK1만 지원하므로, PlayNanoo와 함께 사용한다면 **Unity IAP 5.1 이상**으로 업그레이드하세요.  
5.0.x에서 `PlayNanooRuntime`을 씬에 배치하면 콘솔에 오류가 출력됩니다.
:::
