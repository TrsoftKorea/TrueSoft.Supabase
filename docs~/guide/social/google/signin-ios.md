# Google 신규 로그인 · iOS

```csharp
Task<SupabaseResult> Supabase.SignInWithGoogleIdTokenAsync(string idToken)
```

iOS 또는 커스텀 OAuth 흐름에서 외부 SDK로 발급받은 Google ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
