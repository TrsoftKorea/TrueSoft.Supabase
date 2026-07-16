# Apple 신규 로그인 · 커스텀

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithAppleIdTokenAsync(string idToken, string rawNonce = null)
```

이미 가진 Apple ID 토큰으로 로그인합니다. 토큰을 직접 넘겨야 할 때만 쓰고, 일반적으로는 [Apple 신규 로그인](./signin)을 쓰세요. 성공 시 `result.Profile`에 내 프로필(닉네임·서버 코드 등)이 담깁니다.

```csharp
var result = await Supabase.SignInWithAppleIdTokenAsync(idToken, rawNonce);
if (result.IsSuccess)
{
    ShowNickname(result.Profile.DisplayName);   // 로그인 결과에 담긴 내 프로필
    await PlayerSave.LoadAsync();   // 로그인 성공 — 데이터 로드는 별개 단계
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
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce. 일부 SDK에서 요구 (기본값: `null`) |

**반환**

`.Profile` — 로그인한 내 프로필(`PublicProfile` — 닉네임·서버 코드 등). 자세한 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
