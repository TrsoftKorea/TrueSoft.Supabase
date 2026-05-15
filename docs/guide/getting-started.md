# 빠른 시작

## 설치

**Window > Package Manager > + > Add package from git URL**

```
https://github.com/ljuh1521/TrueSoft.Supabase.git
```

특정 버전 설치 시 `#버전`을 추가합니다 (예: `...git#0.1.0`).

---

## 초기 설정

1. 메뉴 **TrueSoft > Supabase > 설정 에셋 만들기** 로 `SupabaseSettings`를 생성합니다.
2. Inspector에서 `projectUrl`과 `publishableKey`를 입력합니다.
3. 파일을 **`Assets/Resources/SupabaseSettings.asset`** 위치에 저장합니다.
4. (선택) `SupabaseRuntime` 컴포넌트를 씬에 배치해 세션 자동 복원과 RemoteConfig 폴링을 활성화합니다.

> [!IMPORTANT]
> `SupabaseSettings.asset`은 반드시 `Assets/Resources/` 하위에 있어야 런타임에 로드됩니다.

---

## DB 스키마 실행

`Sql/player/` 폴더의 SQL 파일을 **번호 순서대로** Supabase SQL Editor에서 실행합니다.

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

## 샘플 임포트

Package Manager의 **Samples** 탭 > **Import**를 눌러 예제 씬과 스크립트를 가져옵니다.  
`ExampleSupabaseScenarios.cs` — 키보드 단축키 기반 기능별 테스트 흐름을 제공합니다.
