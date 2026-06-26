# 탈퇴 신청

```csharp
Task<SupabaseCallResult> Supabase.TryRequestMyWithdrawalAsync()
```

탈퇴를 예약합니다. 요청이 성공하면 즉시 로그아웃 처리됩니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
