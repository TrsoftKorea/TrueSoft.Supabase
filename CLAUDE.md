# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TrueBase SDK — Unity UPM package (`com.truesoft.supabase`) for integrating Supabase services into Unity games. Targets Unity 2022.3+. Written in C# 11+. Distributed via Git URL, no npm/build scripts — Unity compiles the source directly.

## Supabase 프로젝트 규칙

- **ProjectR (`wxivrmvtpufeczltward`)** — 실제 게임 프로젝트. SQL 변경 시 Supabase MCP로 이 프로젝트에만 직접 적용한다.
- **`Samples~/DatabaseSetup/SQL/player/`** 의 SQL 파일은 SDK 사용자(개발자)가 자신의 프로젝트에 직접 실행하도록 안내하는 문서다. Claude가 임의로 다른 프로젝트에 적용하지 않는다.

## Retool 수정 규칙

- Retool 파일 수정 요청 시 **반드시 `retool_read_react_app_files`로 실제 파일을 먼저 읽은 후** 작업한다.
- 수정된 파일은 **전체 코드**를 제공한다. 부분 코드(diff/snippet)만 제공하지 않는다.
- 코드 제공 시 **파일 경로(예: `/frontend/pages/data/ColumnManagementTab.tsx`)를 반드시 명시**한다.
- **기존 파일 수정** 시 전체 코드를 제공해 사용자가 붙여넣도록 한다. `retool_create_or_append_react_app_thread_message`를 사용하지 않는다.
- **신규 파일 생성** 시 `retool_create_or_append_react_app_thread_message`를 사용해 직접 생성한다. (AI 크레딧 소모를 최소화하기 위해 생성 목적으로만 사용하고, 코드 생성은 Claude가 직접 작성한다.)

## Unity-Specific Rules

- **Never create `.meta` files manually.** Unity auto-generates them for every asset. Adding them by hand causes conflicts.
- No build commands exist in this repo. Unity compiles C# source directly when the package is imported into a Unity project.
- The SDK has no test runner. Validation is done via `Samples~/Examples/ExampleSupabaseScenarios.cs` (keyboard-shortcut-driven test flows in Play Mode).
- **Core 레이어 컴파일 검증**: `Runtime/Core/`는 UnityEngine 의존이 0(`noEngineReferences: true`)이라 Unity 없이 `dotnet build Tools~/CoreCompileCheck/CoreCompileCheck.csproj`로 컴파일 오류를 즉시 확인할 수 있다. Core 변경 후 실행 권장(경고 0 = 정상). `Runtime/Unity/`·`Editor/`는 UnityEngine 의존이라 이 도구로 검증 불가 — Unity 재컴파일 필요.
- **규칙 정적 검사**: `dotnet run --project Tools~/SdkAudit` — 공개 표면(`internal` 멤버·`Try` 접두어·result 타입), 파사드 리셋 대칭성, 문서 정합성(없는 API·시그니처·기본값·`SupabaseReason` 표기), 문서 형식(알림 문법·헤딩 괄호·책갈피 누락·수식어), 샘플 최신성, 미참조 공개 API, `install.sql` 설치 순서를 검사한다. Roslyn 구문 파싱이라 `Runtime/Unity`도 UnityEngine 없이 본다. 파사드·공개 API·문서·`install.sql`을 손댄 뒤 실행한다. 규칙의 예외는 검사기에 반영해야 오탐이 안 난다 — `Tools~/SdkAudit/README.md`의 예외 표 참고.

## Architecture

The SDK has three layers:

**Core** (`Runtime/Core/`) — platform-agnostic, no Unity engine references:
- `Abstractions/` — `ISupabaseHttpClient`, `ISupabaseJsonSerializer` interfaces
- `Auth/` — `SupabaseAuthService`, `SupabaseAnonymousRecoveryService`, `SupabaseSessionChangeKind`
- `Config/` — `SupabaseOptions` (project URL, keys, table names, defaults)
- `Data/` — individual REST services (`SupabaseUserDataService`, `SupabaseRemoteConfigService`, `SupabaseMailboxService`, `SupabaseLeaderboardService`, `SupabaseCouponService`, `SupabaseChatService`, `SupabaseEdgeFunctionsService`, `SupabasePublicProfileService`, `SupabaseServerTimeService`, `SupabaseUserSessionService`)
- `Models/` — `SupabaseSession`, `SupabaseUser`, `SupabaseResult<T>`

