# Apple 신규 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleAsync()
```

플랫폼에 맞는 Apple 로그인을 자동으로 수행합니다 — **iOS는 네이티브 Sign in with Apple, Android는 브라우저 기반 OAuth**로 분기합니다. 호출부는 플랫폼을 구분할 필요가 없습니다. [대시보드·빌드 설정](./setup)을 먼저 완료하세요.

::: tip Android 사용 시
Android도 같은 호출로 동작합니다. Supabase 대시보드 Redirect URLs에 `{패키지이름}://login-callback`만 등록하면 되고, 나머지(딥링크·매니페스트)는 자동 처리됩니다. 자세히는 [대시보드·빌드 설정](./setup)을 참고하세요.
:::

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AppleSignInCancelled` | 사용자가 로그인 창을 직접 취소 |
| `SupabaseFailReason.AppleSignInUnsupportedPlatform` | 에디터 등 미지원 환경 (iOS·Android 실기기 빌드에서 동작) |
| `SupabaseFailReason.AnonymousRequiresLink` | 익명 세션 — 연동은 [게스트 연동](./link)을 사용 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

::: info 커스텀 토큰
외부 OAuth·웹에서 받은 ID 토큰을 직접 쓰려면 [Apple 신규 로그인 · 커스텀](./signin-token)을 사용하세요.
:::
