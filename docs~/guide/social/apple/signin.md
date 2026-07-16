# Apple 신규 로그인

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithAppleAsync()
```

iOS·Android에서 Apple 로그인을 수행합니다. 플랫폼에 맞는 방식이 자동으로 선택됩니다. [대시보드 설정](./setup)을 먼저 완료하세요. 성공 시 `result.Profile`에 내 프로필(닉네임·서버 코드 등)이 담깁니다.

```csharp
var result = await Supabase.SignInWithAppleAsync();
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

::: tip Android 사용 시
Android도 같은 호출로 동작합니다. Supabase 대시보드 Redirect URLs에 `{패키지이름}://login-callback`만 등록하면 되고, 나머지는 자동 처리됩니다. 자세히는 [대시보드 설정](./setup)을 참고하세요.
:::

**반환**

`.Profile` — 로그인한 내 프로필(`PublicProfile` — 닉네임·서버 코드 등). 자세한 필드는 [내 프로필](/guide/display-name/profile) 참고.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInUnsupportedPlatform` | 에디터 등 미지원 환경 (iOS·Android 실기기 빌드에서 동작) |
| `SupabaseFailReason.AnonymousRequiresLink` | 익명 세션 — 연동은 [게스트 연동](./link)을 사용 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 신규 로그인 · 커스텀](./signin-token)을 사용하세요.
:::
