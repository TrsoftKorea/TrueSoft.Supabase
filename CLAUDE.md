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
- **규칙 정적 검사**: `dotnet run --project Tools~/SdkAudit` — 작업이 끝나면 Stop 훅이 `--if-changed` 로 자동 실행한다(검사 대상 경로가 바뀌었을 때만). **이 파일(CLAUDE.md)과 스킬도 검사 대상이다**(가리키는 API·사유가 실존하는지). 그 밖에 공개 표면(`internal` 멤버·`Try` 접두어·result 타입), 파사드 리셋 대칭성, 문서 정합성(없는 API·시그니처·기본값·`SupabaseReason` 표기), 문서 형식(알림 문법·헤딩 괄호·책갈피 누락·수식어), 샘플 최신성, 미참조 공개 API, `install.sql` 설치 순서를 검사한다. Roslyn 구문 파싱이라 `Runtime/Unity`도 UnityEngine 없이 본다. 파사드·공개 API·문서·`install.sql`을 손댄 뒤 실행한다. 규칙의 예외는 검사기에 반영해야 오탐이 안 난다 — `Tools~/SdkAudit/README.md`의 예외 표 참고.

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

## Debug Logs

Temporary debug/session log files (e.g., `debug-*.log`) go at the **workspace root** (`d:\Project\TrueSoft.Supabase`), never under `Runtime/`, `Sql/`, or `Samples~/`. Do not commit them.

## 경로별 규칙

아래 규칙은 해당 파일을 읽을 때만 로드된다. 이 파일이 길수록 지시 준수율이 떨어지므로 분리했다.

| 파일 | 적용 시점 | 내용 |
|------|-----------|------|
| `.claude/rules/docs.md` | `docs~` 를 읽을 때 | 문서 작성 규칙 12개 |
| `.claude/rules/iap.md` | `Runtime/Unity/IAP` 를 읽을 때 | LogTag 명명·로그 형식·로그 레벨 |

**문서를 새로 만들 때는 기존 파일을 읽지 않아 규칙이 자동 로드되지 않는다.** `docs~`에 새 페이지를 추가하기 전에 `.claude/rules/docs.md`를 직접 읽는다.

## 자동 메모리

자동 메모리는 홈이 아니라 **`.claude/memory/`**(리포지토리 안)에 둔다. `settings.local.json`의 `autoMemoryDirectory`가 그리로 가리킨다. 설계 결정·운영 노하우가 코드와 함께 커밋돼 다른 PC에서도 따라온다.

**새 PC에서는 그 한 줄을 직접 넣어야 한다** — 절대 경로만 허용돼서 커밋되는 `settings.json`에는 넣을 수 없다.

```json
{ "autoMemoryDirectory": "<리포지토리 절대경로>/.claude/memory" }
```

