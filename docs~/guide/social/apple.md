# Apple

## 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleIdTokenAsync(string idToken, string rawNonce = null)
```

외부 SDK(Sign in with Apple)에서 발급받은 ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 | `string` |
| `rawNonce` | 토큰과 함께 전달된 nonce. 일부 SDK에서 요구 (기본값: `null`) | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## 게스트 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(string idToken, string rawNonce = null)
```

익명 세션에 Apple 계정을 연동합니다. 외부 SDK(Sign in with Apple)에서 발급받은 ID 토큰을 직접 전달합니다.

익명 세션에서는 직접 로그인(`TrySignInWithAppleIdTokenAsync`) 대신 이 메서드로 연동합니다.  
연동은 Supabase 대시보드 **Authentication > Settings > Manual linking** 이 ON일 때 동작합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 | `string` |
| `rawNonce` | 토큰과 함께 전달된 nonce (기본값: `null`) | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AnonymousRequired` | 익명 세션이 아닌 상태 |
| `SupabaseFailReason.AppleIdTokenEmpty` | 전달된 ID 토큰이 비어있음 |
| `SupabaseFailReason.AnonymousSessionTokenMissing` | 익명 세션 토큰 없음 — 재로그인 필요 |
| `SupabaseFailReason.AppleLinkFailed` | Supabase identity 연동 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## 소셜 계정에 Apple 추가 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleWithIdTokenAsync(string idToken, string rawNonce = null)
```

이미 로그인된 계정(익명 포함)에 Apple 계정을 추가 연동합니다.
