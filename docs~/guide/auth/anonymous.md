# 익명 로그인

```csharp
Task<SupabaseSignInResult> Supabase.SignInAnonymouslyAsync()
```

별도 회원가입 없이 게스트 계정으로 로그인합니다. 이미 비익명 계정으로 로그인된 경우 실패합니다. 저장된 세션이 있으면 기존 계정으로 복원하고, 없으면 새 익명 계정을 생성합니다. 로그인하면 세션이 기기에 자동 저장되어 다음 실행 시 `Supabase.TriggerAutoLoginAsync()`로 복원할 수 있습니다. 성공 시 `result.Profile`에 내 프로필(닉네임·서버 코드 등)이 담깁니다.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();
if (result.IsSuccess)
{
    ShowNickname(result.Profile.Name);   // 로그인 결과에 담긴 내 프로필
    await PlayerSave.LoadAsync();               // 데이터 로드는 별개 단계
    InitGame();
}
else
{
    // 실패 — result.Reason으로 분기 (아래 표 참고)
    ShowLoginError(result.Reason);
}
```

**반환**

`.Profile` — 로그인한 내 프로필(`PublicProfile` — 닉네임·서버 코드 등). 자세한 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

소셜 로그인은 [소셜 로그인](/guide/social/google/)을, 로그인 후 데이터 로드는 [데이터 로드](/guide/user-data/load)를 참고하세요. 자동 로그인도 로그인만 수행하므로 로드는 별도로 호출합니다.
