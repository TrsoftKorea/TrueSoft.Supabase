# 취소 토큰 사용

```csharp
Task<SupabaseCallResult> Supabase.TryRedeemWithdrawalCancelAsync(string cancelToken = null)
```

탈퇴 취소 토큰을 사용해 탈퇴를 취소합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `cancelToken` | 플레이어가 입력한 취소 토큰 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
