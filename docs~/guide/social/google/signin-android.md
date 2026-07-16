# Google 신규 로그인 · Android

```csharp
Task<SupabaseSignInResult> Supabase.SignInWithGoogleAsync()
```

Play Services 계정 선택기를 표시하고, Google ID 토큰을 받아 Supabase 로그인까지 자동으로 처리합니다. [대시보드 설정](./setup)의 Android 항목이 선행되어야 합니다. 성공 시 `result.Profile`에 내 프로필(닉네임·서버 코드 등)이 담깁니다.

```csharp
var result = await Supabase.SignInWithGoogleAsync();
if (result.IsSuccess)
{
    ShowNickname(result.Profile.DisplayName);   // 로그인 결과에 담긴 내 프로필
    await PlayerSave.LoadAsync();   // 로그인 성공 — 데이터 로드
    InitGame();
}
else
{
    ShowLoginError(result.Reason);
}
```

**반환**

`.Profile` — 로그인한 내 프로필(`PublicProfile` — 닉네임·서버 코드 등). 자세한 필드는 [`PublicProfile` 필드](/guide/display-name/profile) 참고.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.GoogleSignInCancelled` | 사용자가 계정 선택기 취소 (뒤로가기 포함) |
| `SupabaseFailReason.GoogleSignInFailed` | Play Services 오류 |
| `SupabaseFailReason.GoogleIdTokenEmpty` | ID 토큰 획득 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
