# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TrueBase SDK — Unity UPM package (`com.truesoft.supabase`) for integrating Supabase services into Unity games. Targets Unity 2022.3+. Written in C# 11+. Distributed via Git URL, no npm/build scripts — Unity compiles the source directly.

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

`Samples~/PlayNanooMigration/` — PlayNanoo + SDK 병행 운영 런타임. `PlayNanooRuntime`은 구체 클래스(`SupabaseRuntime` 상속)로 씬에 직접 배치. `Supabase.GetNanooSaveBridge()`를 통해 `StaticUserSave<TRow>`(`INanooSaveSyncable` 구현)와 자동 연결, 서브클래스 파일 불필요. 스토리지 키는 Inspector `Nanoo Storage Key` 필드로 설정. Awake 시 인터셉터를 등록해 `Supabase.Try*` 호출이 PlayNanoo를 자동 경유. 게스트·구글·애플 로그인, 익명→구글·애플 연동(`TryLinkGoogle/AppleToCurrentAnonymousWithIdTokenAsync`), 로그아웃, 탈퇴/복구(`OnWithdrawalPending`·`OnWithdrawalRestored` 이벤트), `updated_at` 기반 데이터 동기화 포함. PlayNanoo 제거 시 이 파일만 삭제, 게임 코드 변경 없음.

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

### 5. No parenthetical asides in tables or steps

Do **not** append parenthetical clarifications like `(자동 생성됨)`, `(reveal 불필요)`, `(선택 사항)` to table cells or numbered steps. They hurt readability. If the information matters, state it as a separate sentence or callout box; if it doesn't, omit it.
