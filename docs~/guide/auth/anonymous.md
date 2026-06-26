# 익명 로그인

별도 회원가입 없이 바로 시작하는 게스트 로그인입니다.  
저장된 세션이 있으면 기존 계정으로 복원하고, 없으면 새 익명 계정을 생성해 로그인합니다. 로그인하면 세션이 기기에 자동으로 저장되어, 다음 실행 시 `TriggerAutoLoginAsync()`로 복원할 수 있습니다.

소셜 로그인은 [소셜 로그인](/guide/social/google)을 참고하세요.

```csharp
Task<SupabaseCallResult> Supabase.TrySignInAnonymouslyAsync()
```

익명(게스트) 계정으로 로그인합니다. 이미 비익명 계정으로 로그인된 경우 실패합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

로그인 후 유저 데이터를 쓰려면 [데이터 로드](/guide/user-data/load)를 호출합니다. 자동 로그인은 이 로드를 내부에서 함께 처리합니다.
