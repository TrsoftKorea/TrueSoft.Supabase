# 익명 로그인

```csharp
Task<SupabaseSignInResult> Supabase.SignInAnonymouslyAsync()
```

별도 회원가입 없이 게스트 계정으로 로그인합니다. 저장된 세션이 있으면 기존 계정으로 복원하고, 없으면 새 익명 계정을 생성합니다. 이미 비익명 계정으로 로그인된 경우 실패합니다.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);   // 로그인 결과에 담긴 내 프로필
    await Supabase.LoadUserSaveAsync();               // 데이터 로드는 별개 단계
    InitGame();
}
else
{
    // 실패 — result.Reason으로 분기 (아래 표 참고)
    ShowLoginError(result.Reason);
}
```

**반환**

`.Profile`은 로그인한 내 프로필입니다. 담기는 필드는 [공개 프로필](/guide/display-name/profile)을 참고하세요.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserBanned` | 차단된 계정입니다. 자세한 내용은 `result.BanInfo`에 있습니다 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정이라 새 계정으로 다시 가입되었습니다 |

로그인하면 세션이 기기에 저장되어 다음 실행 때 `Supabase.TriggerAutoLoginAsync()`로 복원할 수 있습니다. 소셜 로그인은 [소셜 로그인](/guide/social/google/)을 참고하세요. 로그인과 데이터 로드가 별개 단계인 이유는 [로그인과 로드는 별개 단계](/guide/auth/auto-login#login-load-separate)를 참고하세요.
