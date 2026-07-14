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

## Architecture

The SDK has three layers:

**Core** (`Runtime/Core/`) — platform-agnostic, no Unity engine references:
- `Abstractions/` — `ISupabaseHttpClient`, `ISupabaseJsonSerializer` interfaces
- `Auth/` — `SupabaseAuthService`, `SupabaseAnonymousRecoveryService`, `SupabaseSessionChangeKind`
- `Config/` — `SupabaseOptions` (project URL, keys, table names, defaults)
- `Data/` — individual REST services (`SupabaseUserDataService`, `SupabaseRemoteConfigService`, `SupabaseChatService`, `SupabaseMailboxService`, `SupabaseEdgeFunctionsService`, `SupabasePublicProfileService`, `SupabaseServerTimeService`, `SupabaseUserSessionService`)
- `Models/` — `SupabaseSession`, `SupabaseUser`, `SupabaseResult<T>`

**Unity** (`Runtime/Unity/`) — Unity-specific wrappers:
- `Supabase.cs` — static entry point, all public-facing API
- `SupabaseSDK.cs` — MonoBehaviour singleton, all implementation
- `Config/SupabaseSettings.cs` — ScriptableObject for static values (URL, keys, table names). Must be saved to `Assets/Resources/SupabaseSettings.asset`.
- `Config/SupabaseRuntime.cs` — MonoBehaviour for scene lifecycle: RemoteConfig per-key polling, UserSave auto-sync. **로그인은 자동 실행되지 않음** — 개발자가 `Supabase.TriggerAutoLoginAsync()` 또는 직접 로그인 API를 원하는 타이밍에 호출. `public class` (non-sealed), `protected virtual Awake()` — 상속 가능. Optional but recommended.
- `Config/SupabaseUnityBootstrap.cs` — Auto-bootstraps from `Resources/SupabaseSettings` if no scene-placed runtime is present. Async APIs internally await initialization.
- Facades (`UserSavesFacade`, `RemoteConfigFacade`, `MailboxFacade`, `ChatChannelFacade`, `ServerFunctionsFacade`) — high-level auto-sync wrappers
- `Auth/Anonymous/DeviceFingerprintProvider.cs` — fingerprint for anonymous recovery
- `Auth/Google/` — `GoogleLoginBridge`, `AndroidGoogleLoginProvider` for Play Services OAuth
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

게임에 공개하는 API는 `Supabase` 파사드의 **접두어 없는 메서드**(`Supabase.GetMyMailsAsync()` 등)이며, **항상 result 타입을 반환**한다:
- **값·데이터를 돌려주는 호출** → `SupabaseResult<T>` (`.IsSuccess`·`.Data`로 분기)
- **성공/실패만 알리는 액션** → `SupabaseResult` (암묵적 `bool` 변환 제공 → `if (await Supabase.Xxx())` 패턴 동작, 실패 사유 포함)

실패 사유는 두 가지로 노출된다: **타입 안전 분기는 `.Reason`(`SupabaseFailCode` enum, `Runtime/Core/Models/SupabaseFailCode.cs`)**, **원문·로깅은 `.ErrorCode`(string)**. `Reason`은 `ErrorCode` 문자열에서 `SupabaseFailCodeMap.FromErrorCode`로 매핑되며(문자열 값 기준이라 호출부가 상수든 raw든 인식됨), 카탈로그 밖 동적 사유는 `Unknown`. `.Fail(...)`에 넘기는 인자는 여전히 문자열(`ErrorCode` 원문)이다. 새 사유 추가 시 `SupabaseFailReason` 상수·`SupabaseFailCode` enum·`FromErrorCode` 스위치 **세 곳을 함께** 갱신한다(enum·map은 Core, 상수는 Unity — Core는 Unity를 참조 못 하므로 문자열이 양쪽에 존재).

**bare value(`Task<string>`·`T`·리스트 원본 등)를 직접 반환하지 않는다. 공개 메서드에 `Try` 접두어를 쓰지 않는다.** 호출자가 성공/실패와 "결과 0개 vs 조회 실패"를 항상 구분할 수 있어야 한다.

