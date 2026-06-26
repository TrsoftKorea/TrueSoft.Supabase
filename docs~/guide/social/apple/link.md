# Apple 게스트 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleToCurrentAnonymousAsync()
```

iOS 네이티브 Sign in with Apple로 익명 세션에 Apple 계정을 연동합니다. 기존 익명 계정의 데이터는 그대로 이어집니다. [대시보드·빌드 설정](./setup)을 먼저 완료하세요.

::: warning
익명 세션에서만 호출하세요. 연동은 Supabase 대시보드 **Authentication > Settings > Manual linking** 이 ON일 때 동작합니다.
:::

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInIosOnly` | iOS가 아닌 환경(에디터·Android) |
| `SupabaseFailReason.AnonymousRequired` | 익명 세션이 아닌 상태 |
| `SupabaseFailReason.AppleLinkFailed` | Supabase identity 연동 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
외부 OAuth·웹 흐름으로 받은 ID 토큰을 직접 쓰려면 [Apple 게스트 연동 · 커스텀](./link-token)을 사용하세요.
:::
