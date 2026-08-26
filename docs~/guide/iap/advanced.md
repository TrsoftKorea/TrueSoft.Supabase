# 더 알아보기 {#more}

## 중복 지급 방지 {#duplicate-grant}

아이템을 지급한 직후 앱이 크래시되면, 다음 실행 시 같은 주문이 재처리됩니다.  
이때 `alreadyVerified = true`로 전달되며, [사용법](/guide/iap/usage)의 `onGrant`에 이 확인 하나만 추가하면 중복 지급을 막을 수 있습니다.

```csharp
async (productId, isResuming, alreadyVerified) =>
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
| `alreadyVerified` | 이 계정의 검증 기록이 서버 DB에 이미 있음 |

다른 계정이 검증한 영수증을 보내면 `alreadyVerified`가 아니라 검증 자체가 거부됩니다. 이 플래그는 항상 "내가 이미 산 것"만을 뜻합니다.

## 결제 금액 자동 기록

구매 검증이 완료되면 `purchases` 테이블에 결제 금액 정보가 자동으로 저장됩니다.

| 컬럼 | 타입 | 내용 |
|------|------|------|
| `price_amount` | bigint | 결제 원금(**micros** = 주 단위 ×1,000,000). 정밀도 유지용 내부 값 |
| `price_currency` | text | ISO 4217 통화 코드 (예: `"KRW"`, `"USD"`) |
| `price_amount_krw` | bigint | **KRW 환산 금액(원, 정수)** — 매출 확인은 이 값을 쓰세요. 결제 시점 환율 기준(frankfurter.app). 환산 실패 시 null |

- **Android**: 클라이언트가 Unity IAP `Product.metadata.localizedPrice` / `isoCurrencyCode`를 서버로 전달합니다.
- **iOS SK2** (Unity IAP v5, StoreKit 2): JWS 토큰에 가격 정보가 포함되어 있어 서버가 자동으로 추출합니다.
- **iOS SK1** (Unity IAP v4, 또는 v5 `forceStoreKit1`): 가격 정보가 영수증에 없으므로 `price_amount` / `price_currency`는 저장되지 않습니다.

::: tip Retool 집계
`price_amount_krw` 컬럼을 사용하면 해외 결제 포함 합산이 가능합니다.  
환산에 실패한 행은 `COALESCE(price_amount_krw, 0)`으로 처리하세요.
:::
