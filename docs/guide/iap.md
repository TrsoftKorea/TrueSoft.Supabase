# 인앱 결제 (IAP)

결제 후 서버에서 영수증을 검증하고 아이템을 안전하게 지급하는 기능입니다.  
클라이언트에서 직접 아이템을 주는 방식과 달리, 서버가 결제 사실을 확인한 뒤에만 지급합니다.  
Android (Google Play)와 iOS (App Store) 소모품 아이템을 하나의 코드로 처리합니다.

---

## 사전 준비

[빠른 시작](./getting-started.md)의 **Database Setup** 절차를 먼저 완료하세요.

이후 Package Manager에서 `com.unity.purchasing` **5.2.1 이상**을 설치합니다.  
설치 후 `TRUESOFT_IAP_AVAILABLE` 심볼이 자동으로 정의됩니다.

### 플랫폼 요구사항

| 플랫폼 | 최소 버전 | 이유 |
|--------|----------|------|
| iOS | **15.0 이상** | StoreKit 2 (JWS) 사용. StoreKit 1은 지원하지 않습니다. |
| Android | 기존 프로젝트 기준 | 별도 제약 없음 |

> **iOS 배포 대상 자동 설정**  
> SDK가 포함하는 Editor 스크립트(`IOSDeploymentTargetPostProcessor`)가 iOS 빌드 시 Xcode의 `IPHONEOS_DEPLOYMENT_TARGET`을 자동으로 **15.0 이상**으로 설정합니다.  
> 이미 15.0 이상으로 설정되어 있으면 변경하지 않습니다. App Store에서 iOS 14 이하 기기의 다운로드가 자동으로 차단됩니다.

---

## 사용법

```csharp
private IAPFacade _iapFacade;

private async void Start()
{
    _iapFacade?.Dispose();
    _iapFacade = await Supabase.CreateIAPAsync(
        productIds: new[] { "com.mygame.coins_100", "com.mygame.coins_500", "com.mygame.gems_10" },
        onGrant: async (productId, _, _) =>
        {
            switch (productId)
            {
                case "com.mygame.coins_100": await GiveCoinsAsync(100); break;
                case "com.mygame.coins_500": await GiveCoinsAsync(500); break;
                case "com.mygame.gems_10":   await GiveGemsAsync(10);   break;
            }
            return true;
        });
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

`onGrant` 콜백은 서버 영수증 검증이 완료된 직후 호출됩니다.  
아이템 지급 후 `true`를 반환하면 구매가 소비 처리됩니다.  
두 번째·세 번째 파라미터는 중복 지급 방지 등 고급 처리에 사용합니다. [더 알아보기](#더-알아보기)를 참고하세요.

---

## 주의사항

- **로그인 후 초기화**: `CreateIAPAsync`는 반드시 로그인 완료 이후에 호출하세요. 자동 로그인 경로에서도 마찬가지입니다.
- **소모품 전용**: 비소모품(Non-Consumable)과 구독(Subscription)은 현재 지원하지 않습니다.
- **반드시 Dispose**: `OnDestroy`에서 `Dispose()`를 호출하지 않으면 이벤트 핸들러가 누수됩니다.

---

## 더 알아보기

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
_iapFacade?.Dispose();
_iapFacade = await Supabase.CreateIAPAsync(
    productIds: new[] { "com.mygame.coins_100", "com.mygame.gems_10" },
    onGrant: async (productId, _, _) =>
    {
        // 아이템 지급
        return true;
    },
    onFailed: order => Debug.LogWarning("구매 실패: " + order));
```

### 결제 금액 자동 기록

구매 검증이 완료되면 `purchases` 테이블에 결제 금액 정보가 자동으로 저장됩니다.

| 컬럼 | 타입 | 내용 |
|------|------|------|
| `price_amount` | bigint | 결제 원금 (정수). Android는 `localizedPrice`, iOS는 JWS에서 추출 |
| `price_currency` | text | ISO 4217 통화 코드 (예: `"KRW"`, `"USD"`) |
| `price_amount_krw` | bigint | KRW 환산 금액. 결제 시점 환율 기준 (frankfurter.app). 환산 실패 시 null |

**플랫폼별 처리 방식**

- **Android**: 클라이언트가 Unity IAP `Product.metadata.localizedPrice` / `isoCurrencyCode`를 서버로 전달합니다.
- **iOS**: JWS 토큰에 가격 정보가 포함되어 있어 클라이언트가 별도로 전달하지 않습니다. 서버가 JWS에서 자동으로 추출합니다.

Retool 등 관리 도구에서 집계 시 `price_amount_krw` 컬럼을 사용하면 해외 결제 포함 합산이 가능합니다. 환산에 실패한 행은 `COALESCE(price_amount_krw, 0)`으로 처리하세요.
