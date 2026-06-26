# Apple 신규 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleIdTokenAsync(string idToken, string rawNonce = null)
```

외부 SDK(Sign in with Apple)에서 발급받은 ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce. 일부 SDK에서 요구 (기본값: `null`) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
