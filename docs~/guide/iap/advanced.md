# 더 알아보기 {#more}

중복 지급 방지는 [사용법](/guide/iap/usage#duplicate-grant)을 참고하세요 — `CreateIAPAsync`의 `onGrant`에서 바로 쓰는 내용이라 그쪽으로 옮겼습니다.

## 결제 금액 자동 기록 {#auto-price-tracking}

구매 검증이 완료되면 `purchases` 테이블에 결제 금액 정보가 자동으로 저장됩니다.

| 컬럼 | 타입 | 내용 |
|------|------|------|
| `price_amount` | bigint | 결제 원금. **micros** 단위로, 주 단위 ×1,000,000입니다. 정밀도 유지용 내부 값 |
| `price_currency` | text | ISO 4217 통화 코드. 예: `"KRW"`, `"USD"` |
| `price_amount_krw` | bigint | **KRW로 환산한 정수 금액.** 매출 확인은 이 값을 쓰세요. 환율은 결제 날짜 기준. 금액이나 환율을 아직 모르면 null |
| `is_test` | boolean | [테스트 결제](/guide/iap/prepare#test-purchases)면 true. 테스트인지 알 수 없으면 null이고, 실제 결제로 셉니다 |
| `is_free` | boolean | Google&nbsp;Play 프로모션 코드·광고 보상처럼 돈을 내지 않은 결제면 true |

매출을 직접 셀 때는 `is_test is not true and is_free is not true`로 거르세요.

- **Android**: 클라이언트가 Unity IAP `Product.metadata.localizedPrice` / `isoCurrencyCode`를 서버로 전달합니다.
- **iOS SK2**: StoreKit 2 경로에서는 JWS 토큰에 가격 정보가 포함되어 있어 서버가 자동으로 추출합니다.
- **iOS SK1**: `forceStoreKit1` 경로는 영수증에 가격이 없습니다. [앱 내 구입 키](/guide/start/database-setup#secrets)를 등록해 두면 서버가 애플에 그 거래의 실제 결제 금액을 물어 함께 저장합니다. 키가 없으면 금액 없이 저장됩니다.

환율은 서버가 하루 한 번 받아 두는 환율표에서 결제 날짜, 그날 발표가 없으면 그 직전 발표일의 값을 씁니다. 결제를 확인할 때 환율을 못 구했으면 다음 자동 작업이 채웁니다. 자동 작업은 하루 한 번, 한국 시간 오후 1시 30분에 돕니다. SK1 금액은 결제를 기록한 직후 애플에 묻고, 애플이 답하지 못했으면 다음 자동 작업이, 애플이 그 거래를 모른다고 하면 7일 뒤에 다시 묻습니다. 운영 콘솔 **구매 내역** 위쪽의 "원화 환산" 줄에서 마지막 실행 결과를 볼 수 있습니다.

::: tip Retool 집계
`price_amount_krw` 컬럼을 사용하면 해외 결제 포함 합산이 가능합니다.  
아직 금액을 모르는 행은 `COALESCE(price_amount_krw, 0)`으로 처리하세요.
:::

::: info 스토어 화면의 매출과 차이가 납니다
Google&nbsp;Play는 주문마다 그 순간의 환율로, App&nbsp;Store의 판매 및 추세는 지난달 환율의 이동 평균으로 계산합니다. 여기 금액은 결제 날짜의 환율이라 몇 % 차이가 날 수 있고, 스토어 수수료와 세금을 빼기 전 금액입니다.
:::
