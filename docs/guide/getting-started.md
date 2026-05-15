# 빠른 시작

## 설치

**Window > Package Manager > + > Add package from git URL**

```
https://github.com/trsoftkorea/TrueSoft.Supabase.git
```

특정 버전 설치 시 `#버전`을 추가합니다 (예: `...git#0.1.0`).

---

## Supabase 프로젝트 생성

### 1. 프로젝트 만들기

1. [supabase.com](https://supabase.com)에서 로그인 후 **New project**를 클릭합니다.
2. Organization, 프로젝트 이름, DB 비밀번호, 리전을 입력합니다.
   > [!TIP]
   > 한국 중심 서비스라면 리전은 **Northeast Asia (Tokyo)** 를 추천합니다.  
   > 리전은 프로젝트 생성 후 변경할 수 없으므로 처음에 신중하게 선택하세요.
3. 고급 옵션에서 아래와 같이 설정합니다.

   | 옵션 | 설정 | 이유 |
   |------|------|------|
   | **Enable Data API** | ON | SDK가 REST API로 DB와 통신하므로 필수 |
   | **Automatically expose new tables** | OFF | 새 테이블이 의도치 않게 외부에 노출되는 것을 방지 |
   | **Enable automatic RLS** | ON | 새 테이블에 RLS가 자동 적용되어 데이터 보호 |

4. **Create new project**를 클릭하고 프로비저닝이 완료될 때까지 약 1~2분 대기합니다.

### 2. API 키 확인

대시보드 상단의 **Connect** 버튼을 클릭하거나 **Project Settings > API** 페이지에서 아래 두 값을 복사합니다.

| 항목 | 설명 | 사용처 |
|------|------|--------|
| **Project URL** | `https://<project-id>.supabase.co` | `SupabaseSettings.projectUrl` |
| **Publishable key** | `sb_publishable_...` | `SupabaseSettings.publishableKey` |

> [!NOTE]
> Project URL에 포함된 `<project-id>`는 소셜 로그인 콜백 URL(`https://<project-id>.supabase.co/auth/v1/callback`) 설정 시 별도로 필요합니다.

### 3. Authentication 설정

**Authentication > Settings** 에서 아래 항목을 설정합니다.

| 항목 | 설정 | 이유 |
|------|------|------|
| **Allow anonymous sign-ins** | ON | 로그인 없이 바로 게임을 시작하는 익명 플레이어를 지원 |
| **Confirm email** | OFF | 이메일 인증 없이 즉시 로그인. 게임에서 이메일 로그인을 사용하지 않으면 불필요 |
| **Manual linking** | ON | 비회원(익명)으로 플레이하다가 소셜 계정으로 전환할 때 필요. 이 옵션이 OFF면 연동 API가 오류를 반환합니다 |

소셜 로그인을 사용한다면 **Authentication > Sign In / Providers** 에서 추가로 활성화합니다.

#### Google

1. [Google Cloud Console](https://console.cloud.google.com/apis/dashboard)에서 프로젝트를 생성하고 OAuth 동의 화면을 설정합니다.
   - 상단 프로젝트 선택기에서 **새 프로젝트**를 클릭해 프로젝트를 생성합니다.
   - **API 및 서비스 > OAuth 동의 화면** 으로 이동합니다.
   - 앱 이름, 사용자 지원 이메일을 입력합니다.
   - 사용자 유형: **외부** 선택 후 **만들기**
   - 개발자 연락처 이메일을 입력합니다.
2. **OAuth 클라이언트 ID 만들기 (웹 애플리케이션)**
   - **API 및 서비스 > 사용자 인증 정보 > 사용자 인증 정보 만들기 > OAuth 클라이언트 ID** 선택
   - 애플리케이션 유형: **웹 애플리케이션** 선택
   - 승인된 리디렉션 URI에 `https://<project-id>.supabase.co/auth/v1/callback` 추가
   - **만들기** 후 표시되는 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 복사
3. **(Android 네이티브 로그인 사용 시)** OAuth 클라이언트 ID를 Android 유형으로 추가 생성
   - **사용자 인증 정보 만들기 > OAuth 클라이언트 ID** 에서 애플리케이션 유형을 **Android** 로 선택
   - 패키지 이름과 앱의 **SHA-1 서명 인증서 지문**을 입력합니다
     - SHA-1은 Android Studio 터미널에서 `./gradlew signingReport` 명령으로 확인할 수 있습니다
   - Android 클라이언트를 만들면 Google이 내부적으로 앱을 신뢰하게 됩니다. 이 ID 자체는 `SupabaseSettings`에 입력하지 않습니다
   - 웹 애플리케이션 클라이언트의 **클라이언트 ID**를 `SupabaseSettings.googleWebClientId`에 입력합니다
4. Supabase 대시보드 **Authentication > Providers > Google** 에 **클라이언트 ID**와 **클라이언트 보안 비밀번호** 입력

---

## 초기 설정

### 1. SupabaseSettings 에셋 생성

1. 메뉴 **TrueSoft > Supabase > 설정 에셋 만들기** 로 `SupabaseSettings`를 생성합니다.
2. Inspector에서 **Project URL**과 **Publishable Key**를 입력합니다.
3. 파일을 **`Assets/Resources/SupabaseSettings.asset`** 위치에 저장합니다.

> [!IMPORTANT]
> `SupabaseSettings.asset`은 반드시 `Assets/Resources/` 하위에 있어야 런타임에 로드됩니다.

### 2. SupabaseRuntime 배치

메뉴 **TrueSoft > Supabase > 씬에 런타임 오브젝트 만들기** 를 클릭합니다.  
앱의 첫 씬에 `SupabaseSDK` 게임 오브젝트가 생성되고 `SupabaseRuntime` 컴포넌트와 `SupabaseSettings`가 자동으로 연결됩니다.

> [!TIP]
> 씬에 이미 런타임 오브젝트가 있으면 중복 생성 없이 기존 오브젝트를 선택합니다.

`SupabaseRuntime`은 SDK의 핵심 진입점으로, 아래 기능을 담당합니다.

| 기능 | 설명 |
|------|------|
| SDK 초기화 | `SupabaseSettings`를 읽어 모든 서비스를 초기화합니다 |
| 세션 자동 복원 | `OnEnable` 시 저장된 세션을 복원하고 `OnSessionRestored` 이벤트를 발행합니다 |
| 유저 세이브 자동 동기화 | 변경된 세이브 데이터를 쿨타임 주기로 자동 업로드합니다 ([유저 세이브](./user-saves.md) 참고) |
| RemoteConfig 폴링 | 키별 백그라운드 갱신을 `Update`에서 처리합니다 |
| 앱 일시정지·종료 처리 | 포커스를 잃을 때 세이브 데이터를 즉시 플러시합니다 |

**Inspector 주요 설정:**

| 항목 | 기본값 | 설명 |
|------|--------|------|
| 설정 에셋 | (자동) | 비워두면 `Resources/SupabaseSettings`에서 자동 로드 |
| 씬 유지 (DontDestroyOnLoad) | ON | 씬 전환 후에도 SDK 상태 유지 |
| 세션 자동 복원 | ON | 앱 시작 시 저장된 세션 자동 복원 |
| 자동 동기화 사용 | ON | `StaticUserSave` 자동 업로드 활성화 |
| 자동 저장 쿨타임 (초) | 1 | 연속 변경 시 불필요한 요청을 줄이는 최소 간격 |

세션 복원 완료를 기다려야 하는 코드는 `OnSessionRestored` 이벤트를 사용합니다.

```csharp
void OnEnable()  => SupabaseRuntime.SubscribeSessionRestored(OnReady);
void OnDisable() => SupabaseRuntime.UnsubscribeSessionRestored(OnReady);

void OnReady(bool success)
{
    if (success)
    {
        // 기존 세션 복원 성공 → 유저 세이브 로드도 완료된 상태
        InitGame();
    }
    else
    {
        // 저장된 세션 없음 (첫 실행 또는 로그아웃 후) → 로그인 화면으로 이동
        ShowLoginScreen();
    }
}
```

> [!NOTE]
> `success=false`는 오류가 아닌 정상 케이스입니다. 처음 앱을 실행하는 신규 유저나 로그아웃 후 재실행 시 발생합니다. 이 시점에 익명 로그인 또는 로그인 UI를 표시하세요.

---

## DB 스키마 실행

`Sql/player/` 폴더의 SQL 파일을 **번호 순서대로** Supabase SQL Editor에서 실행합니다.

> [!TIP]
> SQL Editor는 Supabase 대시보드 우측 상단의 **SQL Editor** 버튼 또는 `Ctrl+E`로 열 수 있습니다. 파일 내용을 붙여 넣고 **Run**을 클릭하면 됩니다.

| 순서 | 파일 | 내용 |
|------|------|------|
| 1 | `01_game_servers.sql` | 서버 샤드/선택 |
| 2 | `02_profiles.sql` | 공개 프로필 |
| 3 | `03_display_names.sql` | 닉네임 유니크 인덱스 |
| 4 | `04_user_saves.sql` | 게임 세이브 + RLS |
| 5 | `05_user_sessions.sql` | 중복 로그인 감지 |
| 6 | `06_anonymous_recovery_tokens.sql` | 익명 계정 복구 |
| 7~9 | `07~09_*.sql` | 서버 이주·탈퇴 처리 |
| 10 | `10_remote_config.sql` | Remote Config |
| 11 | `11_mails.sql`, `11_mails_client_hardening.sql` | 우편함 |
| 12 | `12_withdrawal_cancel_rpc.sql` | 탈퇴 취소 RPC |
| 13 | `13_cron_jobs_setup.sql` | 크론 잡 설정 |
| 14 | `14_purchases.sql` | IAP 구매 검증 (IAP 사용 시) |

> [!TIP]
> `99_verify_player_schema.sql`을 마지막에 실행하면 스키마 설치 여부를 확인할 수 있습니다.

---

## 최초 로그인

```csharp
// 익명 로그인 (가장 간단)
var (ok, _) = await Supabase.TrySignInAnonymouslyAsync();

// 세션 복원 (앱 재시작 시)
await Supabase.TryRestoreSessionAsync();

// SupabaseRuntime을 씬에 배치하면 위 복원이 자동으로 처리됩니다
```

> [!NOTE]
> `SupabaseRuntime`을 씬에 배치하면 `Awake()`에서 자동으로 세션 복원을 시도합니다.  
> 수동 로그인과 세션 복원을 모두 처리하려면 `Supabase.IsLoggedIn` 플래그를 폴링하거나 로그인 완료 콜백 안에서 초기화 코드를 호출하세요.

---

## 다음 단계

| 주제 | 가이드 |
|------|--------|
| 게임 세이브 데이터 저장·동기화 | [유저 세이브](./user-saves.md) |
| 로그인·소셜 연동·익명 복구 | [인증](./auth.md) |
| 서버 설정값 런타임 변경 | [Remote Config](./remote-config.md) |
| 결제 영수증 서버 검증 | [인앱 결제](./iap.md) |

