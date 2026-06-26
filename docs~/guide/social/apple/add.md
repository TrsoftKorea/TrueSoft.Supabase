# Apple 추가 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleNativeAsync()
```

iOS 네이티브 Sign in with Apple로, 이미 로그인된 계정(익명 포함)에 Apple 계정을 추가 연동합니다. [대시보드·빌드 설정](./setup)을 먼저 완료하세요.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInIosOnly` | iOS가 아닌 환경(에디터·Android) |
| `SupabaseFailReason.AppleLinkFailed` | Supabase identity 연동 실패 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
외부 OAuth·웹 흐름으로 받은 ID 토큰을 직접 쓰려면 [Apple 추가 연동 · 커스텀](./add-token)을 사용하세요.
:::
