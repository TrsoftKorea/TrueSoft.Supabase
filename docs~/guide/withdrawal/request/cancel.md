# 탈퇴 예약 취소

```csharp
Task<SupabaseResult> Supabase.ClearMyWithdrawalAsync()
```

탈퇴 예약을 취소합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
