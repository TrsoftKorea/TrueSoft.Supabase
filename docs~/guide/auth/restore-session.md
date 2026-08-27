# 세션 복원

```csharp
Task<SupabaseSignInResult> Supabase.RestoreSessionAsync()
```

저장된 refresh token으로 세션만 복원합니다. [자동 로그인](./auto-login)과 달리 자동 로그인 차단 여부를 확인하지 않고, `SupabaseRuntime`의 후처리 훅도 거치지 않습니다.

```csharp
var result = await Supabase.RestoreSessionAsync();
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);
    await Supabase.LoadUserSaveAsync();
    InitGame();
}
else
{
    ShowLoginScreen();
}
```

**반환**

`.Profile` — 복원한 계정의 프로필. 자세한 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.RestoreSessionFailed` | 저장된 토큰이 없거나 복원에 실패했습니다 |

::: info
세션만 복원하므로 유저 데이터는 따로 로드해야 합니다. 로그인과 로드가 별개 단계인 이유는 [로그인과 로드는 별개 단계](./auto-login#login-load-separate)를 참고하세요.
:::