**Unity** (`Runtime/Unity/`) — Unity-specific wrappers:
- `Supabase.cs` — static entry point, all public-facing API
- `SupabaseSDK.cs` — **`internal static class`**, all implementation. 어셈블리 밖(샘플·게임)에서는 보이지 않는다
- `SupabaseBridge.cs` — 어셈블리 밖 통합 런타임이 SDK 내부에 훅을 꽂는 전용 진입점. PlayNANOO 인터셉터·IAP 인터셉터·세이브 브리지 등록이 여기 있다. 게임이 부르는 API가 아니다
- `Config/SupabaseSettings.cs` — ScriptableObject for static values (URL, keys, table names). Must be saved to `Assets/Resources/SupabaseSettings.asset`.
- `Config/SupabaseRuntime.cs` — MonoBehaviour for scene lifecycle: RemoteConfig per-key polling, UserSave auto-sync. **로그인은 자동 실행되지 않음** — 개발자가 `Supabase.TriggerAutoLoginAsync()` 또는 직접 로그인 API를 원하는 타이밍에 호출. `public class` (non-sealed), `protected virtual Awake()` — 상속 가능. Optional but recommended.
- `Config/SupabaseUnityBootstrap.cs` — Auto-bootstraps from `Resources/SupabaseSettings` if no scene-placed runtime is present. Async APIs internally await initialization.
- Facades (`UserSavesFacade`, `RemoteConfigFacade`, `MailboxFacade`, `LeaderboardFacade`, `CouponFacade`, `ChatFacade`, `ServerFunctionsFacade`) — high-level wrappers. **전부 `internal`**이며 `SupabaseSDK`의 `internal` 프로퍼티로만 접근한다 — 게임은 `Supabase` 파사드만 본다. `ChatFacade`는 구독 폴링(`ChatSubscription`)까지 들고 있다
- **게임이 직접 쓰지 않는 타입은 `internal`로 둔다.** HTTP·JSON 구현, 부트스트랩, 로그인 브리지, 로그인 결과 DTO가 여기 해당한다. 게임에 남는 공개 타입은 게임이 **직접 인스턴스를 들고 있는 것**(`ChatSubscription`·`RemoteConfigListener`·IAP 파사드·`StaticUserSave`·`SupabaseSettings` 등)뿐이다
- `Auth/Anonymous/DeviceFingerprintProvider.cs` — fingerprint for anonymous recovery
- `Auth/Google/` — `GoogleLoginBridge`, `AndroidGoogleLoginProvider` for Play Services OAuth
- `Auth/Apple/` — `AppleLoginBridge`, `AppleLoginResult` for Sign in with Apple
- `Http/UnitySupabaseHttpClient.cs` — `UnityWebRequest` implementation of `ISupabaseHttpClient`
- `Json/UnitySupabaseJsonSerializer.cs` — Newtonsoft.Json implementation of `ISupabaseJsonSerializer`

**Editor** (`Editor/`) — Unity editor tooling:
- `SupabaseSetupMenu.cs` — creates `SupabaseSettings.asset` via `TrueSoft > Supabase > 설정 에셋 만들기`
- `SupabaseUserSaveClassGeneratorWindow.cs` — generates C# model classes from Supabase OpenAPI schema (`TrueSoft > Supabase > 유저 데이터 클래스 생성`)

### Assembly Definitions

- `TrueBase.Core.asmdef` — Core only, no UnityEngine references
- `TrueBase.Unity.asmdef` — Unity layer, depends on Core
- `TrueBase.Editor.asmdef` — Editor tools only

## Key Concepts

### SupabaseResult\<T\> and Try API Pattern

게임에 공개하는 API는 `Supabase` 파사드의 **접두어 없는 메서드**(`Supabase.GetMailsAsync()` 등)이며, **항상 result 타입을 반환**한다:
- **값·데이터를 돌려주는 호출** → `SupabaseResult<T>` (`.IsSuccess`·`.Data`로 분기)
- **성공/실패만 알리는 액션** → `SupabaseResult` (암묵적 `bool` 변환 제공 → `if (await Supabase.Xxx())` 패턴 동작, 실패 사유 포함)

실패 사유는 두 가지로 노출된다: **타입 안전 분기는 `.Reason`(`SupabaseReason` enum, `Runtime/Core/Models/SupabaseReason.cs`)**, **원문·로깅은 `.ErrorCode`(string)**. `Reason`은 `ErrorCode` 문자열에서 `SupabaseReasonMap.FromErrorCode`로 매핑되며(문자열 값 기준이라 호출부가 상수든 raw든 인식됨), 카탈로그 밖 동적 사유는 `Unknown`. `.Fail(...)`에 넘기는 인자는 여전히 문자열(`ErrorCode` 원문)이다. 카탈로그는 전부 Core에 있다(`SupabaseErrorCode` 상수·`SupabaseReason` enum·`FromErrorCode` 모두 `Runtime/Core/Models/`). **`SupabaseErrorCode` 상수는 `internal`(SDK 전용)** — 게임은 `SupabaseReason` enum(`.Reason`)으로만 분기하고, 실패 원문은 `.ErrorCode` 문자열로 읽는다. Core 밖 SDK 계층(TrueBase.Unity·TrueBase.Unity.IAP)은 `InternalsVisibleTo`로 `SupabaseErrorCode`에 접근한다(`Runtime/Core/Models/SupabaseErrorCode.cs` 상단). 새 사유는 **`SupabaseErrorCode` 상수 + `SupabaseReason` enum 멤버** 두 곳만 추가하면 된다 — `FromErrorCode` 스위치는 상수를 직접 참조(`SupabaseErrorCode.X => SupabaseReason.X`)하므로 에러코드 문자열은 상수에만 존재한다. 상수가 Core에 있어 Core 서비스도 raw 문자열 대신 `SupabaseErrorCode.X`를 쓴다(카탈로그 밖 저수준 검증 사유만 raw 문자열 유지). 갱신 후 `dotnet run --project Tools~/FailReasonCheck`로 정합성·죽은 사유를 자동 검증한다. 새 사유는 `docs~/guide/api/fail-reasons.md`(에러코드 전체 카탈로그)에도 추가한다.

**실패의 로그 레벨은 "누구 탓인가"로 정한다.** `SupabaseSDK.IsExpectedFailureReason`에 등록된 사유는 `Debug.Log`(일반)로, 나머지는 `Debug.LogError`로 나간다. 게임이 문구로 안내할 사유를 콘솔에 빨갛게 띄우면 진짜 오류가 묻히기 때문이다.

