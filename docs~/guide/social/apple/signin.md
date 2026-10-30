# Apple 신규 로그인

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithAppleAsync()
```

iOS·Android에서 Apple 로그인을 수행합니다. 플랫폼에 맞는 방식이 자동으로 선택됩니다. [대시보드 설정](/guide/social/apple/setup)을 먼저 완료하세요.

```csharp
var result = await Supabase.SignInWithAppleAsync();
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

::: tip Android 사용 시
Android도 같은 호출로 동작합니다. Supabase 대시보드 Redirect URLs에 `{패키지이름}://login-callback`만 등록하면 되고, 나머지는 자동 처리됩니다. 자세히는 [대시보드 설정](/guide/social/apple/setup)을 참고하세요.

플레이나누 런타임이 있으면 Android에서는 브라우저 대신 플레이나누 창이 열립니다. [플레이나누 이관 중 로그인](/guide/migration/login#apple-android)을 참고하세요.
:::

**반환**

`.Profile`은 로그인한 내 프로필입니다. 담기는 필드는 [공개 프로필](/guide/display-name/profile)을 참고하세요.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AppleSignInCancelled` | 플레이어가 로그인 창을 직접 닫았습니다 |
| `SupabaseReason.AppleSignInUnsupportedPlatform` | 에디터처럼 지원하지 않는 환경입니다. iOS·Android 실기기 빌드에서 동작합니다 |
| `SupabaseReason.AnonymousRequiresLink` | 익명 세션입니다. 연동은 [게스트 연동](/guide/social/apple/link)을 쓰세요 |
| `SupabaseReason.UserBanned` | 차단된 계정입니다. 자세한 내용은 `result.BanInfo`에 있습니다 |

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 신규 로그인 · 커스텀](/guide/social/apple/signin-token)을 사용하세요.
:::
