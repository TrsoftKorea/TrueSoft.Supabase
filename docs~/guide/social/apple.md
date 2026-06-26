# Apple

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

Apple은 외부 SDK(Sign in with Apple)로 발급받은 ID 토큰을 직접 전달하는 방식입니다. 플랫폼 구분 없이 동일한 메서드를 사용합니다.

## 신규 로그인

처음 로그인하거나 로그아웃 상태에서 Apple 계정으로 로그인합니다.

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleIdTokenAsync(string idToken, string rawNonce = null)
```

외부 SDK(Sign in with Apple)에서 발급받은 ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce. 일부 SDK에서 요구 (기본값: `null`) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

## 게스트(익명) → Apple 연동

익명 세션을 Apple 계정으로 전환합니다. 기존 익명 계정의 데이터가 그대로 이어집니다.

::: warning
익명 세션에서는 직접 로그인 메서드(`TrySignInWithAppleIdTokenAsync`) 대신 아래 연동 메서드를 사용하세요.  
연동은 Supabase 대시보드 **Authentication > Settings > Manual linking** 이 ON일 때 동작합니다.
:::

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(string idToken, string rawNonce = null)
```

익명 세션에 Apple 계정을 연동합니다. 외부 SDK로 발급받은 ID 토큰을 직접 전달합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce (기본값: `null`) |

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

## 이미 로그인된 계정에 추가 연동

이미 로그인된 계정(익명 포함)에 Apple 계정을 하나 더 연결합니다.

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleWithIdTokenAsync(string idToken, string rawNonce = null)
```

외부 SDK로 발급받은 ID 토큰으로 Apple 계정을 추가 연동합니다.
