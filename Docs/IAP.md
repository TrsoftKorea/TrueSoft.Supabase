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
    _iapFacade = Supabase.CreateIAP();  // 플랫폼 자동 감지

    _iapFacade.OnGrantItemAsync = async (order, response, isResuming) =>
    {
        var productId = order.CartOrdered.Items()[0].Product.definition.id;

        Debug.Log($"order_id={response.order_id}, store={response.store}");

        if (response.already_verified)
        {
            // 이미 검증된 영수증 — 중복 지급 없이 통과
        }

        await MyInventory.GiveItemAsync(productId);
        return true;
    };

    _iapFacade.OnPurchaseFailed += order => Debug.LogWarning("구매 실패: " + order);

    await _iapFacade.InitializeAsync(new[] { "com.mygame.item_1000" });
}

private void OnBuyButtonClicked()
{
    _iapFacade.Purchase("com.mygame.item_1000");
}

private void OnDestroy()
{
    _iapFacade?.Dispose();
}
```

`IAPPurchaseResponse` 공통 필드:

| 필드 | 설명 |
|------|------|
| `order_id` | Google: `orderId` / Apple: `transaction_id` |
| `store` | `"google_play"` 또는 `"apple_app_store"` |
| `already_verified` | 중복 검증 여부 |
| `purchase_state` | 0=구매완료 |
| `product_id` | Apple only (Google은 `order.CartOrdered`에서 읽으세요) |

---

## Android 사용법 (Google Play)

```csharp
private GooglePlayIAPFacade _iapFacade;

private async void Start()
{
    _iapFacade = Supabase.CreateGooglePlayIAP();

    // 아이템 지급 콜백 (필수 설정)
    _iapFacade.OnGrantItemAsync = async (order, response, isResuming) =>
    {
        var productId = order.CartOrdered.Items()[0].Product.definition.id;

        // 서버 검증 결과 확인
        Debug.Log($"order_id: {response.order_id}");

        if (response.already_verified)
        {
            // 이미 검증된 영수증 — 중복 지급 없이 그냥 통과
            // (앱 강제종료 후 재시작 시 정상 발생)
        }

        // 아이템 지급
        await MyInventory.GiveItemAsync(productId);

        return true; // true → Unity IAP가 ConfirmPurchase 호출 (소모품 소비)
                     // false → Pending 유지 → 다음 InitializeAsync에서 재처리
    };

    _iapFacade.OnPurchaseFailed += order => Debug.LogWarning("구매 실패: " + order);

    await _iapFacade.InitializeAsync(new[] { "com.mygame.item_1000" });
}

// 구매 버튼 클릭
private void OnBuyButtonClicked()
{
    _iapFacade.Purchase("com.mygame.item_1000");
}

private void OnDestroy()
{
    _iapFacade?.Dispose(); // 씬 언로드 시 반드시 호출
}
```

---

## iOS 사용법 (Apple App Store)

```csharp
private AppleIAPFacade _appleIapFacade;

private async void Start()
{
    _appleIapFacade = Supabase.CreateAppleIAP();

    _appleIapFacade.OnGrantItemAsync = async (order, response, isResuming) =>
    {
        var productId = order.CartOrdered.Items()[0].Product.definition.id;

        Debug.Log($"transaction_id: {response.transaction_id}");

        if (response.already_verified)
        {
            // 이미 검증된 영수증 — 정상 처리
        }

        await MyInventory.GiveItemAsync(productId);
        return true;
    };

    _appleIapFacade.OnPurchaseFailed += order => Debug.LogWarning("구매 실패: " + order);

    await _appleIapFacade.InitializeAsync(new[] { "com.mygame.item_1000" });
}

private void OnBuyButtonClicked()
{
    _appleIapFacade.Purchase("com.mygame.item_1000");
}

private void OnDestroy()
{
    _appleIapFacade?.Dispose();
}
```

---

## OnGrantItemAsync 콜백 상세

| 반환값 | 동작 |
|--------|------|
| `true` | Unity IAP가 `ConfirmPurchase` 호출 → 소모품 소비 완료 |
| `false` | Pending 유지 → 다음 `InitializeAsync` 호출 시 자동 재처리 |

### isResuming 플래그

```csharp
_iapFacade.OnGrantItemAsync = async (order, response, isResuming) =>
{
    if (isResuming)
    {
        // 앱 재시작 후 처리되지 않은 이전 구매를 재처리 중
        // DB에서 지급 여부를 확인한 뒤 중복 지급 방지 로직 추가 권장
    }
    else
    {
        // 방금 발생한 신규 구매
    }
    return true;
};
```

### already_verified 처리

```csharp
if (response.already_verified)
{
    // 같은 영수증이 이미 서버에서 검증된 적 있음
    // 원인: 앱 강제종료 → 재시작 → InitializeAsync에서 미처리 구매 재처리
    // 중복 지급 없이 정상 처리 후 return true
}
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
- **씬 언로드**: `Dispose()`를 반드시 호출하세요. 호출하지 않으면 이벤트 핸들러가 누수됩니다.
- **InitializeAsync 재호출**: `Dispose()` 후 새 인스턴스를 생성하면 미처리 구매가 자동 재처리됩니다.
