# Apple 추가 연동

```csharp
Task<SupabaseResult> Supabase.LinkAppleNativeAsync()
```

이미 로그인된 계정에 iOS에서 Apple 계정을 추가로 연동합니다. 익명 계정도 가능합니다. [대시보드 설정](./setup)을 먼저 완료하세요.

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

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseReason.AppleSignInIosOnly` | iOS가 아닌 환경. 에디터·Android 포함 |
| `SupabaseReason.AppleLinkFailed` | Apple 연동에 실패했습니다 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 추가 연동 · 커스텀](./add-token)을 사용하세요.
:::