`SupabaseResult`(액션)와 `SupabaseResult<T>`(데이터)는 하나의 타입 계층이다 — `SupabaseResult<T>`가 `SupabaseResult`를 상속하며, `SupabaseCallResult` 같은 별도 타입은 없다.

구현·로깅은 `SupabaseSDK`의 내부 계층이 담당한다: `SupabaseSDK.TryXxxAsync()`가 실제 호출 + 고정 태그(`[Supabase.UserData.LoadAttributed]` 등) 로깅 후 `SupabaseResult`/`SupabaseResult<T>`를 반환하고, 파사드 `Supabase.XxxAsync()`는 그 결과를 그대로 돌려준다. 실패 사유는 `SupabaseFailReason` 상수를 우선 사용하고, 없으면 추가한다. 이 규칙은 `Supabase.*`뿐 아니라 `StaticUserSave<TRow>`의 공개 메서드와 생성기가 emit하는 래퍼에도 동일 적용된다.

### account_id vs user_id

- `account_id` = `auth.users.id` — the current login session identity. Changes on re-auth/account swap.
- `user_id` — persistent player ID that survives re-authentication. Used for audit, analytics, and withdrawal handling.
- Game reads/writes always use `account_id` (matched by RLS `auth.uid()`). `user_id` is for ops tooling only.
- On account deletion, the DB row keeps `user_id` but `account_id` is set to NULL. Re-signup creates a **new row**; old saves are not auto-restored.

### User Saves (Diff Patching)

- Decorate C# fields with `[DataColumn("db_column_name")]` to map to PostgREST columns. Omit the argument to use the member name as the column name.
- Game-facing user-save API is `StaticUserSave<TRow>` and the generated wrapper class — not raw facade calls. Public methods: `LoadAsync()`, `SaveIfChangedAsync()`, `EnsureRowAsync()`, `RequestImmediateSave()`, `FlushNowAsync()` (all return `SupabaseResult`, no `Try` prefix), plus `MarkDirty()`. Events: `OnLoaded`(로드 성공 후), `OnFirstLoad`(신규 유저=DB 행 없던 최초 로드 시, 기본값 적용 후·첫 저장 전 — 여기서 초기값 세팅 시 diff가 서버에 저장됨).
- Auto-syncs on dirty with cooldown. Use `RequestImmediateSave()` or `FlushNowAsync()` for critical moments (scene change, logout, app quit).
- The attributed-load / diff-patch building blocks (`LoadUserDataAttributedAsync`, `LoadUserDataAttributedWithRowStateAsync`, `PatchUserDataDiffAsync`) are `internal` in `SupabaseSDK`/facade — `StaticUserSave` uses them internally to send only changed fields and skip the network when nothing changed.
- **Newtonsoft.Json:** SDK uses Newtonsoft.Json for deserialization. `[DataColumn("other_name")]` changes the select/PATCH key but does NOT change deserialization. If DB column name ≠ C# field name, also add `[JsonProperty("db_column_name")]`.

### Remote Config (Cold Start Pattern)

- No HTTP on app start. Config is lazy-loaded on first `RemoteConfig<T>.CreateReader()`/`CreateBinding()`/`CreateListener()` call.
- Uses stale-while-revalidate (`max_stale_seconds` from DB; 0 treated as 300s). Stale cache is returned immediately while background refresh runs.
- Per-key background polling via `poll_interval_seconds` (0 = no polling). `SupabaseRuntime` ticks polls in `Update`.
- `GetRemoteConfig` (sync, no `Async`) reads the in-memory cache without network.
- `RemoteConfigFacade` — manages polling lifecycle.
- Design: group related settings into one key as a JSON object (`{"stamina":{...},"battle":{...}}`), not one key per scalar.

### Authentication Flows

