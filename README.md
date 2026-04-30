# Truesoft Supabase SDK

Unity에서 Supabase Auth, REST API, Edge Functions를 사용하기 위한 UPM 패키지입니다.

## 설치 방법

1. Unity Package Manager에서 다음 Git URL을 사용해 설치하세요.  
   **Window** > **Package Manager** > **+** > **Add package from git URL**  
   `https://github.com/your-org/com.truesoft.supabase.git`

2. 또는 `Packages/manifest.json`에 아래와 같이 직접 추가할 수 있습니다.  
   `"com.truesoft.supabase": "https://github.com/your-org/com.truesoft.supabase.git"`

3. 특정 버전을 설치하려는 경우  
   `https://github.com/your-org/com.truesoft.supabase.git#0.1.0`  
   와 같이 `#버전` 을 추가하여 설치할 수 있습니다.  
   사용 가능한 버전은 [CHANGELOG.md](CHANGELOG.md)를 확인하세요.

## 준비

`SupabaseSettings`와 `SupabaseRuntime`의 역할은 다음처럼 구분합니다.

- **SupabaseSettings (공통 설정값)**: 프로젝트 URL, Publishable 키, Google Web Client ID, 기본 로그/타임아웃 같은 정적 값을 정의합니다.
- **SupabaseRuntime (씬 실행 정책)**: 시작 시 세션 복원 여부, RemoteConfig **Cold Start**(시작 시 fetch 없음)·키별 백그라운드 폴링·온디맨드 푸시백 같은 런타임 동작을 제어합니다.

1. 메뉴 **TrueSoft > Supabase > 설정 에셋 만들기** 로 `SupabaseSettings` 를 만듭니다.
2. `projectUrl`, `publishableKey` 를 입력합니다. (Android 네이티브 Google 로그인을 쓰면 `googleWebClientId` 도 입력)<br/>
   Google **신규** 가입으로 판단될 때만 `user_metadata.displayName`을 `Player_xxxxxxxx`로 덮어쓰려면 `applyAnonymousDisplayNameOnNewGoogleSignUp`(기본 on)을 유지합니다. 끄면 구글 프로필 이름이 메타데이터에 그대로 남을 수 있습니다.
   익명 계정에 Google을 붙이는 흐름을 쓸 계획이라면 Supabase 대시보드의 Authentication 설정에서 **Manual linking (beta)** 를 켜세요.
3. API 결과 로그를 제어하려면 `enableApiResultLogs`를 설정합니다. (`true`면 Try API별 고정 태그(예: `[Supabase.UserData.Save]`)로 성공/실패 로그 출력. 호출자가 태그를 넘기지 않습니다.)
4. 에셋을 **`Assets/Resources/SupabaseSettings.asset`** 으로 저장합니다. (`Resources.Load("SupabaseSettings")` 와 이름이 일치해야 합니다.)
5. `SupabaseRuntime`은 선택 사항입니다.  
   - 인증/데이터/함수/채팅의 기본 비동기 API는 SDK 내부에서 초기화를 대기하고, 필요 시 `Resources/SupabaseSettings`로 자동 부트스트랩합니다.  
   - 자동 세션 복원/RemoteConfig 주기 폴링까지 씬 라이프사이클로 관리하려면 `SupabaseRuntime` 배치를 권장합니다.

## Truesoft Analytics와 함께 쓸 때 (호스트 전용)

`com.truesoft.supabase`와 `com.truesoft.analytics`(GameAnalytics)는 **패키지 간 참조가 없습니다.**  
Analytics의 `GameEvent.SetUpdateTime`에 Supabase `TryGetServerUtcNowAsync`로 얻은 서버 시각을 넘기려면, **호스트 게임 프로젝트**에서 서버 시각을 **필드에 캐시**하고 getter로 연결합니다(`SetUpdateTime`은 동기 API이므로 getter 안에서 네트워크 호출을 하면 안 됩니다).

- 초기화 순서 예: Supabase 부트스트랩 → 서버 시각 1회 이상 확보 → `GameEvent.SetUpdateTime` 등록.

## REST 테이블 이름 (프로젝트마다 테이블명이 다를 때)

PostgREST로 접근하는 **테이블 이름**은 프로젝트마다 다를 수 있으므로, `SupabaseSettings`에서 바꿀 수 있습니다.

- **User Saves**: 신규는 **명시 컬럼 + 변경분 PATCH** 권장 — `TryPatchUserDataAsync` / `TryLoadUserDataColumnsAsync(select)` (테이블은 `userSavesTable`, 기본 `user_saves`). 컬럼명을 C# 모델에 한 번만 적어 두고 `select`·PATCH 키를 맞추려면 **`[UserSaveColumn]` + `TryLoadUserSaveAttributedAsync` / `TryPatchUserSaveDiffAsync`** 를 쓸 수 있습니다(아래 절). `TrySaveUserDataAsync` / `TryLoadUserDataAsync`는 `save_data(jsonb)` 통째 저장·로드용(초기 프로젝트는 생략 가능).
- **Remote Config**: `remoteConfigTable` (기본 `remote_config`)
- **채팅**: `chatMessagesTable` (기본 `chat_messages`)
- **공개 프로필**: `publicProfilesTable` (기본 `profiles`) — `TryGetPublicDisplayNameAsync`, `TrySetMyDisplayNameAsync`

