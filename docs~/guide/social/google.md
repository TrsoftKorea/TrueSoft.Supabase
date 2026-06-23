# Google

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

---

## 대시보드 설정

1. [Google Cloud Console](https://console.cloud.google.com/apis/dashboard)에서 프로젝트를 만들고 **OAuth 동의 화면**을 설정합니다.  
   앱 이름·이메일을 입력하고 사용자 유형은 **외부**를 선택합니다.
2. **사용자 인증 정보 > OAuth 클라이언트 ID**에서 애플리케이션 유형을 **웹 애플리케이션**으로 생성합니다.  
   승인된 리디렉션 URI에 `https://<project-id>.supabase.co/auth/v1/callback`을 추가합니다.  
   생성 후 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 복사합니다.
3. Supabase 대시보드 **Authentication > Providers > Google**에 위 두 값을 입력합니다.
4. **(Android 네이티브 로그인 사용 시)** 같은 메뉴에서 유형을 **Android**로 OAuth 클라이언트를 추가 생성합니다.  
   패키지명과 SHA-1 지문을 입력합니다.  
   웹 애플리케이션 클라이언트 ID는 `SupabaseSettings`의 `googleWebClientId` 필드에 입력합니다.

---

## Android 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithGoogleAsync()
```

Android에서 Play Services 계정 선택기를 표시하고, Google ID 토큰을 받아 Supabase 로그인까지 자동으로 처리합니다.

Google 로그인 상태에서 익명 로그인으로 전환하려면 먼저 `TrySignOutFullyAsync`로 로그아웃한 뒤 `TrySignInAnonymouslyAsync`를 호출합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.GoogleSignInCancelled` | 사용자가 계정 선택기 취소 (뒤로가기 포함) |
| `SupabaseFailReason.GoogleSignInFailed` | Play Services 오류 |
| `SupabaseFailReason.GoogleIdTokenEmpty` | ID 토큰 획득 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## iOS · 커스텀 로그인

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithGoogleIdTokenAsync(string idToken)
```

iOS 또는 커스텀 OAuth 흐름에서 외부 SDK로 발급받은 Google ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## Android — 게스트 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleToCurrentAnonymousAsync()
```

익명 세션에 Android Play Services Google 계정을 연동합니다. 연동 성공 시 기존 익명 계정이 소셜 계정으로 전환됩니다.

익명 세션에서는 직접 로그인(`TrySignInWithGoogleAsync`) 대신 이 메서드로 연동합니다. 직접 호출하면 `anonymous_session_requires_explicit_link` 오류가 반환됩니다.  
연동은 Supabase 대시보드 **Authentication > Settings > Manual linking** 이 ON일 때 동작합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.GoogleSignInCancelled` | 사용자가 계정 선택기 취소 |
| `SupabaseFailReason.GoogleSignInFailed` | Play Services 오류 |
| `SupabaseFailReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## iOS · 커스텀 — 게스트 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(string idToken, string googleAccessToken = null)
```

익명 세션에 Google 계정을 연동합니다. iOS 또는 커스텀 OAuth 흐름에서 외부 SDK로 발급받은 ID 토큰을 직접 전달합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 | `string` |
| `googleAccessToken` | Google Access Token (기본값: `null`) | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `SupabaseFailReason.GoogleIdTokenEmpty` | 전달된 ID 토큰이 비어있음 |
| `SupabaseFailReason.AnonymousSessionTokenMissing` | 익명 세션 토큰 없음 — 재로그인 필요 |
| `SupabaseFailReason.GoogleLinkFailed` | Supabase identity 연동 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## Android — 소셜 계정에 Google 추가 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleNativeAsync()
```

이미 로그인된 계정(익명 포함)에 Android Play Services Google 계정을 추가 연동합니다.

---

## iOS · 커스텀 — 소셜 계정에 Google 추가 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleWithIdTokenAsync(string idToken, string googleAccessToken = null)
```

이미 로그인된 계정(익명 포함)에 Google 계정을 추가 연동합니다.