- Anonymous sign-in: `Supabase.TrySignInAnonymouslyAsync()`
- Google OAuth (Android): `TrySignInWithGoogleAsync()` via native Play Services (`GoogleLoginBridge`)
- Google OAuth (iOS/custom): `TrySignInWithGoogleIdTokenAsync(idToken)`
- Apple OAuth (ID token): `TrySignInWithAppleIdTokenAsync(idToken, rawNonce)` — 외부 SDK 없이 토큰 직접 전달
- Guest → Google linking: `TryLinkGoogleToCurrentAnonymousAsync()` or `TryLinkGoogleToCurrentAnonymousWithIdTokenAsync()`. Must use these — calling plain `TrySignInWithGoogleAsync` from an anonymous session returns `anonymous_session_requires_explicit_link`.
- Guest → Apple linking: `TryLinkAppleToCurrentAnonymousWithIdTokenAsync(idToken, rawNonce)`
- Session restore (수동): `Supabase.TriggerAutoLoginAsync()` — 자동 실행 없음, 원하는 타이밍에 직접 호출. **세션 복원만 하고 UserSave 로드는 안 함**(수동 로그인과 동일하게 성공 후 `LoadAllUserSavesAsync`/`LoadAsync` 직접 호출). 내부 orchestration은 `SupabaseSDK.TryTriggerAutoLoginAsync`(로그인+`OnAfterAutoLoginAsync` 훅), `SupabaseRuntime`이 훅 등록
- Sign-out: `TrySignOutFullyAsync()` (handles Android Google native logout + Supabase signout + anonymous recovery upsert).

### Table Names

All REST table names are configurable in `SupabaseSettings` and default in `SupabaseOptions`. Columns and query shape within each table are currently fixed in service code. Schema: `public` by default; use `schema.table` form for other schemas.

## Database Schema

SQL files are in `Sql/player/` (not directly in `Sql/`). Run in order in Supabase SQL Editor:

1. `01_servers.sql` — game_servers + ts_default_server_id + ts_server_now
2. `02_profiles.sql` — user_profiles + display_names + user_sessions
3. `03_anonymous_recovery.sql` — anonymous recovery tokens + auth triggers
4. `04_user_data.sql` — save infra (set_updated_at, ts_ensure_my_row) + user_data table + field protection
5. `05_account_management.sql` — server transfer RPCs + withdrawal RPCs
6. `06_mails.sql` — mails table + RPCs (hardening included)
7. `07_purchases.sql` — IAP receipt verification records
8. `08_remote_config.sql` — remote_config table
9. `09_cron_jobs.sql` — withdrawal_delete_queue + cron jobs (pg_cron required)

`99_verify.sql` — validation script.

`Sql/edge-functions/` — Deno Edge Function source for: `displayname-get`, `displayname-set`, `withdrawal-cancel-issue`, `withdrawal-cancel-redeem`, `withdrawal-guard`.

## Samples

`Samples~/Examples/` — full feature showcase. Import via Package Manager > Samples tab. Key file: `ExampleSupabaseScenarios.cs` with keyboard-shortcut-driven test flows. Samples are not compiled until imported.

`Samples~/PlayNanooMigration/` — PlayNANOO + SDK 병행 운영 런타임. `PlayNanooRuntime`은 구체 클래스(`SupabaseRuntime` 상속)로 씬에 직접 배치. `Supabase.GetNanooSaveBridge()`를 통해 `StaticUserSave<TRow>`(`INanooSaveSyncable` 구현)와 자동 연결, 서브클래스 파일 불필요. 스토리지 키는 Inspector `Nanoo Storage Key` 필드로 설정. Awake 시 인터셉터를 등록해 `Supabase.Try*` 호출이 PlayNanoo를 자동 경유. 게스트·구글·애플 로그인, 익명→구글·애플 연동(`TryLinkGoogle/AppleToCurrentAnonymousWithIdTokenAsync`), 로그아웃, 탈퇴/복구(`OnWithdrawalPending`·`OnWithdrawalRestored` 이벤트), `updated_at` 기반 데이터 동기화 포함. PlayNanoo 제거 시 이 파일만 삭제, 게임 코드 변경 없음.

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

### 7. No parenthetical asides in headings, tables, or steps

Do **not** append parenthetical clarifications to headings, table cells, or numbered steps.

- ❌ `# 인증 (Auth)`, `### 클래스 생성기 (선택)`, `| (자동 생성됨) |`
- ✅ `# 인증`, `### 클래스 생성기`, or move the aside to prose/callout