코드에서 `SupabaseOptions`의 `UserSavesTable`, `RemoteConfigTable`, `ChatMessagesTable`, `PublicProfilesTable`도 같은 역할을 합니다. Unity 에셋 경로로 쓸 때는 `SupabaseSettings.ToOptions()`가 비어 있는 테이블 필드를 기본 이름으로 채웁니다. `SupabaseOptions`만 직접 만들 때는 빈 문자열을 넣지 말고, 필드 기본값을 두거나 유효한 이름을 지정하세요. 스키마가 `public`이 아니면 `schema.table` 형식으로 지정할 수 있습니다. 잘못된 문자(`..`, `/`, `\` 등)는 초기화 시 검증되어 예외가 납니다.

## 세이브 데이터 클래스: `[UserSaveColumn]` API와 에디터 OpenAPI 생성기

DB `user_saves`(또는 `userSavesTable`)의 **컬럼 이름**과 클라이언트 **select / PATCH**가 어긋나지 않도록, 필드에 어노테이션을 붙이거나 에디터에서 스키마를 받아 C# 클래스를 생성할 수 있습니다.

### 코드에서 쓰기 (`TryLoadUserSaveAttributedAsync` / `TryPatchUserSaveDiffAsync`)

1. 세이브 한 행을 담는 클래스에 `public` 필드(또는 프로퍼티)를 두고, DB에 올릴 컬럼마다 **`[UserSaveColumn("db_column")]`** 을 붙입니다. 인자를 생략하면 **멤버 이름이 곧 컬럼명**입니다.
2. **로드:** `Supabase.TryLoadUserSaveAttributedAsync<MySaveRow>(defaultValue, includeUpdatedAt: true)` — 붙인 컬럼만 모아 `select` CSV를 만들고 가져옵니다. **본인 `account_id`에 해당하는 행이 0건인지(신규 유저)와 1건인지 구분**하려면 `Supabase.TryLoadUserSaveAttributedWithRowStateAsync<MySaveRow>(...)` 를 쓰세요. 성공 시 `success == true`이고 `hasRow == false`이면 응답이 빈 배열(`[]`)인 경우입니다(값은 `new MySaveRow()`). 인증/HTTP 실패는 `success == false`입니다. 명시 `select` 경로도 동일하게 `TryLoadUserDataColumnsWithRowStateAsync<T>` / `LoadUserDataColumnsWithRowStateAsync<T>` 가 있습니다.
3. **변경분만 저장:** 메모리에 **이전 스냅샷**과 **현재 스냅샷**을 둔 뒤 `Supabase.TryPatchUserSaveDiffAsync(previous, current, ensureRowFirst: true, setUpdatedAtIsoUtc: true)` — 값이 바뀐 컬럼만 PATCH합니다. 둘이 같으면 네트워크 PATCH는 생략됩니다.

내부 `SupabaseResult`를 쓰려면 `Supabase.LoadUserSaveAttributedAsync` / `Supabase.LoadUserSaveAttributedWithRowStateAsync` / `Supabase.PatchUserSaveDiffAsync` 를 사용할 수 있습니다. Try 계열 로그 태그는 `Supabase.UserData.LoadAttributed`, `Supabase.UserData.LoadAttributed.RowState`, `Supabase.UserData.LoadColumns.RowState`, `Supabase.UserData.PatchDiff` 입니다.

### OpenAPI 생성기 (`SaveData.Gold = 10` 스타일)

생성기는 **항상** `public static class` + 내부 Row를 생성합니다. `SaveData.Gold = 10` 같은 프로퍼티 변경이 자동 저장 대상이 됩니다.

- `TryLoadAsync`는 `TryLoadUserSaveAttributedWithRowStateAsync`를 사용합니다. 행이 없으면 Row는 타입 기본값이므로, 게임 초기값은 로드 후 프로퍼티로 채우면 됩니다.
- `TrySaveIfChangedAsync`는 마지막 서버 스냅샷 대비 변경된 컬럼만 PATCH합니다(변경 없으면 전송 없음).
- 변경은 즉시 매번 보내지 않고 **쿨타임 배치**로 묶입니다.
- 중요한 타이밍에는 `TryRequestImmediateSave()`(요청) 또는 `TryFlushNowAsync()`(완료 대기)로 **즉시 전송**을 사용할 수 있습니다.
- 이미 전송 중이면 즉시 전송은 중복 실행하지 않고, 완료 후 dirty를 다시 평가합니다.
- 런타임(`SupabaseRuntime`)은 **Pause/Quit 시 dirty가 있으면 즉시 전송을 시도**합니다.
- 쿨타임은 `Supabase.ConfigureUserSaveAutoSyncCooldown(seconds)`로 조정할 수 있습니다.

### 주의점 (JsonUtility · 컬럼명)

- **Unity `JsonUtility`로 역직렬화할 때**, PostgREST가 내려주는 **JSON 키(보통 DB 컬럼명과 동일)** 와 **C# 필드 이름이 같아야** 값이 채워집니다. 그래서 DB가 `snake_case`이면 필드도 `level`, `updated_at` 처럼 **컬럼명과 동일한 이름**을 쓰는 편이 안전합니다.
- `[UserSaveColumn("other_name")]` 만으로 **JSON 키 이름이 바뀌지는 않습니다.** 컬럼명과 C# 이름이 다르게 두고 싶다면 Newtonsoft 등 **별도 역직렬화**가 필요합니다.
- `updated_at` 은 스키마 도우미가 `select`에 넣을 수 있지만, **diff PATCH에는 자동으로 넣지 않습니다.** 서버/트리거가 갱신하거나, SDK 옵션 `setUpdatedAtIsoUtc` 등 기존 PATCH 경로와 같이 쓰입니다.
- 복합 타입(jsonb 배열 등)은 클래스 한 필드에 담기 어렵습니다. 해당 컬럼은 수동 설계하거나 다른 API를 쓰세요.

### 에디터: OpenAPI로 유저 데이터 클래스 자동 생성

메뉴 **TrueSoft > Supabase > 유저 데이터 클래스 생성** 을 열면 PostgREST OpenAPI(`GET …/rest/v1/`, `Accept: application/openapi+json`)를 바탕으로 **static API + 내부 Row(`[Serializable]`, `[UserSaveColumn]`)** 가 들어간 `.cs` 초안을 만들 수 있습니다.

**사용 순서 요약**

1. (선택) `Resources/SupabaseSettings`를 넣으면 URL·`userSavesTable`이 채워집니다. **Secret 키는 대시보드에서 복사해 창에 직접 입력합니다**(에셋에 넣지 마세요).
2. **API에서 가져와 미리보기**는 **Secret 키가 있을 때만** 사용합니다. 키 없이 쓰려면 브라우저·CLI로 받은 스펙을 **OpenAPI JSON 가져오기…**로 엽니다.
3. 테이블명·제외 컬럼·클래스 이름·네임스페이스를 조정한 뒤 미리보기를 확인합니다.
4. **프로젝트에 .cs 저장…**으로 `Assets` 아래에 저장합니다.

**주의점**

- **가져오기에서 HTTP 401** 이면 **Secret 키·URL**을 확인하세요. URL은 **`https://xxxx.supabase.co`** 프로젝트 루트만 쓰면 됩니다. 그래도 안 되면 **OpenAPI JSON 가져오기…**로 우회하세요.
- **키 형식별 REST 헤더:** 대시보드 **Legacy** JWT Secret 키(`eyJ…`)와 새 **`sb_secret_…` 키**는 Supabase 게이트웨이 처리가 다릅니다. 에디터 OpenAPI 가져오기는 새 키일 때 **`apikey`만** 보내고, 레거시 JWT일 때만 `Authorization: Bearer`에 동일 키를 추가합니다.
- **Secret 키는 절대 플레이어 빌드·저장소·버전 관리에 넣지 마세요.** 이 창에서는 **에디터에서만** 붙여 넣어 쓰는 것을 전제로 합니다.
- 생성기는 타입을 완전히 추론하지 못하는 컬럼(배열·일부 `$ref`·jsonb 등)을 `string /* … refine */` 형태로 남깁니다. **실제 게임에 맞게 타입과 주석을 손으로 다듬어야** 합니다.
- C# 식별자가 될 수 없는 컬럼명(하이픈 등)은 **JsonUtility와 맞지 않아 생성 시 건너뛰고** 경고를 냅니다. 그런 컬럼은 수동 매핑이 필요합니다.

### 아직 “고정”인 부분 (하드코딩이 없다는 뜻은 아님)

테이블 **이름**만 설정 가능합니다. 다음은 Supabase 표준이거나 이 SDK가 전제로 두는 스키마입니다.

| 구분 | 고정에 가까운 내용 |
|------|-------------------|
| **URL 경로** | `…/auth/v1/…`, `…/rest/v1/{테이블}`, `…/functions/v1/{함수명}` — Supabase API 규격 (프로젝트 URL·함수 이름은 설정/인자로 바뀜) |
| **User Saves** | 권장: **게임 조회·수정은 `account_id`만**. **`user_id`**는 동일인 식별·운영용(동일 Google이면 재가입 후에도 **같은 `user_id` 가능**). 행은 **서로게이트 PK**로 두어 재가입 시 **새 `account_id` 행 INSERT**, 옛 행은 `account_id` NULL. SDK 기본 컬럼명 `user_id`에 Auth id → 매핑·확장 필요 |
| **Remote Config** | 조회 컬럼 `key`, `value_json`, `updated_at`, `version`, `enabled`, `description`, `poll_interval_seconds`, `max_stale_seconds`, `requires_auth`, `client_version_min`, `client_version_max` — DDL [`Sql/player/10_remote_config.sql`](Sql/player/10_remote_config.sql). **설계: 1키 = 1설정묶음(JSON)** — 관련 설정을 하나의 키에 `{stamina:{...},battle:{...}}` 형태로 묶고, `poll_interval_seconds`는 키 단위로 설정 |
| **채팅** | 컬럼 `id`, `channel_id`, `user_id`(보낸 이 식별 — 정책에 따라 `account_id` 또는 플레이어 `user_id`), `display_name`, `content`, `created_at` |
| **공개 프로필** | 권장: **`id`**(PK) + **`user_id`** + **`account_id`**. DDL은 [`Sql/player/`](Sql/player/) (`01`…`09` 순, [순서 안내](Sql/supabase_player_tables.sql)) |

즉, **REST 대상 테이블명**은 유연하고, **각 기능이 쓰는 컬럼·쿼리 형태**는 아직 코드에 박혀 있습니다. 다른 스키마를 쓰려면 해당 서비스를 감싼 별도 레이어나 포크가 필요합니다.

### 플레이어 데이터 테이블 구조 (요약)

| 구분 | 역할 |
|------|------|
| **`account_id`** | `auth.users.id`. **게임 클라이언트가 데이터를 읽고 쓸 때 쓰는 기준** — RLS도 보통 `account_id = auth.uid()`. 계정 삭제 시 Auth 행이 없어지고, DB 행에는 **`account_id`만 NULL**로 남기는 패턴. |
| **`user_id`** | **플레이어(사람) 단위 불변 id** — 같은 Google이면 탈퇴 후 재가입해도 **같은 값**으로 둘 수 있음. **운영툴·수동 조회·감사**에서 “동일인의 과거 행”을 묶을 때 사용. **게임은 이 키로 직접 조회하지 않음.** |
| **행 PK (`id`)** | `profiles`·`user_saves`는 **`id` UUID**를 PK로 두고, **한 `user_id`에 대해 “계정 생애”마다 다른 행**이 생기게 함(재가입 = 새 `account_id` = **새 행 INSERT**). |

**한 줄로:** 게임은 **항상 현재 로그인 계정(`account_id`)** 만 보고, **같은 사람의 히스토리**는 **`user_id`로 운영 쪽에서만** 묶는다. 탈퇴하면 그 계정에 붙었던 행은 `account_id`가 NULL이 되어 **게임에서는 더 이상 안 보이고**, 재가입 시 **옛 행을 UPDATE로 다시 연결하지 않고** **새 행**을 만든다 → **예전 세이브·프로필이 게임에 자동 복구되지 않음**.

별도 `app_users` 테이블은 두지 않고, **`profiles`·`user_saves` 행 안에 `user_id` + `account_id`를 같이 둡니다.**

**플레이어 DDL:** [`Sql/player/`](Sql/player/) — 테이블·기능별로 분리. Supabase SQL Editor에서는 `01_game_servers.sql`부터 `10_remote_config.sql`, `99_verify_player_schema.sql`(선택)까지 번호 순 실행([`Sql/supabase_player_tables.sql`](Sql/supabase_player_tables.sql)에 목록·합치기 예시).

### 공개 프로필·닉네임·탈퇴 표시 (`profiles`)

**탈퇴 표시**를 다른 클라이언트가 볼 필요가 있으면 `profiles`에 `withdrawn_at` 등을 두는 패턴이 흔합니다. **스키마·정책 전체**는 위 SQL 파일의 `profiles`와 동일합니다.

- **SELECT:** 공개 읽기 정책(닉네임 등). 탈퇴 후 `account_id` NULL 행을 숨길지는 정책 선택.
- **INSERT/UPDATE:** `account_id = auth.uid()`(게임은 **계정 기준**만).
- **displayName 유니크:** [`Sql/player/03_display_names.sql`](Sql/player/03_display_names.sql) 유니크 인덱스(`lower(trim(display_name))`, 빈 값 제외) 참고.

**`user_id` / 재가입 / RLS**에 대한 공통 설명은 위 **「플레이어 데이터 테이블 구조 (요약)」** 와 아래 **5번 절**(`user_id`·`account_id` — 동작·재가입·SDK)을 봅니다.

기존 테이블에서 옮길 때는 마이그레이션으로 `user_id`·`account_id`·`id`(PK)를 채웁니다.

**Unity API 요약**

- 중복 확인: `TryIsDisplayNameAvailableAsync("후보닉")` — 로그인 중이면 **현재 계정이 이미 쓰는 이름**은 사용 가능으로 나옵니다(닉 수정 화면용).
- 최초/수정 저장: `TrySetMyDisplayNameAsync` 또는 별칭 `TryUpdateMyDisplayNameAsync`(동일).
- 프로필 한 번에 조회: `TryGetPublicProfileAsync(userId)` — SDK는 URL 필터에 넘긴 값으로 조회하므로, **공개 조회를 `user_id`로 할지 `account_id`로 할지**에 맞춰 호출 인자를 통일합니다.
- 탈퇴 표시: `TryMarkMyWithdrawnAsync()`(UTC 시각 기록) / 해제: `TryClearMyWithdrawalAsync()` / 임의 시각: `TrySetMyWithdrawnAtAsync(iso8601)`.
- displayName만: `TryGetPublicDisplayNameAsync`를 사용합니다.

닉네임 길이는 클라이언트에서 최대 64자로 잘립니다. DB 유니크 인덱스는 `lower(trim(...))` 기준이므로, **저장되는 문자열과 중복 검사**가 가능한 한 같은 규칙(공백·대소문자)을 맞추는 것이 좋습니다.

### 법적 대응을 고려한 테이블 구분 (탈퇴 시 삭제 vs 보관)

법률 검토는 별도로 받아야 하며, 아래는 **글로벌 서비스에서 흔히 쓰는 설계 패턴**입니다. 핵심은 **“탈퇴 완료 시 지울 데이터”**와 **“법령·분쟁 대응으로 남길 데이터”**를 **스키마·RLS·접근 주체**로 분리하는 것입니다.

#### 1. 두 갈래로 나누기

| 구분 | 목적 | 탈퇴 완료 시 일반적인 처리 |
|------|------|---------------------------|
| **운영·서비스 데이터** | 게임 플레이, 공개 프로필, 채팅, 친구, 인벤 등 | **삭제** 또는 **비식별화**(식별자·닉네임 제거). 클라이언트·일반 API(RLS)에서 접근. |
| **법정 보존·감사 데이터** | 전자상거래·세금·결제 분쟁, 부정 이용 소명 등 | **항목·기간을 최소화**해 **별도 테이블**(또는 별도 스키마)에만 보관. **서비스 롤·내부 도구**만 접근, 일반 유저 RLS와 분리. |

같은 Postgres 안에서도 **`public`(또는 `app_*`)** 과 **`compliance`**(이름은 팀 규칙에 맞게)처럼 **스키마를 나누면** “어디까지 클라이언트가 닿는지”를 정리하기 쉽습니다.

#### 탈퇴 시 비활성화 대신 삭제

삭제 가능한 운영 데이터는 `withdrawn_at` 같은 **표시만 남기는 방식** 대신 **행 삭제**를 택할 수 있습니다. `profiles`·`user_saves`·`chat_messages` 등은 `auth.users` 삭제와 **CASCADE** 또는 탈퇴 전용 **Edge Function**에서 순서대로 제거하고, 아래 **법정 보존 테이블만** 사용자 레코드와 **FK로 묶이지 않게** 남기는 패턴이 흔합니다. (SDK의 `TryMarkMyWithdrawnAsync` 등은 “soft 탈퇴 표시”용이므로, hard delete 전략이면 앱·서버 플로우에서 대체합니다.)

#### 2. 운영 측에 두기 좋은 것 (탈퇴 시 정리 대상)

- `profiles` — 탈퇴 절차에 따라 **행 삭제**(auth `ON DELETE CASCADE`와 맞춤) 또는 닉네임 비우기 + `withdrawn_at`만 남기기 등 **정책 선택**.
- `user_saves`, `chat_messages`, 기타 게임 전용 테이블 — **삭제 또는 익명화**.
- 이 SDK가 직접 다루는 REST 테이블들은 대부분 여기에 해당.

#### 3. 보관 측에 두기 좋은 것 (예시)

**“법적 대비로 결제 정보만 남기면 될까?”** — **항상 그렇다고 단정하기 어렵습니다.** 관할·업종·유료 여부에 따라 더 적거나 더 필요할 수 있습니다.

- **결제/거래** — Stripe 등 **PG가 원장**이면 카드 정보마저 자사 DB에 없을 수 있습니다. 그 경우에도 **세금·전자상거래·환불 분쟁** 대비로 **거래 요약**(주문·금액·통화·일시·상태, 가능하면 PG 결제 id)을 **최소 필드**로 `compliance` 쪽에 두는 팀이 많습니다. PCI 때문에 **카드 번호·CVC는 저장하지 않는 것**이 원칙입니다.
- **결제 외** — 약관/부정이용 대응으로 **짧은 기간의 보안·감사 로그**를 남기는 경우, **세금 전용 장부**를 별도 시스템에 두는 경우 등은 “결제 한 가지”만으로는 부족할 수 있습니다.
- **`compliance.data_retention_ledger`** (이름 예시) — 탈퇴·삭제 요청 **이벤트**와 `retain_until` 추적용. 결제 보관과 별도로 두기도 합니다.  
  - 필드 예: `event_type`(예: `account_closed`), `user_id`(플레이어 고유 id 등 **최소 식별자**), `occurred_at`, `legal_basis`, `retain_until`, `payload_minimal`(JSON 최소).  
  - **append-only**에 가깝게 두고, 탈퇴 **이벤트**와 **보존 만료**를 추적.
- **삭제 요청 재적용용** — DB 롤백 대비 시, 복구 후 재삭제에 쓰는 **불변에 가까운 이벤트 로그**(Postgres 밖 스토리지와 병행하는 경우도 많음).

정리하면, **보관 테이블을 “결제(거래) 최소 레코드 한 세트”로 시작**하는 것은 현실적이지만, **그 한 세트에 어떤 컬럼이 들어가는지**는 법무·회계와 맞춘 뒤 처리방침에 적는 것이 안전합니다.

#### 4. FK·삭제 순서

- `auth.users` 삭제 시 **운영 테이블**은 `ON DELETE CASCADE`로 함께 지우기 쉽게 설계할 수 있음.
- **보관 테이블**은 `auth.users`에 직접 FK를 두면 사용자 삭제 시 **함께 지워져** 보존 의무와 충돌할 수 있음. 이 경우 **FK 없음** + 플레이어 **`user_id`만** 두거나, **보존 기간 후 배치 삭제** 등으로 모델링합니다.

#### 5. `user_id`·`account_id` — 동작·재가입·SDK (용어 표는 위 요약과 동일)

Supabase **Auth로 계정을 삭제**하면 `auth.users` 행이 제거되고, SQL 파일과 같이 **`ON DELETE SET NULL`**이면 해당 게임 행의 **`account_id`만 NULL**로 남습니다. **`user_id`는 유지**되어 운영툴로 과거 행을 조회할 수 있습니다.

- **게임:** RLS·쿼리는 **`account_id = auth.uid()`** 만.
- **같은 Google 재가입:** `user_id`는 같은 값으로 둘 수 있으나, **옛 행에 `account_id`를 UPDATE하지 않고** **새 `account_id`로 새 행 INSERT** → 게임은 새 데이터만 사용.
- **운영:** **`user_id`**로 동일인의 과거·현재 행을 함께 조회.

**`user_id`를 같게 유지:** OAuth 제공자 안정 식별자(예: Google `sub`)로 결정(해시·UUID v5·매핑 테이블 등). 익명·다중 제공자는 정책으로 정의.

**재가입 요약**

| 경우 | `user_id` | 게임 데이터 행 |
|------|-----------|----------------|
| 동일 Google 재가입 | 같게 둘 수 있음 | **새 `account_id`로 새 행 INSERT**, 옛 행은 NULL 유지 |
| 다른 계정 | 다른 `user_id` | 새 행 INSERT |
| 탈퇴 직후 | 행에 남음 | `account_id` → NULL, 게임 접근 불가 |

**이 SDK:** REST 바디의 **`user_id` 컬럼에 Auth id**를 넣는 전제라, 위 스키마의 **`account_id`**와 맞추려면 **컬럼명/매핑** 또는 **서버·Edge Function에서 변환**이 필요합니다.

**`user_saves`·`account_closures` 테이블 정의와 `user_saves` RLS**는 [`Sql/player/04_user_saves.sql`](Sql/player/04_user_saves.sql), [`Sql/player/09_account_closures.sql`](Sql/player/09_account_closures.sql)에 있습니다. `account_closures`는 RLS만 켜고 정책 없음(일반 JWT 차단, Secret 키 등 서버측 호출 전제).

**앱 쪽**

1. 로그인 성공 시 **플레이어 `user_id`를 결정**(동일 Google이면 항상 같은 값). **`user_saves` / `profiles`는 `account_id` = 현재 `auth.uid()` 인 행이 있는지 보고**, 없으면 **새 행 INSERT**(`user_id` + `account_id`). **옛 행(`account_id` NULL)에는 새 `account_id`를 UPDATE로 붙이지 않음.**  
2. 게임은 **항상 `account_id`(RLS / `auth.uid()`)** 만으로 읽기·쓰기.  
3. 탈퇴 후 재가입해도 **게임은 새 `account_id` 전용 새 행**만 보므로 **이전 세이브·프로필과 자동으로 이어지지 않음**. **`user_id`는 운영·감사용으로만** 같은 사람 아래 여러 행을 묶는 데 사용.  
4. 공개 프로필·리더보드 등에서 **“현재 활성 닉네임”**만 보이게 하려면 `account_id is not null` 조건 등을 두는 식으로 정책을 정합니다.

**서버 이주 (`server_id`)**

- **유저 자가 이주**: 로그인된 앱 세션으로 `POST {SUPABASE_URL}/rest/v1/rpc/ts_transfer_my_server` (바디: `p_target_server_code`, 선택 `p_reason`) 또는 SDK `TryTransferMyServerAsync`. JWT의 `auth.uid()`에 해당하는 `profiles`·파생 테이블이 한 트랜잭션에서 같이 옮겨집니다. 대상 `game_servers.allow_transfers`가 false이면 거절되며, 이주 서버에 동일 닉이 이미 있으면 `display_name_taken_in_target_server`로 실패합니다.
- **Retool·운영(임의 계정)**: **Project Secret 키만** 사용해 `POST {SUPABASE_URL}/rest/v1/rpc/ts_admin_transfer_user_server` 를 호출합니다. 헤더: `apikey: <Secret 키>`, `Authorization: Bearer <Secret 키>`, `Content-Type: application/json`. 바디 예: `{"p_account_id":"<auth.users의 uuid>","p_target_server_code":"KR1","p_reason":"support_ticket_123"}`. 응답은 `{ "ok", "reason", "target_server_id" }` 형태의 행 하나(배열)입니다. `forbidden_not_service_role`이면 이 RPC에 필요한 권한의 JWT가 아닙니다. **Secret 키는 클라이언트·버전관리에 넣지 말고** Retool 시크릿·백엔드에서만 쓰세요.

#### 6. 문서

- 개인정보 처리방침에 **보유 항목·기간·목적**을 테이블 단위로 대응시켜 두면, 설계와 운영이 맞물립니다.

## 제공 범위

- **초기화/세션 준비**: `Supabase.TryStartAsync()`를 기본 진입점으로 사용합니다. 이 단계는 초기화/세션 복원만 담당하며 자동 익명 로그인은 수행하지 않습니다.
- **인증**: `TrySignInAnonymouslyAsync`, `TrySignInWithGoogleAsync`, `TrySignInWithGoogleIdTokenAsync`, `TryRestoreSessionAsync`
- **서버 샤드**: `SetCurrentServerCode`, `GetCurrentServerCode`, `TryTransferMyServerAsync`; 운영·Retool은 RPC `ts_admin_transfer_user_server` (Secret 키 전용, 위 「서버 이주 (`server_id`)」 절)
- **로그아웃**: `TrySignOutFullyAsync` — Android에서는 네이티브 Google 로그아웃을 시도한 뒤 Supabase `SignOutAsync`와 동일 처리(익명이면 복구용 upsert 후 로컬 정리). `TrySignOutAsync`만 쓰면 Google 계정 선택기 상태는 그대로일 수 있습니다.
- **익명→Google 연동(별도 버튼 권장)**: `TryLinkGoogleToCurrentAnonymousAsync` (Android 네이티브), `TryLinkGoogleToCurrentAnonymousWithIdTokenAsync` (ID 토큰 직접 전달). 성공 시 클라이언트가 지문 행을 best-effort 삭제(`ts_anon_recovery_delete_by_fingerprint`)하며, DB 트리거는 [`Sql/player/06_anonymous_recovery_tokens.sql`](Sql/player/06_anonymous_recovery_tokens.sql)을 참고하세요. 탈퇴 요청 RPC(`ts_request_withdrawal`)는 `ts_delete_my_anon_recovery_tokens`로 본인 행을 정리합니다.
- **사용자 데이터**: 프로젝트별 컬럼 기반이면 `TryPatchUserDataAsync` / `TryLoadUserDataColumnsAsync(select)` 또는 **`TryLoadUserSaveAttributedAsync` / `TryPatchUserSaveDiffAsync` + `[UserSaveColumn]`** 를 사용하세요. (`user_saves` 스키마는 [`Sql/player/04_user_saves.sql`](Sql/player/04_user_saves.sql)와 맞출 것)
- **공개 프로필**: `TryGetPublicProfileAsync`, `TryIsDisplayNameAvailableAsync`, `TrySetMyDisplayNameAsync` / `TryUpdateMyDisplayNameAsync`, `TryMarkMyWithdrawnAsync`, `TryClearMyWithdrawalAsync` ([`Sql/player/02_profiles.sql`](Sql/player/02_profiles.sql)·[`Sql/player/03_display_names.sql`](Sql/player/03_display_names.sql) 스키마와 맞출 것)
- **원격 설정**: 구독, `TryRefreshRemoteConfigAsync`(전체 갱신), `TryPollRemoteConfigAsync`(전체 또는 `keys` 지정), `GetRemoteConfigAsync<T>(key)` → `SupabaseResult<T>`, `TryGetRemoteConfigAsync<T>(key)` → `(bool, T)`, `GetRemoteConfig` 캐시 조회(동기)
- **원격 설정(Cold Start + 키별 폴링)**: 앱 시작 시 RemoteConfig를 **자동으로 가져오지 않습니다**. 첫 `GetRemoteConfigAsync` 등으로 키를 읽을 때 해당 키만 서버에서 조회합니다. **캐시 유효 시간**은 DB `max_stale_seconds`(0 이하면 SDK에서 300초로 처리)를 사용합니다. DB `poll_interval_seconds`가 0보다 큰 키는 `SupabaseRuntime`이 `Update`에서 **키별**로 만기 시 폴링합니다. fetch 실패·키 없음·역직렬화 실패 시 `SupabaseResult<T>.Fail` 또는 `Try*`의 `success == false`입니다. 전체 즉시 동기화는 `Supabase.RefreshRemoteConfigOnDemandAsync()` 후 `GetRemoteConfig`로 읽고, 다음 폴링 시각은 `remoteConfigOnDemandPushbackSeconds`만큼 미뤄집니다.
- **Edge Functions**: `TryInvokeFunctionAsync`
- **채팅**: `TryJoinChatChannelAsync`, `TrySendChatMessageAsync`, 채널 이탈
- **Google Play 영수증 검증**: `TryVerifyGooglePlayPurchaseAsync` (위 「Google Play 인앱 상품 영수증 검증」 섹션 참고)

## 샘플 가져오기

Package Manager의 **Samples** 탭에서 **Import**로 프로젝트에 복사해 사용합니다.

### 원격 설정(Cold Start + DB `max_stale_seconds` + 키별 폴링)

- **Cold Start**: 시작 시 RemoteConfig HTTP 호출 없음. `GetRemoteConfigAsync<T>(key)`로 필요한 키만 서버 조회.
- **Stale-While-Revalidate**: DB `max_stale_seconds`를 넘긴 캐시는 **즉시 반환**한 뒤 해당 **키**를 백그라운드에서 갱신합니다.
- **키별 폴링**: `poll_interval_seconds`(0=폴링 안 함)는 Retool에서 키 단위로 조절. `SupabaseRuntime`이 `TickRemoteConfigKeyPolls`로 만기 키만 폴링합니다.
- **온디맨드 전체 갱신**: `RefreshRemoteConfigOnDemandAsync()` 후 캐시에서 읽기.
- **Source Generator**(선택): 패키지 루트 `RoslynAnalyzers/Truesoft.Supabase.RemoteConfig.SourceGenerator.dll`이 프로젝트에 포함됩니다. `static partial` 클래스에 `[RemoteConfig]`와 `[RemoteConfigKey("키")]`가 붙은 `RemoteConfigEntry<T>` 반환 partial 메서드를 선언하면 구현이 자동 생성됩니다. DLL을 갱신하려면 [`Generators/README.md`](Generators/README.md)를 참고하세요.

예시 (JSON 클러스터링 권장):

관련 설정은 하나의 키에 JSON 객체로 묶어 관리합니다:

```csharp
[Serializable]
public class GameplayConfig
{
    public StaminaConfig stamina;
    public BattleConfig battle;
}

[Serializable]
public class StaminaConfig { public int maxEnergy; public int regenSeconds; }

// 사용
var result = await Supabase.GetRemoteConfigAsync<GameplayConfig>("gameplay_v1");
if (result.IsSuccess)
{
    ApplyStamina(result.Data.stamina);
    ApplyBattle(result.Data.battle);
}
else
    Debug.LogError(result.ErrorMessage);
```

Source Generator로 묶으면 더 깔끔합니다:

```csharp
[RemoteConfig]
public static partial class GameRemoteConfig
{
    [RemoteConfigKey("gameplay_v1")]
    public static partial RemoteConfigEntry<GameplayConfig> Gameplay();
}

// 사용
var result = await GameRemoteConfig.Gameplay().FetchAsync();
```

단독 설정이 필요한 경우 (이벤트 ON/OFF 등):

```csharp
[RemoteConfigKey("event_christmas")]
public static partial RemoteConfigEntry<EventConfig> ChristmasEvent();
```

```
Retool 예시:
- key: "gameplay_v1"
- value_json: {"stamina":{"maxEnergy":100,"regenSeconds":300},"battle":{"playerDmg":1.5}}
- poll_interval_seconds: 60 (키 단위 폴링)
- max_stale_seconds: 300

| 샘플 | 내용 |
|------|------|
| **Examples** | 로그인, 데이터 저장/불러오기, RemoteConfig, Edge Function을 기능별 함수로 분리한 데모 |

샘플 소스는 패키지 안의 `Samples~`에만 있고, **Import 전에는 프로젝트에 컴파일되지 않습니다.** Import 후에는 `Assets/Samples/<패키지 표시 이름>/<버전>/` 아래에 복사됩니다.

## Google Play 인앱 상품 영수증 검증

클라이언트가 구글 플레이에서 인앱 상품(inapp)을 구매한 후, 서버에서 영수증을 검증해 중복 결제를 방지합니다.

**SDK 범위**: 영수증 유효성 검증만 수행합니다. 보상 지급은 게임 서버/클라이언트에서 별도로 처리하세요.

### 게임 프로젝트별 필수 설정

각 게임 프로젝트마다 **Google Cloud**, **Google Play Console**, **Supabase** 세 곳에서 설정이 필요합니다.

#### 1. Google Cloud 서비스 계정 생성 및 JSON 키 다운로드

1. **[Google Cloud Console](https://console.cloud.google.com/)** 접속
2. 상단 프로젝트 드롭다운 → **새 프로젝트** 클릭
   - 프로젝트 이름: `[게임명] Play Store Validation` (예시)
   - **만들기** 클릭
3. **API 및 서비스** → **라이브러리** 클릭
4. 검색: `Google Play Developer API` → 클릭 → **활성화**
5. **API 및 서비스** → **사용자 인증 정보** 클릭
6. **사용자 인증 정보 만들기** → **서비스 계정** 선택
   - **서비스 계정 이름**: `PlayStoreValidator` (또는 원하는 이름)
   - **만들고 계속** → **완료** 클릭
7. 생성된 서비스 계정 클릭 (이메일: `xxxxxx@xxxxxx.iam.gserviceaccount.com`)
8. **키** 탭 → **키 추가** → **새 키 만들기** → **JSON** 선택
   - **만들기** 클릭 → JSON 파일 자동 다운로드 ⚠️ **안전한 곳에 보관**

#### 2. Google Play Console에서 서비스 계정 권한 부여

1. **[Google Play Console](https://play.google.com/console)** 접속 (앱을 배포한 계정)
2. 설정 → **사용자 및 권한** 클릭
3. **사용자 초대** 클릭
4. 위에서 복사한 서비스 계정 이메일 입력
5. **역할** → **재무 담당자** (또는 **관리자**) 선택
6. **앱 권한** (선택): 모든 앱 또는 특정 앱만
7. **초대** 클릭

#### 3. Supabase Edge Function Secrets 등록

1. **[Supabase 대시보드](https://supabase.com/dashboard)** 접속 → 프로젝트 선택
2. **Edge Functions** → **purchase-verify-google** 클릭
3. **Secrets** 탭
4. **+ Add secret** 클릭
5. **Key**: `GOOGLE_SERVICE_ACCOUNT_JSON`
6. **Value**: 1단계에서 다운로드한 JSON 파일의 **전체 내용** 복사해 붙여넣기
7. **Save** 클릭

#### 4. SupabaseSettings에서 기본 패키지명 설정

1. Unity 프로젝트에서 `Assets/Resources/SupabaseSettings.asset` 열기
2. 인스펙터 하단 **인앱 결제** 섹션 확인
3. **Default Package Name**: 앱의 패키지명 입력 (예: `com.studio.mygame`)
   - 비워두면 `TryVerifyGooglePlayPurchaseAsync` 호출 시 매번 `packageName` 인자를 전달해야 함
4. **(선택) Purchase Verify Google Function Name**: 기본값 `purchase-verify-google` 유지

### 사용 예시

```csharp
// 게임에서 구글 플레이 구매 후
var (success, response) = await Supabase.TryVerifyGooglePlayPurchaseAsync(
    purchaseToken: googlePlayPurchaseToken,  // Google Play에서 받은 토큰
    productId: "gem_pack_1",                 // 상품 ID
    packageName: "com.studio.mygame"         // 생략 시 SupabaseSettings의 기본값 사용
);

if (success && response.ok)
{
    if (response.already_verified)
    {
        Debug.Log("이미 검증된 영수증입니다.");
        return;
    }
    
    Debug.Log($"영수증 검증 성공. Order ID: {response.order_id}");
    // 여기서 보상 지급 로직 호출 (게임 서버 또는 클라이언트)
    await GrantRewardToPlayer(productId);
}
else
{
    Debug.LogError($"영수증 검증 실패: {response.reason}");
}
```

### API 참고

**`Supabase.TryVerifyGooglePlayPurchaseAsync`**

```csharp
public static Task<(bool success, GooglePlayPurchaseResponse value)> TryVerifyGooglePlayPurchaseAsync(
    string purchaseToken,
    string productId,
    string packageName = null)
```

- **반환값**: `(bool success, GooglePlayPurchaseResponse value)`
  - `success`: 영수증 검증 성공 여부 (네트워크·인증 오류도 false)
  - `value.ok`: 구글 플레이 API 응답 성공 여부
  - `value.already_verified`: 동일 `purchase_token`으로 이미 검증된 경우 true
  - `value.order_id`: Google Play Order ID
  - `value.purchase_state`: 0=구매완료, 1=취소, 2=대기
  - `value.reason`: 실패 사유 (예: `not_purchased`, `user_not_found` 등)

**로그 태그**: `[Supabase.Purchase.VerifyGoogle]`

### 트러블슈팅

| 문제 | 원인 | 해결 |
|------|------|------|
| `google_api_error_401` | GOOGLE_SERVICE_ACCOUNT_JSON이 잘못됨 | Supabase Secrets에 정확한 JSON 전체를 다시 입력 |
| `google_api_error_403` | Google Play Console에서 서비스 계정 권한 없음 | Play Console에서 서비스 계정 이메일을 **재무 담당자** 역할로 초대 |
| `not_purchased` | 구매 상태가 0(완료)이 아님 | 테스트: `purchase_state == 0`인 토큰만 검증됨. 테스트 구매 토큰 사용 |
| `already_verified: true` 반복 | 동일 토큰을 여러 번 검증 시도 | 정상 동작. 중복 방지 처리됨 |
| `user_not_found` | 로그인하지 않은 상태에서 호출 | `TrySignInAnonymouslyAsync` 등으로 먼저 로그인 |

### 샘플 사용 방법

1. **패키지 설치**  
   `Window` > `Package Manager`에서 `Truesoft Supabase SDK`를 선택합니다.

2. **샘플 가져오기**  
   패키지 상세 패널에서 **Samples** 목록을 펼칩니다.  
   - **Examples** 옆 **Import**를 누릅니다.

3. **실행 전 준비** (루트 README의 **준비** 섹션과 동일)  
   - `SupabaseSettings` 생성 후 `projectUrl`, `publishableKey` 입력  
   - `Assets/Resources/SupabaseSettings.asset` 경로·이름 유지  
   - Supabase 대시보드에서 **Anonymous sign-ins** 활성화 (Basic/Full 게스트 로그인 사용 시)

4. **씬 실행**  
   - 빈 씬 또는 테스트용 씬을 엽니다.  
   - Hierarchy에 빈 GameObject를 만들고 `ExampleSupabaseScenarios` 컴포넌트를 붙입니다.  
   - **Play** 시 `runOnStart`가 켜져 있으면 자동 실행됩니다.  
   - 수동 실행 시 인스펙터에서 컴포넌트 우클릭 → **Run All Examples** 또는 기능별 실행 메뉴를 사용합니다.

5. **결과 확인**  
   - Console에 `[Supabase.*]` 형태의 Try API 로그와 샘플 스크립트의 `[Sample]` 로그가 나오면 성공입니다.  
   - 초기화가 안 되면 Console의 **`[Supabase 초기화 점검]`** 을 따릅니다.

6. **샘플 제거**  
   - `Assets/Samples/Truesoft Supabase SDK/0.1.0/` (버전은 다를 수 있음) 폴더를 삭제하거나, Import한 샘플 폴더만 삭제합니다.

각 샘플 폴더 안의 `README.md`에도 동일한 내용을 요약해 두었습니다.

## 익명 계정과 Google 연동

- 연동은 자동으로 수행하지 않고, **별도 버튼 UX**에서만 호출하는 것을 권장합니다.
- 익명 세션에서 일반 `TrySignInWithGoogleAsync` / `TrySignInWithGoogleIdTokenAsync`를 호출하면 실패(`anonymous_session_requires_explicit_link`)합니다.
- Google 등 비익명으로 이미 로그인된 상태에서 `TrySignInAnonymouslyAsync`를 호출하면 실패(`signed_in_non_anonymous_sign_out_first`)합니다. 먼저 `TrySignOutFullyAsync` 등으로 로그아웃하세요.
- 공개 프로필/닉네임 조회(`TryGetPublicProfileAsync`, `TryGetPublicDisplayNameAsync`, `TryIsDisplayNameAvailableAsync`)는 `server_id` 격리 정책상 로그인 세션이 필요합니다.
- 익명 세션 연동은 `TryLinkGoogleToCurrentAnonymousAsync`(또는 ID 토큰 버전)를 사용하세요.
- 연동 성공 시에는 같은 `auth.users.id`를 유지하면서 `is_anonymous`가 false가 되어야 합니다.
- 연동하려는 Google 계정이 이미 다른 사용자에 연결되어 있으면 연동은 실패하며, 현재 익명 세션은 유지됩니다.
- 연동이 끝난 뒤 사용자가 다시 익명 로그인 버튼을 누르면, 기존 연동 계정을 복원하지 않고 **새 익명 계정**을 만듭니다.

## 문의 및 기여

이슈, 기능 제안, 버그 리포트는 GitHub Issue 탭을 통해 공유해 주세요.  
내부 프로젝트 확장이나 기능 추가가 필요한 경우 담당자에게 직접 문의 바랍니다.