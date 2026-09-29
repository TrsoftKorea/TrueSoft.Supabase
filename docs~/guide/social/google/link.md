# Google 게스트 연동

```csharp
Task<SupabaseResult> Supabase.LinkGoogleToGuestAsync()
```

익명 세션에 Google 계정을 연동합니다. [신규 로그인](./signin)과 같은 계정 선택 창을 Android·iOS에서 띄웁니다. 기존 익명 계정의 데이터가 그대로 이어집니다.

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
| `SupabaseReason.GoogleSignInCancelled` | 사용자가 계정 선택 창을 닫음 |
| `SupabaseReason.GoogleSignInFailed` | Google 로그인 처리 오류 |
| `SupabaseReason.OAuthLoginInProgress` | iOS · Google 로그인이 이미 진행 중. 이 호출만 실패하고 진행 중인 로그인은 그대로 이어짐 |
| `SupabaseReason.GoogleWebClientIdEmpty` | Android · `googleWebClientId`가 비어 있음 |
| `SupabaseReason.GoogleIosClientIdInvalid` | iOS · `googleIosClientId`가 비었거나, 형식이 틀리거나, 웹 클라이언트 ID와 같음 |
| `SupabaseReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `SupabaseReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
