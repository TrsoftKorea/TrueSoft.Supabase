# Apple 게스트 연동 · 커스텀

```csharp
Task<SupabaseResult> Supabase.LinkAppleToGuestWithIdTokenAsync(string idToken, string rawNonce = null)
```

이미 가진 Apple ID 토큰으로 익명(게스트) 계정에 Apple 계정을 연동합니다. 일반적으로는 [Apple 게스트 연동](./link)을 쓰세요. 기존 계정의 데이터는 그대로 이어집니다.

```csharp
var result = await Supabase.LinkAppleToGuestWithIdTokenAsync(idToken, rawNonce);
if (result.IsSuccess)
{
    // 연동 완료 — 기존 게스트 데이터 그대로 유지
    ShowLinkedBadge();
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
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce (기본값: `null`) |

**에러 코드**

| ErrorCode | 설명 |
|--------|------|
| `SupabaseErrorCode.AnonymousRequired` | 익명 세션이 아닌 상태 |
| `SupabaseErrorCode.AppleIdTokenEmpty` | 전달된 ID 토큰이 비어있음 |
| `SupabaseErrorCode.AnonymousSessionTokenMissing` | 익명 세션 토큰 없음 — 재로그인 필요 |
| `SupabaseErrorCode.AppleLinkFailed` | Apple 연동에 실패했습니다 |
| `SupabaseErrorCode.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseErrorCode.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseErrorCode.NetworkError` | 네트워크 오류 또는 타임아웃 |
