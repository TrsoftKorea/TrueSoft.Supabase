# Google 신규 로그인

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithGoogleAsync()
```

Google 계정 선택 창을 띄우고, Google ID 토큰을 받아 Supabase 로그인까지 자동으로 처리합니다. Android는 Play Services 계정 선택 창을, iOS는 기본 로그인 창을 씁니다. [Android 설정](/guide/social/google/setup#android)·[iOS 설정](/guide/social/google/setup#ios) 중 쓰는 플랫폼 항목을 먼저 완료하세요.

```csharp
var result = await Supabase.SignInWithGoogleAsync();
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

**반환**

`.Profile`은 로그인한 내 프로필입니다. 담기는 필드는 [공개 프로필](/guide/display-name/profile)을 참고하세요.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.GoogleSignInCancelled` | 플레이어가 계정 선택 창을 닫았습니다 |
| `SupabaseReason.GoogleSignInFailed` | Google 로그인 처리 중 오류가 났습니다 |
| `SupabaseReason.OAuthLoginInProgress` | iOS에서 Google 로그인이 이미 진행 중입니다. 이 호출만 실패하고 진행 중인 로그인은 그대로 이어집니다 |
| `SupabaseReason.GoogleIdTokenEmpty` | ID 토큰을 받지 못했습니다 |
| `SupabaseReason.GoogleWebClientIdEmpty` | Android에서 `googleWebClientId`가 비어 있습니다 |
| `SupabaseReason.GoogleIosClientIdInvalid` | iOS에서 `googleIosClientId`가 비었거나, 형식이 틀리거나, 웹 클라이언트 ID와 같습니다 |
| `SupabaseReason.AnonymousRequiresLink` | 익명 세션입니다. 연동은 [게스트 연동](/guide/social/google/link)을 쓰세요 |
| `SupabaseReason.UserBanned` | 차단된 계정입니다. 자세한 내용은 `result.BanInfo`에 있습니다 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정이라 새 계정으로 다시 가입되었습니다 |
