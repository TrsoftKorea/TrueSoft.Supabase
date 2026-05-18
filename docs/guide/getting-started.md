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
Google OAuth 설정 방법은 [인증](./auth.md)을 참고하세요.

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

자동 로그인 타이밍 제어와 이벤트 콜백 사용법은 [인증](./auth.md)을 참고하세요.

---

## DB 스키마 실행

`Sql/player/` 폴더의 SQL 파일을 **번호 순서대로** Supabase SQL Editor에서 실행합니다.

> [!TIP]
> Supabase 대시보드 어느 화면에서나 **SQL Editor** 버튼 또는 `Ctrl+E`로 열 수 있습니다. 파일 내용을 붙여 넣고 **Run**을 클릭하면 됩니다.

| 순서 | 파일 | 카테고리 | 내용 |
|------|------|----------|------|
| 1 | `01_game_servers.sql` | 기반 | 서버 샤드/선택 |
| 2 | `02_profiles.sql` | 기반 | 공개 프로필 |
| 3 | `03_display_names.sql` | 기반 | 닉네임 유니크 인덱스 |
| 4 | `04_user_saves.sql` | 유저 데이터 | 세이브 테이블 공통 RPC |
| 5 | `05_user_sessions.sql` | 유저 데이터 | 중복 로그인 감지 |
| 6 | `06_anonymous_recovery.sql` | 유저 데이터 | 익명 계정 복구 |
| 7 | `07_server_transfer.sql` | 서버 이주 | server_id 동기화 트리거 + 이주 RPC |
| 8 | `08_withdrawal.sql` | 탈퇴 | 탈퇴 이력 + 예약 인덱스 + 취소 RPC |
| 9 | `09_remote_config.sql` | 기능 | Remote Config |
| 10 | `10_mails.sql` | 기능 | 우편함 |
| 11 | `11_mails_hardening.sql` | 기능 | 우편함 RLS 강화 (선택) |
| 12 | `12_purchases.sql` | 기능 | IAP 구매 검증 (IAP 사용 시) |
| 13 | `13_cron_jobs.sql` | 운영 | 만료 정리·탈퇴 배치 크론 잡 |
| 14 | `14_field_protection.sql` | 유저 데이터 | 세이브 필드 클라이언트 증가 차단 (선택) |

> [!TIP]
> `99_verify_player_schema.sql`을 마지막에 실행하면 스키마 설치 여부를 확인할 수 있습니다.

---

## 최초 로그인

```csharp
// 익명 로그인 — 계정 생성 없이 바로 시작
await Supabase.TrySignInAnonymouslyAsync();
```

`SupabaseRuntime`이 씬에 배치되어 있으면 앱 재시작 시 자동으로 세션을 복원합니다.  
로그인 API 전체 목록과 소셜 로그인은 [인증](./auth.md)을 참고하세요.

---

## 다음 단계

| 주제 | 가이드 |
|------|--------|
| 게임 세이브 데이터 저장·동기화 | [유저 세이브](./user-saves.md) |
| 로그인·소셜 연동·익명 복구 | [인증](./auth.md) |
| 서버 설정값 런타임 변경 | [Remote Config](./remote-config.md) |
| 결제 영수증 서버 검증 | [인앱 결제](./iap.md) |

