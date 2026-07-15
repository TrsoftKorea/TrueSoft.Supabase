# Apple 추가 연동

```csharp
Task<SupabaseResult> Supabase.LinkAppleNativeAsync()
```

이미 로그인된 계정(익명 포함)에 Apple 계정을 추가로 연동합니다(iOS). [대시보드 설정](./setup)을 먼저 완료하세요.

```csharp
var result = await Supabase.LinkAppleNativeAsync();
if (result.IsSuccess)
{
    // 연동 완료 — 현재 계정에 Apple 계정 추가
    ShowLinkedBadge();
}
else
{
    ShowLinkError(result.Reason);
}
```

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInIosOnly` | iOS가 아닌 환경(에디터·Android) |
| `SupabaseFailReason.AppleLinkFailed` | Apple 연동에 실패했습니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 추가 연동 · 커스텀](./add-token)을 사용하세요.
:::
