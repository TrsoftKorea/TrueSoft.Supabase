# 인앱 결제 (IAP)

Unity IAP v5 + Supabase Edge Function을 이용한 서버 측 영수증 검증입니다.  
Android (Google Play)와 iOS (Apple App Store) 소모품 아이템을 지원합니다.

---

## 사전 준비

### 1. Unity Package Manager

`com.unity.purchasing` **5.2.1 이상** 설치 필요.  
설치 후 `TRUESOFT_IAP_AVAILABLE` 심볼이 자동 정의됩니다.

### 2. Google Play 설정

| 항목 | 위치 |
|------|------|
| Google Service Account JSON 발급 | Google Play Console > 설정 > API 액세스 |
| Supabase 시크릿 등록 | Supabase Dashboard > Edge Functions > Secrets > `GOOGLE_SERVICE_ACCOUNT_JSON` |

### 3. Apple App Store 설정

| 항목 | 위치 |
|------|------|
| 앱 공유 암호 발급 | App Store Connect > 앱 선택 > 앱 정보 > 앱 공유 암호 |
| Supabase 시크릿 등록 | Supabase Dashboard > Edge Functions > Secrets > `APPLE_SHARED_SECRET` |

### 4. Edge Function 배포

```bash
supabase functions deploy purchase-verify-google
supabase functions deploy purchase-verify-apple
```

### 5. DB 마이그레이션

`Sql/player/07_purchases.sql`을 Supabase SQL Editor에서 실행하세요.

---

## 사용법

플랫폼 분기 없이 Android와 iOS를 하나의 코드로 처리합니다.

```csharp
private IAPFacade _iapFacade;

private async void Start()
{
    _iapFacade = await Supabase.CreateIAPAsync(
        productIds: new[] { "com.mygame.item_1000" },
        onGrant: async (productId, isResuming, alreadyVerified) =>
        {
            await MyInventory.GiveItemAsync(productId);
            return true; // true → SDK가 ConfirmPurchase 호출 (소모품 소비)
                         // false → Pending 유지 → 다음 초기화 시 재처리
        });

    if (_iapFacade == null)
    {
        Debug.LogWarning("IAP 초기화 실패");
        return;
    }
}

private void OnBuyButtonClicked()
{
    _iapFacade?.Purchase("com.mygame.item_1000");
}

private void OnDestroy()
{
    _iapFacade?.Dispose();
}
```

---

## OnGrantItemAsync 콜백

| 반환값 | 동작 |
|--------|------|
| `true` | Unity IAP가 `ConfirmPurchase` 호출 → 소모품 소비 완료 |
| `false` | Pending 유지 → 다음 초기화 시 자동 재처리 |

### alreadyVerified / isResuming 플래그

| 플래그 | 의미 |
|--------|------|
| `isResuming` | 이전 앱 세션의 미처리 주문을 재처리 중 |
| `alreadyVerified` | 서버 DB에 이미 검증 기록이 있음 — 지급 후 크래시된 케이스 |

> [!IMPORTANT]
> `alreadyVerified`는 `isResuming`과 독립적인 신호입니다.  
> `isResuming=true`이더라도 서버 DB 기록이 없으면 `alreadyVerified=false`입니다.  
> 두 플래그의 조합으로 중복 지급을 안전하게 처리하세요.

```csharp
onGrant: async (productId, isResuming, alreadyVerified) =>
{
    if (alreadyVerified)
    {
        // 서버는 이 영수증을 이미 처리했음
        // 원인: 아이템 지급 후 ConfirmPurchase 전에 앱이 크래시된 경우
        // DB에서 지급 여부를 확인해 중복 지급을 방지하세요
        bool alreadyGranted = await MyInventory.HasItemAsync(productId);
        if (alreadyGranted) return true; // 이미 지급됨 → 소비만 완료
    }

    await MyInventory.GiveItemAsync(productId);
    return true;
}
```

### 구매 실패 콜백

```csharp
_iapFacade = await Supabase.CreateIAPAsync(
    productIds: new[] { "com.mygame.item_1000" },
    onGrant: async (productId, isResuming, alreadyVerified) =>
    {
        await MyInventory.GiveItemAsync(productId);
        return true;
    },
    onFailed: order => Debug.LogWarning("구매 실패: " + order));
```

---

## 주의사항

> [!WARNING]
> **초기화 타이밍**: `CreateIAPAsync`는 로그인 완료 이후에 호출해야 합니다.  
> 자동 로그인 경로(앱 재시작)에서도 IAP가 초기화되도록  
> `Supabase.IsLoggedIn`을 폴링하거나 로그인 완료 콜백 내에서 호출하세요.

- **소모품 전용**: 비소모품(Non-Consumable)과 구독(Subscription)은 검증 로직이 달라 현재 지원하지 않습니다.
- **샌드박스 자동 전환**: Apple Edge Function은 프로덕션 → 샌드박스 순으로 자동 재시도합니다.  
  테스트 환경에서 별도 설정 불필요합니다.
- **초기화 실패**: `CreateIAPAsync`가 `null`을 반환하면 초기화 실패입니다. 네트워크 연결을 확인하세요.
- **씬 언로드**: `Dispose()`를 반드시 호출하세요. 호출하지 않으면 이벤트 핸들러가 누수됩니다.
- **재초기화**: `Dispose()` 후 새 인스턴스를 생성하면 미처리 구매가 자동 재처리됩니다.
