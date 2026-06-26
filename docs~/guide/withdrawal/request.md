# 탈퇴 처리

즉시 삭제하지 않고 일정 유예 기간 후에 처리됩니다. 유예 기간 동안 플레이어가 탈퇴를 취소할 수 있습니다.  
유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

::: info
유예 기간이 만료된 계정은 로그인 시 자동으로 처리됩니다.  
[Edge Function 배포](/guide/start/database-setup#edge-function-deploy)가 완료되어 있어야 합니다.
:::

## 탈퇴 신청 {#request}

```csharp
Task<SupabaseCallResult> Supabase.TryRequestMyWithdrawalAsync()
```

탈퇴를 예약합니다. 요청이 성공하면 즉시 로그아웃 처리됩니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

## 예약 상태 확인 {#status}

```csharp
Task<MyWithdrawalStatus> Supabase.TryGetMyWithdrawalStatusAsync()
```

현재 탈퇴 예약 상태를 조회합니다. 조회 실패 시 `null`을 반환합니다.

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.IsScheduled` | `bool` | 탈퇴 유예 예약 여부 |
| `.SecondsRemaining` | `long` | 탈퇴까지 남은 시간 (초) |
| `.WithdrawnAtIso` | `string` | 탈퇴 예약 일시 (ISO 8601) |

## 탈퇴 취소 {#clear}

```csharp
Task<SupabaseCallResult> Supabase.TryClearMyWithdrawalAsync()
```

탈퇴 예약을 취소합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
