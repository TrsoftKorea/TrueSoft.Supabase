# 자동 로그인

앱 재시작 시 저장된 세션으로 로그인을 복원합니다.

## 자동 로그인 호출

```csharp
Task<SupabaseSignInResult> Supabase.TriggerAutoLoginAsync()
```

씬에 `SupabaseRuntime` 컴포넌트를 배치하면 SDK가 초기화됩니다. 로그인은 자동 실행되지 않으므로 원하는 타이밍에 직접 호출합니다. 성공 시 `result.Profile`에 내 프로필(닉네임·서버 코드 등)이 담깁니다.

```csharp
var result = await Supabase.TriggerAutoLoginAsync();
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);   // 로그인 결과에 담긴 내 프로필
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

**반환**

`.Profile` — 로그인한 내 프로필(`PublicProfile` — 닉네임·서버 코드 등). 자세한 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

::: info 로그인과 로드는 별개 단계
`TriggerAutoLoginAsync()`는 세션만 복원하고 유저 데이터는 로드하지 않습니다. 수동 로그인과 동일하게 성공 후 [로드](/guide/user-data/load)를 직접 호출하세요.
:::