| 분류 | 예 | 로그 |
|------|-----|------|
| 유저가 입력·상태 때문에 정상 거절됨 | 글자 수 초과, 채팅 차단 중, 쿨타임, 쿠폰 코드 틀림·이미 사용 | 일반 |
| 게임·설정이 잘못됨 | 없는 채널 코드, 프로필에 서버 없음, 등록 안 된 리더보드 필드 | Error |
| 인프라 문제 | 네트워크, 파싱 실패 | Error |

**새 사유를 추가할 때 이 분류를 함께 판단한다.** 유저 탓이면 `IsExpectedFailureReason`의 switch에 추가한다. `LogAndReturnResult`를 거치는 API는 자동 적용되지만, `LogApiResult`를 직접 부르는 곳은 `errorOnFail: !IsExpectedFailureReason(...)`을 명시해야 한다.

**사유가 있으면 반드시 `IsSuccess = false`로 반환한다.** 호출자가 `Reason`을 확인할 수 있어야 하기 때문이다. 오류가 아닌 상황도 예외 없이 실패로 내보낸다 — 유저 세이브 저장 3종(`SaveIfChangedAsync`·`SaveNowAsync`·`RequestSave`)은 보낼 변경분이 없으면 `UserSaveNoChanges` 실패를 반환한다. 이런 사유를 내부에서 다시 소비하는 코드(예: `LoadAsync`의 로드 후 초기 저장)는 `Reason`으로 걸러 경고 로그를 내지 않는다. 사유 없는 성공은 `SupabaseResult.Ok`뿐이다.

**bare value(`Task<string>`·`T`·리스트 원본 등)를 직접 반환하지 않는다. 공개 메서드에 `Try` 접두어를 쓰지 않는다.** 호출자가 성공/실패와 "결과 0개 vs 조회 실패"를 항상 구분할 수 있어야 한다.

적용 범위는 **게임이 직접 부르는 모든 공개 API**다 — `Supabase.*`, `SupabaseIAP.*`, `StaticUserSave<TRow>`의 공개 메서드, 생성기가 emit하는 래퍼, `RemoteConfig<T>`. 배선 전용인 `SupabaseBridge`는 게임 API가 아니라 대상이 아니다.

핸들을 돌려주는 팩토리(`CreateListener()` 등)는 런타임에 실패할 여지가 없어 result로 감싸지 않는다. `[RemoteConfigKey]` 누락처럼 개발 실수는 예외로 던진다.

**읽기 API를 팩토리로 감싸지 않는다.** 해제할 것도 상태도 없는 읽기는 정적 메서드 하나로 노출한다 — 게임이 필드를 들고 있을 이유가 없고, 클로저가 인자를 가둬 두면 같은 키에 설정이 다른 인스턴스를 만들 수 있는 것처럼 보이지만 실제로는 키 단위 설정을 서로 덮어쓴다.

`SupabaseResult`(액션)와 `SupabaseResult<T>`(데이터)는 하나의 타입 계층이다 — `SupabaseResult<T>`가 `SupabaseResult`를 상속하며, `SupabaseCallResult` 같은 별도 타입은 없다.

**`Supabase` 파사드에는 게임에 공개하는 API만 둔다(`internal` 멤버 금지).** 파사드에 내부 배선을 섞으면 게임이 볼 공개 표면이 흐려진다. 배선을 둘 곳은 호출자가 어디 있느냐로 갈린다.

| 호출자 | 참조 대상 |
|--------|-----------|
| 어셈블리 안(`Runtime/Unity`) | `SupabaseSDK`를 직접 |
| 어셈블리 밖(`Samples~`·통합 런타임) | `SupabaseBridge` |

`SupabaseSDK`는 `internal`이라 어셈블리 밖에서 보이지 않는다. 밖에서 필요한 배선은 **`Supabase`가 아니라 `SupabaseBridge`에** 추가한다.

구현·로깅은 `SupabaseSDK`의 내부 계층이 담당한다: `SupabaseSDK.TryXxxAsync()`가 실제 호출 + 고정 태그(`[Supabase.UserData.LoadAttributed]` 등) 로깅 후 `SupabaseResult`/`SupabaseResult<T>`를 반환하고, 파사드 `Supabase.XxxAsync()`는 그 결과를 그대로 돌려준다. 실패 사유는 `SupabaseErrorCode` 상수를 우선 사용하고, 없으면 추가한다. 이 규칙은 `Supabase.*`뿐 아니라 `StaticUserSave<TRow>`의 공개 메서드와 생성기가 emit하는 래퍼에도 동일 적용된다.

### account_id vs user_id

- `account_id` = `auth.users.id` — the current login session identity. Changes on re-auth/account swap.
- `user_id` — persistent player ID that survives re-authentication. Used for audit, analytics, and withdrawal handling.
- Game reads/writes always use `account_id` (matched by RLS `auth.uid()`). `user_id` is for ops tooling only.
- On account deletion, the DB row keeps `user_id` but `account_id` is set to NULL. Re-signup creates a **new row**; old saves are not auto-restored.

### User Saves (Diff Patching)

