# Google 게스트 연동 — Android

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleToCurrentAnonymousAsync()
```

익명 세션에 Android Play Services Google 계정을 연동합니다. 기존 익명 계정의 데이터가 그대로 이어집니다.

::: warning
익명 세션에서는 직접 로그인 메서드 대신 이 연동 메서드를 사용하세요. 연동은 Supabase 대시보드 **Authentication > Settings > Manual linking** 이 ON일 때 동작합니다.
:::

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.GoogleSignInCancelled` | 사용자가 계정 선택기 취소 |
| `SupabaseFailReason.GoogleSignInFailed` | Play Services 오류 |
| `SupabaseFailReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
