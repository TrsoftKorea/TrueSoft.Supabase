# 예약 상태 확인

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