- Decorate C# fields with `[DataColumn("db_column_name")]` to map to PostgREST columns. Omit the argument to use the member name as the column name.
- Game-facing user-save API은 **`Supabase` 파사드**다: `LoadUserSaveAsync()`·`SaveNowAsync()`·`RequestSave()`·`SaveIfChangedAsync()`·`DeleteUserSaveAsync()` (모두 result 타입 반환, `Try` 접두어 없음). 행 존재 보장·전체 저장은 로드·저장 경로가 알아서 처리하므로 파사드에 없다. 구현은 `StaticUserSave<TRow>`의 동명 메서드이고, 파사드는 `IUserSaveOperations`(`Runtime/Unity/IUserSaveOperations.cs`)로 위임한다 — 서브클래스가 생성자에서 `SupabaseSDK._userSave`에 자신을 등록한다. **생성 클래스에는 `Row`와 정적 프로퍼티(+`MarkDirty`)만 emit한다** — 조작 API 래퍼를 다시 넣지 말 것. SDK가 저장 API를 바꿔도 생성 파일이 깨지지 않게 하려는 의도적 분리이며, 생성 파일은 DB 컬럼이 바뀔 때만 재생성한다. 로드 완료는 `await LoadAsync()` 반환으로 감지(별도 완료 이벤트 없음). `LoadAsync()`는 `SupabaseLoadResult`(SupabaseResult 파생, `Runtime/Core/Models/SupabaseLoadResult.cs`)를 반환하며 `.IsNewUser`(신규 유저=DB 행 없던 최초 로드 여부, 호출에 묶여 불변)로 신규 유저 후처리를 분기. 로드 전 초기값(fallback): 로그인 후·로드 전에 **컬렉션(jsonb) 컬럼**에 값을 세팅하면 로드 시 병합. **Auto\* 컬렉션(AutoList·AutoDict·2D)은 요소 단위 병합** — 서버에 **비기본값**이 든 인덱스·키는 유지, **서버에 없거나 값이 기본값**인 슬롯/키만 fallback으로 채움(리스트 3→4 확장 시 기존 실제값 유지, 빈/기본 슬롯만 채움). "기본값"은 `[AutoDefault]`(없으면 타입 기본값) 기준, 참조 타입 원소는 값 비교 불가라 서버 인스턴스 있으면 유지. 서버 SQL NULL이면 fallback 전체 적용. 스칼라 미지원(DB DEFAULT). 내부: `DataSchema.MergeServerOverFallback`가 `ApplyRow`에서 병합 — Auto\* 멤버는 복제본에 `[AutoDefault]` 레시피 복원(JSON 복제로 소실) 후 `IAutoDefaultable.FillMissingFrom`으로 채움. fallback 미설정 시 `CopyInto`와 동일.
- Auto-syncs on dirty with cooldown. Use `RequestSave()` or `SaveNowAsync()` for critical moments (scene change, logout, app quit). 세이브 타입을 모르는 내부 훅은 `SupabaseSDK.TrySaveAllAsync()`를 쓴다 — 파사드에 없다.
- The attributed-load / diff-patch building blocks (`TryLoadUserDataAttributedWithRowStateAsync`, `TryPatchUserDataDiffAsync`) live on `SupabaseSDK` only — `StaticUserSave` uses them to send only changed fields and skip the network when nothing changed. 파사드에는 노출하지 않는다.
- **Newtonsoft.Json:** SDK uses Newtonsoft.Json for deserialization. `[DataColumn("other_name")]` changes the select/PATCH key but does NOT change deserialization. If DB column name ≠ C# field name, also add `[JsonProperty("db_column_name")]`.

### Remote Config (Cold Start Pattern)

- No HTTP on app start. Config is lazy-loaded on first `RemoteConfig<T>.GetAsync()`/`CreateListener()` call.
- Uses stale-while-revalidate (`max_stale_seconds` from DB; 0 treated as 300s). Stale cache is returned immediately while background refresh runs.
- Per-key background polling via `poll_interval_seconds` (0 = no polling). `SupabaseRuntime` ticks polls in `Update`.
- 게임 표면은 `RemoteConfig<T>` 둘뿐이다: `GetAsync()`(1회 읽기, `SupabaseResult<T>` 반환), `CreateListener()`(변경 시 콜백). 자주 읽어야 하면 게임이 리스너 콜백에서 자기 필드에 담아 두고 그 필드를 읽는다 — SDK가 값을 들고 있어 주는 경로(구 `CreateBinding`)는 없앴다. 첫 fetch 전에 `null`을 돌려줘 게임이 NRE를 맞는 구조였고, 리스너 + 필드 하나면 같은 일을 게임 기본값으로 안전하게 할 수 있기 때문이다.
- Design: group related settings into one key as a JSON object (`{"stamina":{...},"battle":{...}}`), not one key per scalar.

### Authentication Flows

파사드 메서드에는 `Try` 접두어가 없다. `SupabaseSDK`의 동명 `Try*`는 내부 구현이다.

- Anonymous sign-in: `Supabase.SignInAnonymouslyAsync()`
- Google OAuth (Android): `SignInWithGoogleAsync()` via native Play Services (`GoogleLoginBridge`)
- Google OAuth (iOS/custom): `SignInWithGoogleIdTokenAsync(idToken)`
- Apple OAuth (ID token): `SignInWithAppleIdTokenAsync(idToken, rawNonce)` — 외부 SDK 없이 토큰 직접 전달
- Guest → Google linking: `LinkGoogleToGuestAsync()` or `LinkGoogleToGuestWithIdTokenAsync()`. Must use these — calling plain `SignInWithGoogleAsync` from an anonymous session returns `anonymous_session_requires_explicit_link`.
- Guest → Apple linking: `LinkAppleToGuestWithIdTokenAsync(idToken, rawNonce)`
- Session restore (수동): `Supabase.TriggerAutoLoginAsync()` — 자동 실행 없음, 원하는 타이밍에 직접 호출. **세션 복원만 하고 UserSave 로드는 안 함**(수동 로그인과 동일하게 성공 후 `Supabase.LoadUserSaveAsync()` 직접 호출). 내부 orchestration은 `SupabaseSDK.TryTriggerAutoLoginAsync`(로그인+`OnAfterAutoLoginAsync` 훅), `SupabaseRuntime`이 훅 등록
- Sign-out: `SignOutFullyAsync()` (handles Android Google native logout + Supabase signout + anonymous recovery upsert).

