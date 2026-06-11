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
- `Config/SupabaseRuntime.cs` — MonoBehaviour for scene lifecycle: RemoteConfig per-key polling, UserSave auto-sync. **로그인은 자동 실행되지 않음** — 개발자가 `SupabaseRuntime.TriggerAutoLoginAsync()` 또는 직접 로그인 API를 원하는 타이밍에 호출. `public class` (non-sealed), `protected virtual Awake()` — 상속 가능. Optional but recommended.
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

Every data API comes in two forms:
- `Supabase.LoadUserDataAttributedAsync<T>()` → returns `SupabaseResult<T>` (check `.IsSuccess`, `.Data`, `.ErrorMessage`)
- `Supabase.TryLoadUserDataAttributedAsync<T>()` → returns `T`, logs internally with a fixed tag like `[Supabase.UserData.LoadAttributed]`

Use `Try*` variants in game code. Use the non-Try variants when you need to inspect `SupabaseResult` directly.

### account_id vs user_id

- `account_id` = `auth.users.id` — the current login session identity. Changes on re-auth/account swap.
- `user_id` — persistent player ID that survives re-authentication. Used for audit, analytics, and withdrawal handling.
- Game reads/writes always use `account_id` (matched by RLS `auth.uid()`). `user_id` is for ops tooling only.
- On account deletion, the DB row keeps `user_id` but `account_id` is set to NULL. Re-signup creates a **new row**; old saves are not auto-restored.

### User Saves (Diff Patching)

- Decorate C# fields with `[DataColumn("db_column_name")]` to map to PostgREST columns. Omit the argument to use the member name as the column name.
- `Supabase.TryLoadUserDataAttributedAsync<T>()` — loads only mapped columns.
- `Supabase.TryLoadUserDataAttributedWithRowStateAsync<T>()` — additionally returns `hasRow` to distinguish new user (empty array) from auth failure.
- `Supabase.TryPatchUserDataDiffAsync(previous, current)` — sends only changed fields; skips network if nothing changed.
- `StaticUserSave<TRow>` — recommended pattern; auto-syncs on dirty with cooldown. Use `TryRequestImmediateSave()` or `TryFlushNowAsync()` for critical moments.
- **Newtonsoft.Json:** SDK uses Newtonsoft.Json for deserialization. `[DataColumn("other_name")]` changes the select/PATCH key but does NOT change deserialization. If DB column name ≠ C# field name, also add `[JsonProperty("db_column_name")]`.

### Remote Config (Cold Start Pattern)

- No HTTP on app start. Config is lazy-loaded on first `Supabase.GetRemoteConfigAsync<T>(key)`.
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
- Session restore (수동): `SupabaseRuntime.TriggerAutoLoginAsync()` — 자동 실행 없음, 원하는 타이밍에 직접 호출
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

All user-facing docs live in `docs/guide/`. Apply these rules on every code change — do not wait to be asked.

### 1. Update docs alongside code

When adding, changing, or removing a feature, update the corresponding `docs/guide/*.md` in the **same task**:
- New API or behavior → add/update the relevant guide page.
- Removed API, file, or Secret key → remove every reference to it across all doc files.
- Changed parameter names or signatures → update code examples in the docs.

### 2. Dead link prevention

Whenever a doc file or section is removed or renamed:
1. Search all `docs/guide/*.md` for links pointing to the old file/anchor.
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

### 5. Sample display names — English only

`package.json`의 `samples[].displayName`은 영어만 사용한다. 한글 단독 또는 한영 혼용 이름은 금지. 예: `"PlayNANOO Migration"` (O), `"PlayNANOO 이관"` (X).

### 6. No parenthetical asides in headings, tables, or steps

Do **not** append parenthetical clarifications to headings, table cells, or numbered steps.

- ❌ `# 인증 (Auth)`, `### 클래스 생성기 (선택)`, `| (자동 생성됨) |`
- ✅ `# 인증`, `### 클래스 생성기`, or move the aside to prose/callout

If the information matters, state it as a separate sentence or callout box. If it doesn't, omit it. For sub-type headings use an em-dash instead: `### 탈퇴 취소 — 토큰 방식`.

### 7. Show all parameters in code examples

기본 구성: **설명** → **파라미터 표** (필요 시) → **예시 코드**

**시그니처 블록은 타입이 복잡할 때만** 추가한다. 판단 기준:
- ✅ 추가: 델리게이트(`Func<...>`, `Action<...>`), 제네릭이 2단계 이상, 파라미터가 5개 이상
- ❌ 생략: `string`, `bool`, `int` 조합의 단순 파라미터

```md
// ❌ 단순 — 시그니처 불필요
await Supabase.TrySignOutFullyAsync(clearStorage: true, deleteUserSessionRow: true);

// ✅ 복잡 — 시그니처 추가
\`\`\`csharp
public static async Task<IAPFacade> CreateIAPAsync(
    string[]                              productIds,
    Func<string, bool, bool, Task<bool>>  onGrant,
    Action<IAPPurchaseFailedInfo>          onFailed  = null,
    int                                   timeoutMs = 10_000)
\`\`\`
```

파라미터 표는 타입 열 없이 `| 파라미터 | 설명 |` 2열로. 이름만으로 의미가 명확한 파라미터는 생략한다.

When a signature changes, update **all** matching examples in `docs/guide/`.

### 8. 가이드 함수 블록 형식

`docs/guide/`의 각 공개 함수는 `####` 제목 + 아래 섹션으로 기술한다. 해당 항목이 없으면 섹션 자체를 생략한다.

```md
#### `메서드명(파라미터 나열)`

```csharp
반환타입 Supabase.메서드명(타입 파라미터, ...)
```

한 줄 설명.

**파라미터**  ← 파라미터가 없으면 섹션 전체 생략

| 파라미터 | 설명 |
|----------|------|
| `name` | 설명. optional이면 `(기본값: x)` 표기 |

**반환**  ← isSuccess / bool 만이면 생략. 의미 있는 프로퍼티가 있을 때만

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|

**실패 원인**  ← 의미 있는 실패 원인이 없으면 생략

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.상수명` | 설명 (상수가 있으면 우선 사용, 없으면 raw 문자열) |
```

| 항목 | 규칙 |
|------|------|
| 섹션 제목 | 함수명을 `` ` `` 로 감싸 구분. 오버로드는 별도 블록 |
| 시그니처 | `Supabase.` 접두어 사용. Rule 7과 무관하게 항상 포함 |
| 파라미터 표 | 타입 열 없이 2열. `(기본값: x)` 표기 포함 |
| 반환 표 | `isSuccess` / `Success` 생략. 직접 반환 타입이나 `.Data` 프로퍼티만 기술 |
| 실패 원인 | `SupabaseFailReason` 상수 우선, 없으면 raw 문자열 |
