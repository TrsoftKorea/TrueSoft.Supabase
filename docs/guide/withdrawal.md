# 탈퇴 · 서버 이주

---

## 탈퇴 처리

즉시 삭제하지 않고 일정 유예 기간 후에 처리됩니다. 유예 기간 동안 플레이어가 탈퇴를 취소할 수 있습니다.  
유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

::: info
유예 기간이 만료된 계정은 로그인 시 자동으로 처리됩니다.  
[Edge Function 배포](./getting-started.md#edge-function-deploy)가 완료되어 있어야 합니다.
:::

```csharp
Task<SupabaseCallResult> Supabase.TryRequestMyWithdrawalAsync()
```

탈퇴를 예약합니다. 요청이 성공하면 즉시 로그아웃 처리됩니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

```csharp
Task<MyWithdrawalStatus?> Supabase.TryGetMyWithdrawalStatusAsync()
```

현재 탈퇴 예약 상태를 조회합니다. 조회 실패 시 `null`을 반환합니다.

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.IsScheduled` | `bool` | 탈퇴 유예 예약 여부 |
| `.SecondsRemaining` | `long` | 탈퇴까지 남은 시간 (초) |
| `.WithdrawnAtIso` | `string` | 탈퇴 예약 일시 (ISO 8601) |

---

```csharp
Task<SupabaseCallResult> Supabase.TryClearMyWithdrawalAsync()
```

탈퇴 예약을 취소합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

```csharp
await Supabase.TryRequestMyWithdrawalAsync();   // 탈퇴 예약
await Supabase.TryGetMyWithdrawalStatusAsync(); // 예약 상태 및 남은 시간 조회
await Supabase.TryClearMyWithdrawalAsync();     // 예약 취소
```

---

## 탈퇴 취소 — 토큰 방식

유예 기간이 지나 이미 탈퇴가 완료된 경우, 토큰을 이용해 계정을 복구할 수 있습니다.  
서버에서 토큰을 발급받아 이메일 등으로 전달하고, 플레이어가 해당 토큰으로 취소를 완료하는 방식입니다.

::: warning
[Edge Function 배포](./getting-started.md#edge-function-deploy)가 완료되어 있어야 합니다.
:::

```csharp
Task<string> Supabase.TryRequestWithdrawalCancelTokenAsync(string defaultValue = null)
```

탈퇴 취소 토큰을 발급합니다. 실패 시 `defaultValue`를 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `defaultValue` | 토큰 발급 실패 시 반환할 기본값 (기본값: `null`) | `string` |

---

```csharp
Task<SupabaseCallResult> Supabase.TryRedeemWithdrawalCancelAsync(string cancelToken = null)
```

탈퇴 취소 토큰을 사용해 탈퇴를 취소합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `cancelToken` | 플레이어가 입력한 취소 토큰 | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

```csharp
// 탈퇴 취소 토큰 발급 — 플레이어에게 전달
var token = await Supabase.TryRequestWithdrawalCancelTokenAsync(defaultValue: null);

// 플레이어가 토큰을 입력해 취소 완료
await Supabase.TryRedeemWithdrawalCancelAsync(cancelToken: token);
```

---

## 서버 이주

플레이어를 다른 서버로 이동시킵니다. 서버별로 닉네임 고유성이 관리되므로, 이주 대상 서버에 같은 닉네임이 이미 존재하면 실패합니다.

```csharp
Task<SupabaseCallResult> Supabase.TryTransferMyServerAsync(string targetServerCode, string reason = null)
```

현재 계정을 지정한 서버로 이주합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `targetServerCode` | 이주할 서버 코드 (예: `"GLOBAL"`, `"KR1"`) | `string` |
| `reason` | 이주 사유. 서버 로그에만 기록됨 (기본값: `null`) | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

```csharp
await Supabase.TryTransferMyServerAsync("GLOBAL", reason: null);
```
