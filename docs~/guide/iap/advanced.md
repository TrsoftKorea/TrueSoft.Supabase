# 더 알아보기 {#more}

중복 지급 방지는 [사용법](/guide/iap/usage#duplicate-grant)을 참고하세요 — `CreateIAPAsync`의 `onGrant`에서 바로 쓰는 내용이라 그쪽으로 옮겼습니다.

## 결제 금액 자동 기록 {#auto-price-tracking}

구매 검증이 완료되면 `purchases` 테이블에 결제 금액 정보가 자동으로 저장됩니다.

| 컬럼 | 타입 | 내용 |
|------|------|------|
| `price_amount` | bigint | 결제 원금. **micros** 단위로, 주 단위 ×1,000,000입니다. 정밀도 유지용 내부 값 |
| `price_currency` | text | ISO 4217 통화 코드. 예: `"KRW"`, `"USD"` |
| `price_amount_krw` | bigint | **KRW로 환산한 정수 금액.** 매출 확인은 이 값을 쓰세요. 환율은 결제 시점 frankfurter.app 기준. 환산 실패 시 null |

- **Android**: 클라이언트가 Unity IAP `Product.metadata.localizedPrice` / `isoCurrencyCode`를 서버로 전달합니다.
- **iOS SK2**: Unity IAP v5의 StoreKit 2 경로에서는 JWS 토큰에 가격 정보가 포함되어 있어 서버가 자동으로 추출합니다.
- **iOS SK1**: Unity IAP v4 또는 v5 `forceStoreKit1` 경로는 가격 정보가 영수증에 없으므로 `price_amount` / `price_currency`는 저장되지 않습니다.

::: tip Retool 집계
`price_amount_krw` 컬럼을 사용하면 해외 결제 포함 합산이 가능합니다.  
환산에 실패한 행은 `COALESCE(price_amount_krw, 0)`으로 처리하세요.
:::
