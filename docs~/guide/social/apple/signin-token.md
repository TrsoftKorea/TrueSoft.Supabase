# Apple 신규 로그인 · 커스텀

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithAppleIdTokenAsync(
    string idToken,
    string rawNonce          = null,
    string authorizationCode = null)
```

이미 가진 Apple ID 토큰으로 로그인합니다. 토큰을 직접 넘겨야 할 때만 쓰고, 일반적으로는 [Apple 신규 로그인](/guide/social/apple/signin)을 쓰세요. 성공 시 `result.Profile`에 내 프로필이 담깁니다.

```csharp
var result = await Supabase.SignInWithAppleIdTokenAsync(idToken, rawNonce, authorizationCode);
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);   // 로그인 결과에 담긴 내 프로필
    await Supabase.LoadUserSaveAsync();   // 로그인 성공 — 데이터 로드는 별개 단계
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
| `authorizationCode` | 토큰과 함께 받은 일회용 인증 코드. 넘겨야 탈퇴할 때 Apple 연결이 끊깁니다. [탈퇴 시 Apple 연결 해제](/guide/social/apple/#revoke)를 참고하세요 (기본값: `null`) |

**반환**

`.Profile`은 로그인한 내 프로필입니다. 담기는 필드는 [공개 프로필](/guide/display-name/profile)을 참고하세요.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AnonymousRequiresLink` | 익명 세션입니다. 연동은 [게스트 연동](/guide/social/apple/link)을 쓰세요 |
| `SupabaseReason.UserBanned` | 차단된 계정입니다. 자세한 내용은 `result.BanInfo`에 있습니다 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정입니다 |
