# Google 신규 로그인 · iOS

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithGoogleIdTokenAsync(string idToken)
```

iOS 또는 커스텀 OAuth 흐름에서 외부 SDK로 발급받은 Google ID 토큰으로 Supabase에 로그인합니다. 성공 시 `result.Profile`에 내 프로필(닉네임·서버 코드 등)이 담깁니다.

```csharp
var result = await Supabase.SignInWithGoogleIdTokenAsync(idToken);
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);   // 로그인 결과에 담긴 내 프로필
    await PlayerSave.LoadAsync();   // 로그인 성공 — 데이터 로드
    InitGame();
}
else
{
    ShowLoginError(result.Reason);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 |

**반환**

`.Profile` — 로그인한 내 프로필(`PublicProfile` — 닉네임·서버 코드 등). 자세한 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseErrorCode.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseErrorCode.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseErrorCode.NetworkError` | 네트워크 오류 또는 타임아웃 |
