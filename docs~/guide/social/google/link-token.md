# Google 게스트 연동 · iOS

```csharp
Task<SupabaseResult> Supabase.LinkGoogleToGuestWithIdTokenAsync(string idToken, string googleAccessToken = null)
```

익명 세션에 Google 계정을 연동합니다. 외부 SDK로 발급받은 ID 토큰을 직접 전달하며, 기존 익명 계정의 데이터가 그대로 이어집니다.

```csharp
var result = await Supabase.LinkGoogleToGuestWithIdTokenAsync(idToken);
if (result.IsSuccess)
{
    // 연동 완료 — 기존 익명 계정 데이터 유지
}
else
{
    ShowLinkError(result.Reason);
}
```

::: warning
익명 세션에서만 호출하세요. 연동은 Supabase 대시보드 **Authentication > Sign In / Providers**의 Manual Linking이 켜져 있을 때 동작합니다.
:::

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 |
| `googleAccessToken` | Google Access Token (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `SupabaseReason.GoogleIdTokenEmpty` | 전달된 ID 토큰이 비어있음 |
| `SupabaseReason.AnonymousSessionTokenMissing` | 익명 세션 토큰 없음 — 재로그인 필요 |
| `SupabaseReason.GoogleLinkFailed` | Supabase identity 연동 실패 |
| `SupabaseReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