### Table Names

All REST table names are configurable in `SupabaseSettings` and default in `SupabaseOptions`. Columns and query shape within each table are currently fixed in service code. Schema: `public` by default; use `schema.table` form for other schemas.

## Database Schema

스키마 전체가 `Samples~/DatabaseSetup/SQL/player/install.sql` 한 파일에 있습니다. Supabase SQL Editor에 붙여넣고 한 번 실행하면 설치가 끝납니다. `verify.sql`은 설치 검증용입니다.

절 구성은 다음과 같습니다. 1~18은 기능별 스키마이고 **19는 반드시 마지막**입니다.

1. 게임 서버 — game_servers + ts_default_server_id + ts_server_now
2. 플레이어 프로필 — user_profiles + display_names + user_sessions
3. 익명 계정 복구 — recovery tokens + auth triggers
4. 유저 데이터 — save infra (set_updated_at, ts_ensure_my_row) + user_data + field protection
5. 계정 관리 — server transfer RPCs + withdrawal RPCs
6. 우편함 — mails + RPCs
7. 인앱 결제 — purchases
8. 원격 설정 — remote_config
9. 크론 잡 — withdrawal_delete_queue + cron (pg_cron required)
10. 계정 차단 — user_ban_messages + ban RPCs
11. 유저 데이터 로그 — user_data_logs + change-diff trigger
12. 어드민 우편 — game_items + mail_batches + ts_admin_send_mail
13. 우편 예약 — mail_schedules + cron 기반 예약·반복 발송
14. 우편 분류 — mail_categories
15. 리더보드 — 정의·기록·컬럼 등록 + 순위 RPC
16. 운영자 스키마 버전관리 — 스테이징 → 게시 → 롤백. service_role 전용, 클라이언트 grant 없음
17. 쿠폰 — coupons + coupon_codes + coupon_redemptions + ts_coupon_redeem
18. 채팅 — chat_channels + chat_messages + chat_mutes + ts_chat_send·ts_chat_fetch_many
19. 클라이언트 권한 최소화 — anon·authenticated 테이블·함수 권한 회수 후 허용 목록만 부여

**19절은 앞 절에서 만든 함수를 이름으로 grant 하므로 순서를 바꾸면 "함수가 없다"며 실패합니다.** 또 Supabase 기본 권한은 새 테이블마다 anon·authenticated에 ALL(TRUNCATE 포함)을 부여하고 TRUNCATE는 RLS를 우회하므로, 이 절이 `ALTER DEFAULT PRIVILEGES`로 **기본 권한 자체를 차단**합니다.

**새 테이블·함수를 추가할 때**는 권한이 없는 상태로 생성되므로, 클라이언트 접근이 필요하면 해당 절에서 `grant select ... to authenticated` / `grant execute on function ... to authenticated`처럼 필요한 권한만 명시합니다. RLS 정책만 만들고 grant를 빠뜨리면 PostgREST가 권한 오류를 냅니다. 클라이언트에 여는 함수라면 19절의 허용 목록에도 추가하세요.

**DB를 고칠 때는 MCP 적용과 `install.sql` 수정을 항상 한 세트로** 합니다. 파일을 "최초 설치 전용"으로 취급해 갱신을 미루면 파일과 라이브 DB가 갈라지고, 그 차이는 다음 신규 프로젝트에서야 드러납니다(실제로 `mails.localized`·`ts_coupon_redeem`에서 두 번 발생).

::: warning revoke 대상에 anon·authenticated를 반드시 포함
`revoke all on function x from public`만 쓰면 PUBLIC 유사롤만 회수되고, Supabase 기본 권한이 anon·authenticated에 **직접 부여한** EXECUTE는 그대로 남습니다. 이 때문에 SECURITY DEFINER 관리 함수가 anon에 노출돼 있었습니다. 항상 `from public, anon, authenticated`로 회수하세요.
:::

`Samples~/DatabaseSetup/EdgeFunctions/` — Deno Edge Function source for: `admin-displayname-set`, `displayname-get`, `displayname-set`, `get-ban-info`, `purchase-verify-apple`, `purchase-verify-apple-legacy`, `purchase-verify-google`, `withdrawal-cancel-issue`, `withdrawal-cancel-redeem`, `withdrawal-guard`.

## Samples

`Samples~/Examples/` — full feature showcase. Import via Package Manager > Samples tab. Key file: `ExampleSupabaseScenarios.cs` with keyboard-shortcut-driven test flows. Samples are not compiled until imported.

