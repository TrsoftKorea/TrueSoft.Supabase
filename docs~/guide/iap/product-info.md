# 상품 정보 조회

```csharp
IAPProductInfo IAPFacade.GetProductInfo(string productId)
```

스토어 카탈로그에서 가격·통화 등 상품 정보를 조회합니다. 네트워크 호출 없이 `CreateIAPAsync`가 초기화 시점에 이미 받아온 정보를 그대로 읽습니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `productId` | 조회할 상품 ID |

**반환**

초기화 전이거나 잘못된 ID로 카탈로그에 없으면 `null`.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `ProductId` | `string` | 상품 ID |
| `Title` | `string` | 로컬라이즈된 상품명 |
| `Description` | `string` | 로컬라이즈된 상품 설명 |
| `PriceString` | `string` | 통화 기호가 포함된 가격 문자열. 예: `"₩1,200"` |
| `Price` | `decimal` | 가격(통화 소수점 단위) |
| `CurrencyCode` | `string` | ISO 4217 통화 코드. 예: `"KRW"` |
| `IsAvailable` | `bool` | 스토어에서 구매 가능한 상품인지 |

```csharp
var info = iap.GetProductInfo(productId);
if (info != null)
    priceLabel.text = info.PriceString;
```

`GooglePlayIAPFacade`·`AppleIAPFacade`에도 같은 메서드가 있습니다.
