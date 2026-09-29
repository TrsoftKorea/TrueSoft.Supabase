# Google 신규 로그인 · 커스텀

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithGoogleIdTokenAsync(string idToken)
```

다른 SDK나 직접 만든 OAuth 흐름으로 이미 받은 Google ID 토큰으로 Supabase에 로그인합니다. 계정 선택 창을 SDK가 띄우게 하려면 [신규 로그인](./signin)을 쓰세요. 성공 시 `result.Profile`에 내 프로필이 담깁니다.

```csharp
var result = await Supabase.SignInWithGoogleIdTokenAsync(idToken);
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);   // 로그인 결과에 담긴 내 프로필
    await Supabase.LoadUserSaveAsync();   // 로그인 성공 — 데이터 로드
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

`.Profile` — 로그인한 내 프로필입니다. 담기는 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AnonymousRequiresLink` | 익명 세션 — 연동은 [게스트 연동](./link-token)을 사용 |
| `SupabaseReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