`Samples~/PlayNanooMigration/` — PlayNANOO + SDK 병행 운영 런타임. `PlayNanooRuntime`은 구체 클래스(`SupabaseRuntime` 상속)로 씬에 직접 배치. `SupabaseSDK.GetNanooSaveBridge()`를 통해 `StaticUserSave<TRow>`(`INanooSaveSyncable` 구현)와 자동 연결, 서브클래스 파일 불필요. **인터셉터·브리지 등록은 `Supabase` 파사드가 아니라 `SupabaseSDK`를 직접 부른다** — 게임에 공개하는 API가 아니기 때문이다. 스토리지 키는 Inspector `Nanoo Storage Key` 필드로 설정. Awake 시 인터셉터를 등록해 `Supabase.Try*` 호출이 PlayNanoo를 자동 경유. 게스트·구글·애플 로그인, 익명→구글·애플 연동(`TryLinkGoogle/AppleToGuestWithIdTokenAsync`), 로그아웃, 탈퇴 예약·취소 포함 — 취소는 별도 메서드 없이 표준 `Supabase.RedeemWithdrawalCancelAsync()`를 인터셉터로 감싸 나누 복구(`WithDrawalRestore`)까지 자동 처리하고, `OnWithdrawalPending`(파라미터 없음) 이벤트로 감지 시점을 알린다. `updated_at` 기반 데이터 동기화 포함. PlayNanoo 제거 시 이 파일만 삭제, 게임 코드 변경 없음.

## IAP 코딩 규칙

### LogTag 명명

IAP 파사드마다 `LogTag`를 override해 로그 출처를 구분한다.

| 클래스 | LogTag |
|--------|--------|
| `BaseIAPFacade` (기본값) | `[Supabase.IAP]` |
| `IAPFacade` | `[Supabase.IAP]` (기본값 사용) |
| `AppleIAPFacade` | `[Supabase.IAP.Apple]` |
| `GooglePlayIAPFacade` | `[Supabase.IAP.Google]` |

새 플랫폼별 파사드를 추가할 때는 반드시 `protected override string LogTag`를 정의한다.

### 로그 메시지 형식

모든 로그는 `$"{LogTag} 메시지"` 형식. `productId`를 알 수 있는 시점이면 항상 포함한다.

| 상황 | 포함할 필드 | 예시 |
|------|------------|------|
| receipt / token 파싱 실패 | `product={productId}` | `purchaseToken 추출 실패. product={productId}` |
| 서버 검증 실패 (네트워크·응답 이상) | `product={productId}` | `서버 검증 실패. product={productId}` |
| 서버 검증 거부 (ok=false) | `reason={response.reason}, product={productId}` | `구매를 거부했습니다. reason={...}, product={...}` |
| 구매 실패 이벤트 (OnPurchaseFailed) | `product={productId}, reason={failureReason}` | `구매 실패: product={...}, reason={...}` |

`productId`를 아직 모르는 시점(null 체크 등)은 생략해도 된다.

### 로그 레벨

- `Debug.LogWarning` — 구매 흐름 이상 (파싱 실패, 서버 거부, 타임아웃 등 복구 가능)
- `Debug.LogError` — 내부 예외 (`OnGrantItemAsync` 예외, `ProcessPurchaseAsync` 예외)
- `Debug.LogError` — 설정 오류로 절대 동작할 수 없는 상황 (예: Unity IAP 5.0.x + PlayNanooRuntime)

---

## Debug Logs

Temporary debug/session log files (e.g., `debug-*.log`) go at the **workspace root** (`d:\Project\TrueSoft.Supabase`), never under `Runtime/`, `Sql/`, or `Samples~/`. Do not commit them.

## Documentation Rules

All user-facing docs live in `docs~/guide/`. Apply these rules on every code change — do not wait to be asked.

### 1. Update docs alongside code

When adding, changing, or removing a feature, update the corresponding `docs~/guide/*.md` in the **same task**:
- New API or behavior → add/update the relevant guide page.
- Removed API, file, or Secret key → remove every reference to it across all doc files.
- Changed parameter names or signatures → update code examples in the docs.

### 2. Dead link prevention

Whenever a doc file or section is removed or renamed:
1. Search all `docs~/guide/*.md` for links pointing to the old file/anchor.
2. Remove or update every match before finishing the task.

Korean heading anchors are unreliable in VitePress. Any heading that is **linked to from elsewhere** must have an explicit anchor ID:
```md
## 더 알아보기 {#more}   ← link target
[더 알아보기](#more)      ← link
```
Do NOT rely on auto-generated Korean slugs like `#더-알아보기`.

### 3. Callout box style — VitePress `:::` only

**Never use** GitHub-style alerts (`> [!NOTE]`, `> [!TIP]`, `> [!WARNING]`, `> [!IMPORTANT]`, `> [!CAUTION]`). VitePress does not render them correctly.

Always use VitePress custom containers:

| 용도 | 컨테이너 |
|------|---------|
| 팁 / 추천 사항 | `::: tip` |
| 중립적 참고 정보 | `::: info` |
| 주의 / 경고 / 중요 | `::: warning` |
| 위험 / 데이터 손실 가능성 | `::: danger` |
| 접을 수 있는 부가 설명 | `::: details 제목` |

```md
::: warning
`SupabaseSettings.asset`은 반드시 `Assets/Resources/` 하위에 있어야 합니다.
:::

::: tip iOS 배포 대상 자동 설정
SDK가 빌드 시 자동으로 15.0으로 설정합니다.
:::
```

### 4. What goes in a callout box

Use callout boxes for **supplementary content** — content the reader can skip on first read but needs for edge cases:
- 주의사항 (warnings, "반드시 ~하세요") → `::: warning`
- 팁 / 자동 처리 안내 → `::: tip`
- 참고 / 동작 방식 보충 → `::: info`
- 긴 선택적 내용 → `::: details`

Core usage (the happy path) must remain as **plain prose + code blocks**, not buried in callout boxes.

### 5. Link directly to the target section

