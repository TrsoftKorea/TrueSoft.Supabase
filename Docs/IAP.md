# 인앱 결제 (IAP) 서버 검증 가이드

Unity IAP v5 + Supabase Edge Function을 이용한 서버 측 영수증 검증 가이드입니다.  
Android (Google Play)와 iOS (Apple App Store) 소모품 아이템을 지원합니다.

---

## 검증 흐름

```
[앱] 결제창 표시
  ↓ 사용자 승인
[Unity IAP] OnPurchasePending 이벤트
  ↓ 영수증 자동 추출
[Facade] Supabase Edge Function 호출
  ↓ JWT 인증 + 영수증 전달
[Edge Function] Google / Apple 서버에 검증 요청
  ↓ 검증 성공
[DB] purchases 테이블 INSERT (중복 방지)
  ↓
[OnGrantItemAsync 콜백] 게임 코드에서 아이템 지급
  ↓ return true
[Unity IAP] ConfirmPurchase (소모품 소비 완료)
```

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
`Sql/player/14_purchases.sql`을 Supabase SQL Editor에서 실행하세요.  
기존 테이블이 있는 경우 `store` 컬럼만 추가됩니다 (기존 데이터 영향 없음).

---

## 통합 사용법 (권장)

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
                         // false → Pending 유지 → 다음 InitializeAsync에서 재처리
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

## Android 사용법 (Google Play)

```csharp
private GooglePlayIAPFacade _iapFacade;

private async void Start()
{
    _iapFacade = await Supabase.CreateGooglePlayIAPAsync(
        productIds: new[] { "com.mygame.item_1000" },
        onGrant: async (productId, isResuming, alreadyVerified) =>
        {
            await MyInventory.GiveItemAsync(productId);
            return true;
        });
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

## iOS 사용법 (Apple App Store)

```csharp
private AppleIAPFacade _iapFacade;

private async void Start()
{
    _iapFacade = await Supabase.CreateAppleIAPAsync(
        productIds: new[] { "com.mygame.item_1000" },
        onGrant: async (productId, isResuming, alreadyVerified) =>
        {
            await MyInventory.GiveItemAsync(productId);
            return true;
        });
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

## OnGrantItemAsync 콜백 상세

| 반환값 | 동작 |
|--------|------|
| `true` | Unity IAP가 `ConfirmPurchase` 호출 → 소모품 소비 완료 |
| `false` | Pending 유지 → 다음 `InitializeAsync` 호출 시 자동 재처리 |

### alreadyVerified / isResuming 플래그

| 플래그 | 의미 |
|--------|------|
| `isResuming` | 이전 앱 세션의 미처리 주문을 재처리 중 |
| `alreadyVerified` | 서버 DB에 이미 검증 기록이 있음 — 지급 후 크래시된 케이스 |

두 플래그의 조합으로 중복 지급을 안전하게 처리할 수 있습니다.

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

## 저수준 사용법 (직접 초기화)

`CreateXxxAsync` 대신 수동으로 설정·초기화할 수도 있습니다.

```csharp
var iapFacade = Supabase.CreateIAP();
iapFacade.OnGrantItemAsync = async (productId, isResuming, alreadyVerified) =>
{
    await MyInventory.GiveItemAsync(productId);
    return true;
};
iapFacade.OnPurchaseFailed += order => Debug.LogWarning("구매 실패: " + order);
await iapFacade.InitializeAsync(new[] { "com.mygame.item_1000" });
```

---

## DB 스키마 (purchases 테이블)

```sql
purchases
├── id              bigint (PK)
├── account_id      uuid → auth.users
├── user_id         text (영구 플레이어 ID)
├── product_id      text
├── purchase_token  text UNIQUE  -- Google: purchaseToken / Apple: transaction_id
├── order_id        text         -- Google: orderId / Apple: transaction_id
├── package_name    text         -- Google: packageName / Apple: bundleId
├── purchase_state  int          -- 0=purchased, 1=cancelled(Google only), 2=pending(Google only)
├── store           text         -- 'google_play' | 'apple_app_store'
└── verified_at     timestamptz
```

RLS로 각 사용자는 자신의 구매 내역만 조회·삽입 가능합니다.

---

## 주의사항

- **소모품 전용**: 비소모품(Non-Consumable)과 구독(Subscription)은 검증 로직이 달라 현재 지원하지 않습니다.
- **샌드박스 자동 전환**: Apple Edge Function은 프로덕션 → 샌드박스 순으로 자동 재시도합니다. 테스트 환경에서 별도 설정 불필요합니다.
- **초기화 실패**: `CreateIAPAsync`가 `null`을 반환하면 초기화 실패입니다. 네트워크 상태나 Unity Services 초기화를 확인하세요.
- **씬 언로드**: `Dispose()`를 반드시 호출하세요. 호출하지 않으면 이벤트 핸들러가 누수됩니다.
- **재초기화**: `Dispose()` 후 새 인스턴스를 생성하면 미처리 구매가 자동 재처리됩니다.
