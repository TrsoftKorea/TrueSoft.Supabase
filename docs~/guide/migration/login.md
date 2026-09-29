# 로그인

## 로그인 호출

로그인 API는 이관 전·중·후 동일합니다. 자세한 내용은 [인증](/guide/auth/anonymous)을 참고하세요.

Android에서 Apple 로그인은 플레이나누 WebView로 토큰을 받아 Supabase 로그인까지 이어지며, 다른 로그인과 같은 결과를 돌려줍니다.

```csharp
// Apple (Android) — 플레이나누 WebView로 토큰 획득 후 로그인
var result = await TrueBaseNanoo.StartAppleSignInAndroid();
if (result.IsSuccess)
{
    await Supabase.LoadUserSaveAsync();
    InitGame();
}
else if (result.Reason == SupabaseReason.AppleSignInCancelled)
{
    HideLoading();   // 창을 닫거나 뒤로가기로 나감
}
else
{
    ShowLoginError(result.Reason);
}
```

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AppleSignInCancelled` | 창을 닫거나 뒤로가기로 나감. 창이 닫힌 뒤 약 2초 안에 옵니다 |
| `SupabaseReason.OAuthLoginInProgress` | 창이 뜨는 중이거나 로그인 처리 중에 다시 누름. 새 창은 열리지 않습니다 |
| `SupabaseReason.AppleSignInUnsupportedPlatform` | Android 기기가 아님. 에디터·iOS에서는 바로 이 사유로 끝납니다 |
| `SupabaseReason.AnonymousRequiresLink` | 게스트 세션에서 호출함. 플레이나누 Android Apple 로그인은 게스트 연동을 지원하지 않으니 로그아웃 후 호출합니다 |
| `SupabaseReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: warning 창이 닫힌 것은 SDK가 짐작합니다
플레이나누 창은 로그인에 성공했을 때만 알려 주고, 닫기·뒤로가기·애플 쪽 실패는 알려 주지 않습니다. SDK는 창이 닫혀 게임 화면으로 돌아온 뒤 토큰이 오지 않으면 취소로 봅니다. 이 신호를 주지 않는 기기에서는 결과가 오지 않을 수 있으니, 로딩 화면에 닫기 버튼을 두는 것을 권장합니다. 닫은 뒤 다시 누르면 남아 있던 요청은 취소로 끝나고 새 창이 열립니다. 이때 앞 호출이 받는 취소 결과는 무시하고, 마지막 호출의 결과만 처리하세요.
:::

플레이나누 로그인이 성공하면 아래 프로퍼티를 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `TrueBaseNanoo.UserId` | 플레이나누 uuid. 로그인 전에는 null |
| `TrueBaseNanoo.OpenId` | 플레이나누 openid. SDK가 반환하지 않으면 null |

## 자동 로그인

`Supabase.TriggerAutoLoginAsync()`는 플레이나누 런타임이 있을 때 두 세션을 모두 복원합니다.

1. Supabase 리프레시 토큰으로 세션 복원
2. 저장된 플레이나누 액세스 토큰으로 `TokenSignIn` 호출
3. 둘 다 성공하면 `true` 반환

저장된 플레이나누 토큰이 없거나 복원에 실패하면 Supabase 세션까지 로그아웃한 뒤 `false`를 반환합니다. 두 세션이 항상 동시에 유효하도록 보장하며, 이 경우 게임은 자동 로그인 실패로 받아 명시 로그인으로 유도합니다.

`SignOutFullyAsync()`는 Supabase와 플레이나누 액세스 토큰을 모두 삭제합니다. 플레이나누 액세스 토큰 유효기간은 24시간이라, 그 이후에는 자동 로그인이 만료되어 플레이어가 직접 로그인합니다.