When referencing a specific section in another doc, link directly to the section anchor — never link to the page and name the section in surrounding text.

```md
❌ [빠른 시작](./getting-started.md)의 **Database Setup** 절차를 먼저 완료하세요.
✅ [Database Setup](./getting-started.md#database-setup) 절차를 먼저 완료하세요.

❌ [빠른 시작](./getting-started.md)의 Edge Function 배포가 완료되어 있어야 합니다.
✅ [Edge Function 배포](./getting-started.md#edge-function-deploy)가 완료되어 있어야 합니다.
```

If the target heading contains Korean, add an explicit anchor ID to the heading first (see Rule 2).

### 6. Sample display names — English only

`package.json`의 `samples[].displayName`은 영어만 사용한다. 한글 단독 또는 한영 혼용 이름은 금지. 예: `"PlayNANOO Migration"` (O), `"PlayNANOO 이관"` (X).

### 7. No parenthetical asides in headings, tables, steps, or prose

Do **not** append parenthetical clarifications to headings, table cells, or numbered steps.

- ❌ `# 인증 (Auth)`, `### 클래스 생성기 (선택)`, `| (자동 생성됨) |`
- ✅ `# 인증`, `### 클래스 생성기`, or move the aside to prose/callout

If the information matters, state it as a separate sentence or callout box. If it doesn't, omit it.

**산문 본문에서도 쓸데없는 부연 괄호를 쓰지 않는다.** 값·타입·이유를 괄호로 덧붙이지 말고, 인라인으로 풀거나 중요하면 별도 문장으로 쓴다.

- ❌ `삭제 예정 시각(DateTimeOffset)`, `탈퇴가 완료(계정 삭제)되면`, `로그인 없이 호출됩니다(publishable 키)`, `삭제 예정 시각(WithdrawnAt)`
- ✅ `삭제 예정 시각`, `탈퇴가 완료되어 계정이 삭제되면`, `로그인 없이 호출됩니다`, `삭제 예정 시각 WithdrawnAt`

예외: 코드 식·마크다운 링크(`(./cancel)`)·파라미터 표의 `(기본값: x)`처럼 **문법·표준 표기**는 아사이드가 아니므로 허용한다(Rule 8·9).

하위 유형을 구분할 땐 **괄호도 em-dash(`—`)도 쓰지 않고 가운뎃점(`·`)**을 쓴다. 헤딩·사이드바 항목·표 라벨 모두 동일.

- ❌ `### 탈퇴 취소 — 토큰 방식`, `### 신규 로그인 — Android`, 사이드바 `탈퇴 취소 (토큰)`
- ✅ `### 탈퇴 취소 · 토큰 방식`, `### 신규 로그인 · Android`, 사이드바 `탈퇴 취소 · 토큰`

**헤딩은 짧은 한글 서술형으로 유지한다.** 긴 메서드명·한영 혼용을 헤딩에 넣으면 우측 책갈피(outline)에서 이름이 잘리고 가독성이 떨어진다.

- 메서드명을 헤딩에 직접 쓰지 않는다(Rule 9). 메서드명은 코드 시그니처에서 보여준다.
- 불필요한 영어 병기를 빼고 한글 서술형으로 쓴다. 짧은 플랫폼·고유명사 토큰(`Android`·`iOS`·`Google`·`Apple` 등)은 허용하되, 한글 설명과 영어 식별자를 한 헤딩에 뒤섞지 않는다.
- ❌ `### iOS · 커스텀 — TrySignInWithGoogleIdTokenAsync`
- ✅ `### iOS 로그인 · 커스텀`

### 8. Show all parameters in code examples

함수 페이지는 **항상 시그니처로 시작**한다(Rule 11, 코드 우선). 시그니처는 `반환타입 Supabase.메서드(...)` 형태로 쓰고 `public`/`static`/`async` 등 수식어는 뺀다. 파라미터가 여러 줄이면 정렬해 가독성을 높인다.

```csharp
Task<SupabaseResult<IAPFacade>> SupabaseIAP.CreateIAPAsync(
    string[]                              productIds,
    Func<string, bool, bool, Task<bool>>  onGrant,
    Action<IAPPurchaseFailedInfo>          onFailed  = null,
    int                                   timeoutMs = 10_000)
```

파라미터 표는 타입 열 없이 `| 파라미터 | 설명 |` 2열로. 모든 파라미터를 표기하되 이름만으로 의미가 명확하면 생략 가능, optional은 `(기본값: x)` 표기.

When a signature changes, update **all** matching examples in `docs~/guide/`.

### 9. 가이드 함수 블록 형식

함수 블록의 **본문 구성**이다. 헤딩 레벨은 Rule 11이 정한다(단일 함수/메서드 페이지의 `#` 제목, 메서드명이 아니라 **서술형 기능 제목**). 해당 항목이 없으면 섹션 자체를 생략한다.

```md
# 기능명   ← 페이지 H1(서술형, Rule 11)

```csharp
반환타입 Supabase.메서드명(타입 파라미터, ...)
```

한 줄 설명.

**파라미터**  ← 파라미터가 없으면 섹션 전체 생략

| 파라미터 | 설명 |
|----------|------|
| `name` | 설명. optional이면 `(기본값: x)` 표기 |

**반환**  ← `SupabaseResult`(성공·실패만)이면 생략. 값을 돌려주면(`Task<string>`·상태 객체·`T` 등) **반드시 기술** — 프로퍼티가 여럿이면 표로, 단일 값이면 한 줄로

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|

**에러 코드**  ← 의미 있는 에러 코드가 없으면 생략

| Reason | 설명 |
|--------|------|
| `SupabaseReason.멤버명` | 설명 |
```

