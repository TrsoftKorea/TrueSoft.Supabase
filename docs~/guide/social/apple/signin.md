# Apple 신규 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleAsync()
```

iOS 네이티브 Sign in with Apple 화면을 띄워 Supabase에 로그인합니다. 외부 SDK·토큰 발급 없이 호출 한 번으로 동작합니다. [대시보드·빌드 설정](./setup)을 먼저 완료하세요.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInIosOnly` | iOS가 아닌 환경(에디터·Android) |
| `SupabaseFailReason.AnonymousRequiresLink` | 익명 세션 — 연동은 [게스트 연동](./link)을 사용 |
| `SupabaseFailReason.AppleIdTokenEmpty` | ID 토큰을 받지 못함 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
외부 OAuth·웹 흐름으로 받은 ID 토큰을 직접 쓰려면 [Apple 신규 로그인 · 커스텀](./signin-token)을 사용하세요.
:::
