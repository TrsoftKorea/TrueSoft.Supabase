# 인증

인증은 플레이어 계정을 만들고 관리하는 기능입니다.  
별도 회원가입 없이 바로 시작하는 익명 로그인과, 소셜 계정 연동을 통한 기기 간 이어하기를 지원합니다.

---

## 로그인

저장된 세션이 있으면 기존 계정으로 복원하고, 없으면 새 익명 계정을 생성해 로그인합니다.  
로그인하면 세션이 기기에 자동으로 저장되어, 다음 실행 시 `TriggerAutoLoginAsync()`로 복원할 수 있습니다.

소셜 로그인은 [소셜 로그인](#social-login)을 참고하세요.

#### `TrySignInAnonymouslyAsync()`

```csharp
Task<SupabaseCallResult> Supabase.TrySignInAnonymouslyAsync()
```

익명(게스트) 계정으로 로그인합니다. 이미 비익명 계정으로 로그인된 경우 실패합니다.

`Try*` 메서드는 `SupabaseCallResult`를 반환합니다. `if (await ...)` 패턴과 완전히 호환되며, 실패 원인을 확인할 때는 결과를 변수에 받습니다.

```csharp
var result = await Supabase.TrySignInAnonymouslyAsync();
if (!result.Success)
{
    Debug.Log(result.Reason);  // 실패 원인 ("user_banned", "http_response_null" 등)
    if (result.Reason == SupabaseFailReason.UserBanned)
        ShowBanScreen(result.BanInfo);  // 차단 정보 포함
}
```

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## 자동 로그인

씬에 `SupabaseRuntime` 컴포넌트를 배치하면 SDK가 초기화됩니다.  
로그인은 자동 실행되지 않으므로 원하는 타이밍에 직접 호출합니다.

### SupabaseRuntime

`SupabaseRuntime`은 SDK의 핵심 진입점으로, 아래 기능을 담당합니다.

| 기능 | 설명 |
|------|------|
| SDK 초기화 | `SupabaseSettings`를 읽어 모든 서비스를 초기화합니다 |
| 유저 세이브 자동 동기화 | 변경된 세이브 데이터를 쿨타임 주기로 자동 업로드합니다 ([유저 데이터](./user-data.md) 참고) |
| RemoteConfig 폴링 | 키별 백그라운드 갱신을 `Update`에서 처리합니다 |
| 앱 일시정지·종료 처리 | 앱이 일시정지(`OnApplicationPause`)되거나 종료될 때 세이브 데이터를 즉시 플러시합니다 |

```csharp
// 로그인 화면 진입 후, 또는 원하는 씬 진입 시점에 호출
await SupabaseRuntime.TriggerAutoLoginAsync();
```

### 완료 이벤트

`TriggerAutoLoginAsync()` 완료를 기다려야 하는 코드는 `OnAutoLoginCompleted` 이벤트를 사용합니다.

```csharp
void OnEnable()  => SupabaseRuntime.SubscribeAutoLoginCompleted(OnReady);
void OnDisable() => SupabaseRuntime.UnsubscribeAutoLoginCompleted(OnReady);

void OnReady(bool success)
{
    if (success)
    {
        // 자동 로그인 성공 → 유저 세이브 로드도 완료된 상태
        InitGame();
    }
    else
    {
        // 저장된 세션 없음 (첫 실행 또는 로그아웃 후) → 로그인 화면으로 이동
        ShowLoginScreen();
    }
}
```

### 로그인 후 사용 가능한 값

로그인이 성공하면 아래 프로퍼티를 바로 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `Supabase.MyProfile.DisplayName` | 닉네임. 설정 전에는 빈 문자열 |
| `Supabase.MyProfile.ServerCode` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `Supabase.MyProfile.IsWithdrawn` | 탈퇴 예약 여부 |
| `Supabase.UserId` | 플레이어 고유 ID. 재로그인·계정 연동 후에도 변하지 않음 |
| `Supabase.IsAnonymous` | 익명 로그인 여부 |

---

## 소셜 로그인 {#social-login}

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

### Google

#### 대시보드 설정

1. [Google Cloud Console](https://console.cloud.google.com/apis/dashboard)에서 프로젝트를 만들고 **OAuth 동의 화면**을 설정합니다.  
   앱 이름·이메일을 입력하고 사용자 유형은 **외부**를 선택합니다.
2. **사용자 인증 정보 > OAuth 클라이언트 ID**에서 애플리케이션 유형을 **웹 애플리케이션**으로 생성합니다.  
   승인된 리디렉션 URI에 `https://<project-id>.supabase.co/auth/v1/callback`을 추가합니다.  
   생성 후 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 복사합니다.
3. Supabase 대시보드 **Authentication > Providers > Google**에 위 두 값을 입력합니다.
4. **(Android 네이티브 로그인 사용 시)** 같은 메뉴에서 유형을 **Android**로 OAuth 클라이언트를 추가 생성합니다.  
   패키지명과 SHA-1 지문을 입력합니다.  
   웹 애플리케이션 클라이언트 ID는 `SupabaseSettings`의 `googleWebClientId` 필드에 입력합니다.

#### `TrySignInWithGoogleAsync()`

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithGoogleAsync()
```

Android에서 Play Services 계정 선택기를 표시하고, Google ID 토큰을 받아 Supabase 로그인까지 자동으로 처리합니다.

::: warning
Google이 이미 로그인된 상태에서 `TrySignInAnonymouslyAsync`를 호출하면 실패합니다.  
먼저 `TrySignOutFullyAsync`로 로그아웃하세요.
:::

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.GoogleSignInCancelled` | 사용자가 계정 선택기 취소 (뒤로가기 포함) |
| `"google_signin_failed"` | Play Services 오류 |
| `"google_id_token_empty"` | ID 토큰 획득 실패 |
| `"google_web_client_id_empty"` | `SupabaseSettings.googleWebClientId` 미설정 |
| `SupabaseFailReason.AnonymousRequiresLink` | 익명 세션에서 호출 — `TryLinkGoogleToCurrentAnonymousAsync` 사용 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

취소와 오류를 구분하는 예시:

```csharp
var result = await Supabase.TrySignInWithGoogleAsync();
if (!result)
{
    if (result.Reason == SupabaseFailReason.GoogleSignInCancelled)
        return; // 사용자가 직접 취소 — 오류 UI 불필요
    if (result.Reason == SupabaseFailReason.UserBanned)
        ShowBanScreen(result.BanInfo);
    else
        ShowErrorPopup(result.Reason);
}
```

#### `TrySignInWithGoogleIdTokenAsync(idToken)`

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithGoogleIdTokenAsync(string idToken)
```

iOS · 커스텀 OAuth 흐름에서 외부 SDK로 발급받은 Google ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AnonymousRequiresLink` | 익명 세션에서 호출 — `TryLinkGoogleToCurrentAnonymousWithIdTokenAsync` 사용 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

#### `TryLinkGoogleToCurrentAnonymousAsync()`

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleToCurrentAnonymousAsync()
```

익명 세션에 Android Play Services Google 계정을 연동합니다. 연동 성공 시 기존 익명 계정이 소셜 계정으로 전환되며, 플레이어 ID와 게임 데이터는 그대로 유지됩니다.

::: warning
익명 세션에서 직접 `TrySignInWithGoogleAsync`를 호출하면 `anonymous_session_requires_explicit_link` 오류가 반환됩니다.  
반드시 이 메서드를 사용하세요.  
Supabase 대시보드 **Authentication > Settings > Manual linking** 을 ON으로 설정해야 합니다.
:::

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.GoogleSignInCancelled` | 사용자가 계정 선택기 취소 |
| `"google_signin_failed"` | Play Services 오류 |
| `"google_web_client_id_empty"` | `SupabaseSettings.googleWebClientId` 미설정 |
| `SupabaseFailReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

#### `TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken, googleAccessToken)`

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(string idToken, string googleAccessToken = null)
```

익명 세션에 Google 계정을 연동합니다. iOS · 커스텀 OAuth 흐름에서 외부 SDK로 발급받은 ID 토큰을 직접 전달합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 |
| `googleAccessToken` | Google Access Token (기본값: null) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AnonymousRequired` | 이미 소셜 로그인 상태 — 익명 세션에서만 호출 가능 |
| `"google_id_token_empty"` | 전달된 ID 토큰이 비어있음 |
| `"anonymous_session_token_missing"` | 익명 세션 토큰 없음 — 재로그인 필요 |
| `"google_link_failed"` | Supabase identity 연동 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

### Apple

#### `TrySignInWithAppleIdTokenAsync(idToken, rawNonce)`

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleIdTokenAsync(string idToken, string rawNonce = null)
```

외부 SDK(Sign in with Apple)에서 발급받은 ID 토큰으로 Supabase에 로그인합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce. 일부 SDK에서 요구 (기본값: null) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AnonymousRequiresLink` | 익명 세션에서 호출 — `TryLinkAppleToCurrentAnonymousWithIdTokenAsync` 사용 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

#### `TryLinkAppleToCurrentAnonymousWithIdTokenAsync(idToken, rawNonce)`

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(string idToken, string rawNonce = null)
```

익명 세션에 Apple 계정을 연동합니다. 외부 SDK(Sign in with Apple)에서 발급받은 ID 토큰을 직접 전달합니다. 연동 성공 시 기존 익명 계정이 소셜 계정으로 전환됩니다.

::: warning
익명 세션에서는 `TrySignInWithAppleIdTokenAsync` 대신 반드시 이 메서드를 사용하세요.  
Supabase 대시보드 **Authentication > Settings > Manual linking** 을 ON으로 설정해야 합니다.
:::

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce (기본값: null) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.AnonymousRequired` | 익명 세션이 아닌 상태 |
| `"apple_id_token_empty"` | 전달된 ID 토큰이 비어있음 |
| `"anonymous_session_token_missing"` | 익명 세션 토큰 없음 — 재로그인 필요 |
| `"apple_link_failed"` | Supabase identity 연동 실패 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

## 로그아웃

Android Google 계정 선택기 초기화 + Supabase 세션 해제 + 익명 복구 토큰 저장을 한 번에 처리합니다.

#### `TrySignOutFullyAsync(clearStorage, deleteUserSessionRow)`

```csharp
Task<SupabaseCallResult> Supabase.TrySignOutFullyAsync(bool clearStorage = true, bool deleteUserSessionRow = true)
```

로그아웃하고 세션을 정리합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `clearStorage` | 기기에 저장된 세션 토큰 삭제 여부 (기본값: `true`) |
| `deleteUserSessionRow` | 중복 로그인 감지용 DB 행 삭제 여부 (기본값: `true`) |

#### `TryRefreshSessionAsync(refreshToken)`

```csharp
Task<SupabaseCallResult> Supabase.TryRefreshSessionAsync(string refreshToken)
```

리프레시 토큰으로 세션을 갱신합니다. SDK가 자동으로 처리하므로 직접 호출할 필요가 없습니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `refreshToken` | 갱신에 사용할 리프레시 토큰 |

---

## 익명 계정 복구

앱을 삭제했다가 재설치하거나 로그아웃 후 다시 익명 로그인을 하면, 기기 고유 지문을 이용해 이전 익명 계정을 자동으로 복구합니다.  
`TrySignInAnonymouslyAsync()` 또는 `SupabaseRuntime`의 자동 로그인 시 내부적으로 수행됩니다. 별도로 호출할 필요가 없습니다.

**복구 조건:**
- 같은 기기에서 재설치한 경우 복구됩니다.
- 기기를 변경하거나 지문이 달라진 경우 복구되지 않고 새 익명 계정이 생성됩니다.
- 소셜 계정으로 연동한 이후에는 소셜 로그인으로 복원되므로 이 기능이 필요하지 않습니다.

**복구 실패 시:** 새 익명 계정으로 로그인이 진행됩니다.  
별도 오류 이벤트는 발행되지 않습니다.

---

## 차단된 계정 처리 {#ban-handling}

Supabase 대시보드에서 계정을 차단(`banned_until` 설정)하면, 해당 계정으로 로그인 시 SDK가 자동으로 차단 정보를 가져와 `result.BanInfo`에 채웁니다.

```csharp
var result = await Supabase.TrySignInAnonymouslyAsync();

if (!result.Success && result.BanInfo != null)
{
    var info = result.BanInfo;

    // 차단 해제 일시
    if (info.IsPermanentBan)
        Debug.Log("영구 차단");
    else
        Debug.Log($"차단 해제: {info.BannedUntil:yyyy-MM-dd HH:mm}");

    // 어드민 메시지 (설정된 경우)
    if (!string.IsNullOrEmpty(info.BanMessage))
        Debug.Log($"사유: {info.BanMessage}");
}
```

`SupabaseCallResult.Reason == SupabaseFailReason.UserBanned`일 때만 `BanInfo`가 유효하며, 그 외에는 항상 `null`입니다.

::: info
`BanInfo` 조회는 `get-ban-info` Edge Function을 호출합니다. 차단 상태가 아닌 경우 `result.BanInfo`는 항상 `null`입니다.
:::

### 어드민 메시지 설정 방법

Supabase 대시보드 또는 Retool에서 차단과 함께 메시지를 설정합니다.

**1단계 — 계정 차단** (Supabase 대시보드)  
`Authentication` → `Users` → 해당 유저 선택 → `Ban user` → 차단 해제 일시 입력

**2단계 — 메시지 저장** (SQL 또는 Retool)

```sql
-- 새 메시지 등록
insert into user_ban_messages (account_id, ban_message)
values ('유저-uuid', '규칙 위반으로 인해 차단되었습니다.')
on conflict (account_id) do update set
  ban_message = excluded.ban_message,
  updated_at  = now();

-- 메시지 삭제
delete from user_ban_messages where account_id = '유저-uuid';
```

### 수동 조회

#### `TryGetBanInfoAsync(accountId)`

```csharp
Task<SupabaseBanInfo?> Supabase.TryGetBanInfoAsync(string accountId)
```

특정 계정의 차단 정보를 조회합니다. 차단 상태가 아니거나 조회 실패 시 `null`을 반환합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `accountId` | 조회할 계정 ID (`auth.users.id`) |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.IsPermanentBan` | `bool` | 영구 차단 여부 |
| `.BannedUntil` | `DateTime` | 차단 해제 일시. 영구 차단이면 의미 없음 |
| `.BanMessage` | `string` | 어드민이 설정한 차단 사유 메시지. 없으면 빈 문자열 |

```csharp
var banInfo = await Supabase.TryGetBanInfoAsync(accountId);
if (banInfo != null)
{
    // 차단 중
}
```