| 항목 | 규칙 |
|------|------|
| 헤딩 | 페이지 H1(서술형 기능 제목). 메서드명을 그대로 쓰지 않는다. 오버로드는 별도 페이지 |
| 시그니처 | `Supabase.`(또는 `SupabaseIAP.`) 접두어 사용. 항상 포함, 수식어(`public`/`static`/`async`) 제외 |
| 파라미터 표 | 타입 열 없이 2열. `(기본값: x)` 표기 포함 |
| 반환 표 | `isSuccess` / `Success` 생략. 직접 반환 타입이나 `.Data` 프로퍼티만 기술 |
| 에러 코드 | 표에 `SupabaseReason` enum 멤버를 나열(게임은 `.Reason`으로 분기). 문자열 카탈로그 `SupabaseErrorCode`는 internal이라 게임 문서에 노출하지 않는다 |

### 10. H1 아래 본문에는 반드시 H2를 붙인다

VitePress 우측 책갈피(outline)는 `H2`(`## `)부터 표시하고 `H1`(페이지 제목)은 제외한다. 따라서 **H1 바로 아래에 헤딩 없이 실질 본문이 떠 있으면 그 내용은 책갈피에 안 잡혀 최상단 항목이 누락된다.**

H1과 첫 `## ` 사이에 **코드블록 / 표 / 2단락 이상**이 있으면, 그 상단 본문에도 `## ` 제목을 붙인다.

- ❌ `# 자동 로그인` 바로 아래 코드 예제 → 그 뒤 첫 H2만 책갈피에 뜸
- ✅ `# 자동 로그인` → `## 자동 로그인 호출`(코드 포함) → `## 로그인 후 사용 가능한 값`

예외 — **한 줄짜리 도입 문장**이나 `:::` 콜아웃만 있는 경우는 책갈피가 불필요하므로 H2를 붙이지 않는다. 페이지에 H2가 하나도 없는 단일 주제 문서(예: 단일 함수 페이지)도 그대로 둔다(책갈피 자체가 숨겨짐).

### 11. 기능 페이지 캐노니컬 구조 — 코드 우선

API 색인이 링크하는 기능 페이지들은 **어느 페이지를 열어도 같은 구조**여야 한다. 핵심: 헤딩 바로 다음에 코드 시그니처가 와서 **코드가 눈에 띄어야** 한다 — **헤딩과 코드 사이에 설명 문장을 넣지 않는다**(설명이 길면 코드가 묻힘).

1. **함수 블록 순서** (Rule 9): 헤딩 → ```csharp 시그니처(`반환타입 메서드(...)`, `public`/`static`/`async` 등 수식어 제외) → **한 줄 설명(코드 아래)** → 파라미터 표 → 반환 → 에러 코드. 코드 앞에 도입문/설명을 두지 않는다.
2. **단일 함수 페이지**: `# 기능명` 직후 바로 시그니처. 부가 맥락(왜 쓰는지·주의사항)은 코드 아래 설명에 합치거나 `:::` 콜아웃으로 페이지 끝에 둔다.
3. **다중 함수 페이지**: 함수가 여러 개라 코드가 많아지면 **폴더로 쪼갠다** — `<기능>/index.md`(개요 + 메서드 나열 표·결정 표로 각 페이지에 링크) + **메서드마다 별도 페이지(코드 블록 1개)**. 한 페이지에 시그니처를 2개 이상 두지 않는다. 예: `social/google/{index,setup,signin-android,signin-ios,link-android,...}`. 사이드바는 `기능`을 접이식 그룹으로 만들고 하위에 각 메서드 페이지를 둔다.
4. 한 페이지 안에서 함수마다 같은 요소(파라미터/반환/에러 코드)는 **있으면 모두, 없으면 모두** 일관되게.
5. **본문에 장식용 수평선(`---`)을 쓰지 않는다** — H1 도입문과 본문 사이, `##` 섹션들 사이 모두. `##`와 여백이 구분 역할을 한다. (예외: 이미지가 많은 단계별 절차 페이지의 단계 구분 `---`은 허용.)

### 12. 표 칸이 세로로 줄바꿈되지 않게 한다

마크다운 표는 열 너비를 지정할 수 없어 브라우저가 칸을 최대한 좁히려고 **공백마다 줄바꿈**한다. 결정 표(`| 상황 | ... |`)처럼 한 열에 짧은 헤더 + 긴 한글 문구가 들어가면 그 문구가 글자 단위로 세로로 쌓여 가독성이 떨어진다.

**한 줄로 유지해야 하는 셀의 공백은 `&nbsp;`로 바꾼다.** 그러면 그 열이 문구의 자연 너비만큼 넓어진다.

```md
❌ | 로그인된 계정에 추가 연동 | ... |
✅ | 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | ... |
```

- 적용 대상: `상황`·`용도`처럼 **짧은 헤더 + 긴 설명 문구**가 한 셀에 들어가는 결정·분기 표의 해당 열.
- 적용 제외: 첫 열이 공백 없는 코드 식별자(`TrySignInAnonymouslyAsync` 등)인 API 색인 표는 애초에 쌓이지 않으므로 손대지 않는다.
- 마지막(가장 넓은) 설명 열은 줄바꿈돼도 되므로 `&nbsp;` 처리하지 않는다 — 좁아서 세로로 쌓이는 **앞쪽 좁은 열**에만 적용한다.