If the information matters, state it as a separate sentence or callout box. If it doesn't, omit it.

예외: 파라미터 표의 `(기본값: x)`처럼 **값을 정의하는 표준 표기**는 아사이드가 아니므로 허용한다(Rule 8·9).

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

**실패 원인**  ← 의미 있는 실패 원인이 없으면 생략

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.상수명` | 설명 (상수가 있으면 우선 사용, 없으면 raw 문자열) |
```

| 항목 | 규칙 |
|------|------|
| 헤딩 | 페이지 H1(서술형 기능 제목). 메서드명을 그대로 쓰지 않는다. 오버로드는 별도 페이지 |
| 시그니처 | `Supabase.`(또는 `SupabaseIAP.`) 접두어 사용. 항상 포함, 수식어(`public`/`static`/`async`) 제외 |
| 파라미터 표 | 타입 열 없이 2열. `(기본값: x)` 표기 포함 |
| 반환 표 | `isSuccess` / `Success` 생략. 직접 반환 타입이나 `.Data` 프로퍼티만 기술 |
| 실패 원인 | `SupabaseFailReason` 상수 우선, 없으면 raw 문자열. 게임은 동일 이름의 `SupabaseFailCode` enum(`.Reason`)으로 분기 |

### 10. H1 아래 본문에는 반드시 H2를 붙인다

VitePress 우측 책갈피(outline)는 `H2`(`## `)부터 표시하고 `H1`(페이지 제목)은 제외한다. 따라서 **H1 바로 아래에 헤딩 없이 실질 본문이 떠 있으면 그 내용은 책갈피에 안 잡혀 최상단 항목이 누락된다.**

H1과 첫 `## ` 사이에 **코드블록 / 표 / 2단락 이상**이 있으면, 그 상단 본문에도 `## ` 제목을 붙인다.

- ❌ `# 자동 로그인` 바로 아래 코드 예제 → 그 뒤 첫 H2만 책갈피에 뜸
- ✅ `# 자동 로그인` → `## 자동 로그인 호출`(코드 포함) → `## 로그인 후 사용 가능한 값`

예외 — **한 줄짜리 도입 문장**이나 `:::` 콜아웃만 있는 경우는 책갈피가 불필요하므로 H2를 붙이지 않는다. 페이지에 H2가 하나도 없는 단일 주제 문서(예: 단일 함수 페이지)도 그대로 둔다(책갈피 자체가 숨겨짐).

### 11. 기능 페이지 캐노니컬 구조 — 코드 우선

API 색인이 링크하는 기능 페이지들은 **어느 페이지를 열어도 같은 구조**여야 한다. 핵심: 헤딩 바로 다음에 코드 시그니처가 와서 **코드가 눈에 띄어야** 한다 — **헤딩과 코드 사이에 설명 문장을 넣지 않는다**(설명이 길면 코드가 묻힘).

1. **함수 블록 순서** (Rule 9): 헤딩 → ```csharp 시그니처(`반환타입 메서드(...)`, `public`/`static`/`async` 등 수식어 제외) → **한 줄 설명(코드 아래)** → 파라미터 표 → 반환 → 실패 원인. 코드 앞에 도입문/설명을 두지 않는다.
2. **단일 함수 페이지**: `# 기능명` 직후 바로 시그니처. 부가 맥락(왜 쓰는지·주의사항)은 코드 아래 설명에 합치거나 `:::` 콜아웃으로 페이지 끝에 둔다.
3. **다중 함수 페이지**: 함수가 여러 개라 코드가 많아지면 **폴더로 쪼갠다** — `<기능>/index.md`(개요 + 메서드 나열 표·결정 표로 각 페이지에 링크) + **메서드마다 별도 페이지(코드 블록 1개)**. 한 페이지에 시그니처를 2개 이상 두지 않는다. 예: `social/google/{index,setup,signin-android,signin-ios,link-android,...}`. 사이드바는 `기능`을 접이식 그룹으로 만들고 하위에 각 메서드 페이지를 둔다.
4. 한 페이지 안에서 함수마다 같은 요소(파라미터/반환/실패 원인)는 **있으면 모두, 없으면 모두** 일관되게.
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
