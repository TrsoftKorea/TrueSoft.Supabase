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

::: info 유저 데이터는 따로 로드합니다
세션만 복원하므로, 수동 로그인과 동일하게 성공 후 [로드](/guide/user-data/load)를 직접 호출하세요.
:::
