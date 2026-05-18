# 인증 (Auth)

## 로그인

```csharp
// 익명 로그인 — 계정 생성 없이 바로 시작
await Supabase.TrySignInAnonymouslyAsync();
```

소셜 로그인 코드는 [소셜 로그인 (선택)](#소셜-로그인-선택) 섹션을 참고하세요.

---

## 로그아웃

```csharp
await Supabase.TrySignOutFullyAsync();
```

Android Google 계정 선택기 초기화 + Supabase 세션 해제 + 익명 복구 토큰 저장을 한 번에 처리합니다.

---

## 자동 로그인

씬에 `SupabaseRuntime` 컴포넌트를 배치하면 자동 로그인이 활성화됩니다.  
Inspector의 **즉시 자동 로그인** 필드가 실행 타이밍을 결정합니다.

### SupabaseRuntime

`SupabaseRuntime`은 SDK의 핵심 진입점으로, 아래 기능을 담당합니다.

| 기능 | 설명 |
|------|------|
| SDK 초기화 | `SupabaseSettings`를 읽어 모든 서비스를 초기화합니다 |
| 자동 로그인 | Inspector의 **즉시 자동 로그인**이 ON이면 `Awake` 시 자동 실행, OFF이면 `TriggerAutoLoginAsync()`로 수동 호출 |
| 유저 세이브 자동 동기화 | 변경된 세이브 데이터를 쿨타임 주기로 자동 업로드합니다 ([유저 세이브](./user-saves.md) 참고) |
| RemoteConfig 폴링 | 키별 백그라운드 갱신을 `Update`에서 처리합니다 |
| 앱 일시정지·종료 처리 | 포커스를 잃을 때 세이브 데이터를 즉시 플러시합니다 |

### 즉시 자동 로그인 ON (기본값)

`Awake` 시점에 저장된 세션으로 자동 로그인합니다. 별도 코드가 필요 없습니다.

### 즉시 자동 로그인 OFF

원하는 타이밍에 직접 호출합니다.

```csharp
// 로그인 화면 진입 후, 또는 원하는 씬 진입 시점에 호출
await SupabaseRuntime.TriggerAutoLoginAsync();
```

### 완료 이벤트

자동 로그인 완료를 기다려야 하는 코드는 `OnAutoLoginCompleted` 이벤트를 사용합니다.  
**즉시 자동 로그인 ON·OFF 모두** 이 이벤트가 발행됩니다.

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

---

## 익명 계정 복구

앱을 삭제했다가 재설치하거나 로그아웃 후 다시 익명 로그인을 하면, 기기 고유 지문을 이용해 이전 익명 계정을 자동으로 복구합니다.

**동작 시점:** `TrySignInAnonymouslyAsync()` 또는 `SupabaseRuntime`의 자동 로그인 시 내부적으로 수행됩니다.  
별도로 호출할 필요가 없습니다.

**복구 조건 및 한계:**
- 같은 기기에서 재설치한 경우 복구됩니다.
- 기기를 변경하거나 지문이 달라진 경우 복구되지 않고 새 익명 계정이 생성됩니다.
- 소셜 계정으로 연동한 이후에는 소셜 로그인으로 복원되므로 이 기능이 필요하지 않습니다.

**복구 실패 시:** 새 익명 계정으로 로그인이 진행됩니다.  
별도 오류 이벤트는 발행되지 않습니다.

관련 SQL: `Sql/player/03_anonymous_recovery.sql`

---

## 소셜 로그인 (선택)

> [!TIP]
> 소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.  
> 각 Provider는 독립적으로 추가할 수 있습니다.

---

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

#### 로그인

```csharp
// Android 네이티브 (Play Services)
await Supabase.TrySignInWithGoogleAsync();

// iOS · 커스텀 OAuth (ID 토큰 직접 전달)
await Supabase.TrySignInWithGoogleIdTokenAsync(idToken);
```

#### 익명 → Google 연동

> [!IMPORTANT]
> 익명 세션에서 직접 `TrySignInWithGoogleAsync`를 호출하면 `anonymous_session_requires_explicit_link` 오류가 반환됩니다.  
> 반드시 아래 연동 전용 API를 사용하세요.

```csharp
// Android 네이티브
await Supabase.TryLinkGoogleToCurrentAnonymousAsync();

// iOS · 커스텀 OAuth (ID 토큰 직접 전달)
await Supabase.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken);
```

- 연동 성공 시 동일 `auth.users.id`를 유지하면서 `is_anonymous`가 false가 됩니다.
- 이미 다른 사용자에 연결된 계정이면 연동이 실패하고 기존 익명 세션은 유지됩니다.
- Supabase 대시보드 **Authentication > Settings > Manual linking** 을 ON으로 설정해야 합니다.

---

## 주의사항

> [!WARNING]
> Google이 이미 로그인된 상태에서 `TrySignInAnonymouslyAsync`를 호출하면 실패합니다.  
> 먼저 `TrySignOutFullyAsync`로 로그아웃하세요.
