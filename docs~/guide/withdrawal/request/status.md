# 예약 상태 확인

```csharp
Task<SupabaseResult<MyWithdrawalStatus>> Supabase.GetMyWithdrawalStatusAsync()
```

현재 탈퇴 예약 상태를 조회합니다.

**반환**

`.Data` — 탈퇴 예약 상태 객체. 조회 실패 시 없음.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Data.IsScheduled` | `bool` | 탈퇴 유예 예약 여부 |
| `.Data.SecondsRemaining` | `long` | 탈퇴까지 남은 시간 (초) |
| `.Data.WithdrawnAtIso` | `string` | 탈퇴 예약 일시 (ISO 8601) |
