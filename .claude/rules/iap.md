---
paths:
  - "Runtime/Unity/IAP/**/*.cs"
---

## IAP 코딩 규칙

### LogTag 명명

IAP 파사드마다 `LogTag`를 override해 로그 출처를 구분한다.

| 클래스 | LogTag |
|--------|--------|
| `BaseIAPFacade` (기본값) | `[Supabase.IAP]` |
| `IAPFacade` | `[Supabase.IAP]` (기본값 사용) |
| `AppleIAPFacade` | `[Supabase.IAP.Apple]` |
| `GooglePlayIAPFacade` | `[Supabase.IAP.Google]` |

새 플랫폼별 파사드를 추가할 때는 반드시 `protected override string LogTag`를 정의한다.

**표의 네 클래스마다 `*V4` 짝이 있다**(Unity IAP 4.x 경로 — `BaseIAPFacadeV4`·`IAPFacadeV4`·`AppleIAPFacadeV4`·`GooglePlayIAPFacadeV4`). LogTag 값은 같다. 로그를 고칠 때 한쪽만 고치면 v4 프로젝트에서만 형식이 어긋난다.

### 로그 메시지 형식

모든 로그는 `$"{LogTag} 메시지"` 형식. `productId`를 알 수 있는 시점이면 항상 포함한다.

| 상황 | 포함할 필드 | 예시 |
|------|------------|------|
| receipt / token 파싱 실패 | `product={productId}` | `purchaseToken 추출 실패. product={productId}` |
| 서버 검증 실패 (네트워크·응답 이상) | `product={productId}` | `서버 검증 실패. product={productId}` |
| 서버 검증 거부 (ok=false) | `reason={response.reason}, product={productId}` | `구매를 거부했습니다. reason={...}, product={...}` |
| 구매 실패 이벤트 (OnPurchaseFailed) | `product={productId}, reason={failureReason}` | `구매 실패: product={...}, reason={...}` |

`productId`를 아직 모르는 시점(null 체크 등)은 생략해도 된다.

### 로그 레벨

- `Debug.LogWarning` — 구매 흐름 이상 (파싱 실패, 서버 거부, 타임아웃 등 복구 가능)
- `Debug.LogError` — 내부 예외 (`OnGrantItemAsync` 예외, `ProcessPurchaseAsync` 예외)
- `Debug.LogError` — 설정 오류로 절대 동작할 수 없는 상황 (예: Unity IAP 5.0.x + PlayNanooRuntime)

---

