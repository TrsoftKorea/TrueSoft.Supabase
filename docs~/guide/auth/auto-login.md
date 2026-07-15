# 자동 로그인

앱 재시작 시 저장된 세션으로 로그인을 복원합니다.

## 자동 로그인 호출

씬에 `SupabaseRuntime` 컴포넌트를 배치하면 SDK가 초기화됩니다.  
로그인은 자동 실행되지 않으므로 원하는 타이밍에 직접 호출합니다.

```csharp
var result = await Supabase.TriggerAutoLoginAsync();
if (result.IsSuccess)
{
    ShowNickname(result.Profile.DisplayName);   // 로그인 결과에 담긴 내 프로필
    // 자동 로그인 성공 — 데이터 로드는 별개 단계입니다.
    await PlayerSave.LoadAsync();
    InitGame();
}
else
{
    // 저장된 세션 없음 (첫 실행 또는 로그아웃 후) → 로그인 화면으로 이동
    ShowLoginScreen();
}
```

::: info 로그인과 로드는 별개 단계
`TriggerAutoLoginAsync()`는 세션만 복원하고 유저 데이터는 로드하지 않습니다. 수동 로그인과 동일하게 성공 후 [로드](/guide/user-data/load)를 직접 호출하세요.
:::

## 로그인 후 사용 가능한 값 {#after-login-values}

로그인이 성공하면 아래 프로퍼티를 바로 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `Supabase.IsLoggedIn` | 현재 로그인 여부 |
| `Supabase.UserId` | 현재 로그인 계정 ID (`auth.users.id`) |
| `Supabase.IsAnonymous` | 익명 로그인 여부 |
| `Supabase.IsLinkedWithGoogle` | Google 연동 여부 |
| `Supabase.IsLinkedWithApple` | Apple 연동 여부 |

내 프로필(닉네임·서버 코드·탈퇴 상태)은 로그인 결과(`SupabaseSignInResult`)의 `.Profile`에 담겨 옵니다. [내 프로필](/guide/display-name/profile#my-profile) 참고.
