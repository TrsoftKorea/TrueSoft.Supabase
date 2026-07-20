# Apple 게스트 연동

```csharp
Task<SupabaseResult> Supabase.LinkAppleToGuestAsync()
```

익명(게스트) 계정에 Apple 계정을 연동합니다(iOS). 기존 계정의 데이터는 그대로 이어집니다. [대시보드 설정](./setup)을 먼저 완료하세요.

```csharp
var result = await Supabase.LinkAppleToGuestAsync();
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

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInIosOnly` | iOS가 아닌 환경(에디터·Android) |
| `SupabaseFailReason.AnonymousRequired` | 익명 세션이 아닌 상태 |
| `SupabaseFailReason.AppleLinkFailed` | Apple 연동에 실패했습니다 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 게스트 연동 · 커스텀](./link-token)을 사용하세요.
:::
