# 익명 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInAnonymouslyAsync()
```

별도 회원가입 없이 게스트 계정으로 로그인합니다. 이미 비익명 계정으로 로그인된 경우 실패합니다. 저장된 세션이 있으면 기존 계정으로 복원하고, 없으면 새 익명 계정을 생성합니다. 로그인하면 세션이 기기에 자동 저장되어 다음 실행 시 `TriggerAutoLoginAsync()`로 복원할 수 있습니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

소셜 로그인은 [소셜 로그인](/guide/social/google)을, 로그인 후 데이터 로드는 [데이터 로드](/guide/user-data/load)를 참고하세요. 자동 로그인은 이 로드를 내부에서 함께 처리합니다.
