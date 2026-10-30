# Google 게스트 연동

```csharp
Task<SupabaseResult> Supabase.LinkGoogleToGuestAsync()
```

익명 세션에 Google 계정을 연동합니다. [신규 로그인](/guide/social/google/signin)과 같은 계정 선택 창을 Android·iOS에서 띄웁니다. 기존 익명 계정의 데이터가 그대로 이어집니다.

```csharp
var result = await Supabase.LinkGoogleToGuestAsync();
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
익명 세션에서는 직접 로그인 메서드 대신 이 연동 메서드를 사용하세요. 연동은 Supabase 대시보드 **Authentication > Sign In / Providers**의 Manual Linking이 켜져 있을 때 동작합니다.
:::

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.GoogleSignInCancelled` | 플레이어가 계정 선택 창을 닫았습니다 |
| `SupabaseReason.GoogleSignInFailed` | Google 로그인 처리 중 오류가 났습니다 |
| `SupabaseReason.OAuthLoginInProgress` | iOS에서 Google 로그인이 이미 진행 중입니다. 이 호출만 실패하고 진행 중인 로그인은 그대로 이어집니다 |
| `SupabaseReason.GoogleWebClientIdEmpty` | Android에서 `googleWebClientId`가 비어 있습니다 |
| `SupabaseReason.GoogleIosClientIdInvalid` | iOS에서 `googleIosClientId`가 비었거나, 형식이 틀리거나, 웹 클라이언트 ID와 같습니다 |
| `SupabaseReason.AnonymousRequired` | 이미 소셜 로그인 상태입니다. 익명 세션에서만 부를 수 있습니다 |
| `SupabaseReason.UserBanned` | 차단된 계정입니다. 자세한 내용은 `result.BanInfo`에 있습니다 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정입니다 |
