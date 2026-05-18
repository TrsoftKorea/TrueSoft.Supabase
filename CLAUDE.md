# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity UPM (Unity Package Manager) SDK (`com.truesoft.supabase`) for integrating Supabase services into Unity games. Targets Unity 2022.3+. Written in C# 11+. Distributed via Git URL, no npm/build scripts — Unity compiles the source directly.

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
- `Config/SupabaseRuntime.cs` — MonoBehaviour for scene lifecycle: session restore on start, RemoteConfig per-key polling. Optional but recommended.
- `Config/SupabaseUnityBootstrap.cs` — Auto-bootstraps from `Resources/SupabaseSettings` if no scene-placed runtime is present. Async APIs internally await initialization.
- Facades (`UserSavesFacade`, `RemoteConfigFacade`, `MailboxFacade`, `ChatChannelFacade`, `ServerFunctionsFacade`) — high-level auto-sync wrappers
- `Auth/Anonymous/DeviceFingerprintProvider.cs` — fingerprint for anonymous recovery
- `Auth/Google/` — `GoogleLoginBridge`, `AndroidGoogleLoginProvider` for Play Services OAuth
- `Http/UnitySupabaseHttpClient.cs` — `UnityWebRequest` implementation of `ISupabaseHttpClient`
- `Json/UnitySupabaseJsonSerializer.cs` — `JsonUtility` + Newtonsoft bridge

**Editor** (`Editor/`) — Unity editor tooling:
- `SupabaseSetupMenu.cs` — creates `SupabaseSettings.asset` via `TrueSoft > Supabase > 설정 에셋 만들기`
- `SupabaseUserSaveClassGeneratorWindow.cs` — generates C# model classes from Supabase OpenAPI schema (`TrueSoft > Supabase > 유저 데이터 클래스 생성`)

### Assembly Definitions

- `Truesoft.Supabase.Core.asmdef` — Core only, no UnityEngine references
- `Truesoft.Supabase.Unity.asmdef` — Unity layer, depends on Core
- `Truesoft.Supabase.Editor.asmdef` — Editor tools only

## Key Concepts

### SupabaseResult\<T\> and Try API Pattern

Every data API comes in two forms:
- `Supabase.LoadUserSaveAttributedAsync<T>()` → returns `SupabaseResult<T>` (check `.IsSuccess`, `.Data`, `.ErrorMessage`)
- `Supabase.TryLoadUserSaveAttributedAsync<T>()` → returns `(bool success, T value)`, logs internally with a fixed tag like `[Supabase.UserData.LoadAttributed]`

Use `Try*` variants in game code. Use the non-Try variants when you need to inspect `SupabaseResult` directly.

### account_id vs user_id

- `account_id` = `auth.users.id` — the current login session identity. Changes on re-auth/account swap.
- `user_id` — persistent player ID that survives re-authentication. Used for audit, analytics, and withdrawal handling.
- Game reads/writes always use `account_id` (matched by RLS `auth.uid()`). `user_id` is for ops tooling only.
- On account deletion, the DB row keeps `user_id` but `account_id` is set to NULL. Re-signup creates a **new row**; old saves are not auto-restored.

### User Saves (Diff Patching)

- Decorate C# fields with `[UserSaveColumn("db_column_name")]` to map to PostgREST columns. Omit the argument to use the member name as the column name.
- `Supabase.TryLoadUserSaveAttributedAsync<T>()` — loads only mapped columns.
- `Supabase.TryLoadUserSaveAttributedWithRowStateAsync<T>()` — additionally returns `hasRow` to distinguish new user (empty array) from auth failure.
- `Supabase.TryPatchUserSaveDiffAsync(previous, current)` — sends only changed fields; skips network if nothing changed.
- `UserSavesFacade` — auto-syncs on dirty with cooldown. Use `TryRequestImmediateSave()` or `TryFlushNowAsync()` for critical moments.
- **JsonUtility constraint:** PostgREST JSON keys must match C# field names exactly. `[UserSaveColumn("other_name")]` changes the select/PATCH key but does NOT change JSON deserialization. If DB column name ≠ C# field name, use Newtonsoft or a manual mapping.

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
- Guest → Google linking: `TryLinkGoogleToCurrentAnonymousAsync()` or `TryLinkGoogleToCurrentAnonymousWithIdTokenAsync()`. Must use these — calling plain `TrySignInWithGoogleAsync` from an anonymous session returns `anonymous_session_requires_explicit_link`.
- Sign-out: `TrySignOutFullyAsync()` (handles Android Google native logout + Supabase signout + anonymous recovery upsert).

### Table Names

All REST table names are configurable in `SupabaseSettings` and default in `SupabaseOptions`. Columns and query shape within each table are currently fixed in service code. Schema: `public` by default; use `schema.table` form for other schemas.

## Database Schema

SQL files are in `Sql/player/` (not directly in `Sql/`). Run in order in Supabase SQL Editor:

1. `01_game_servers.sql` — server sharding/selection
2. `02_profiles.sql` — public profiles
3. `03_display_names.sql` — unique display name index (`lower(trim(...))`, excludes empty)
4. `04_user_saves.sql` — save table infrastructure (user_table_metadata, set_updated_at, ts_ensure_my_row, admin_create_user_table)
5. `05_user_sessions.sql` — duplicate login detection
6. `06_anonymous_recovery.sql` — guest fingerprints
7. `07_server_transfer.sql` — server_id sync triggers + server transfer RPCs
8. `09_withdrawal.sql` — account_closures + profiles_withdrawn_at index + ts_withdrawal_cancel_redeem
9. `11_remote_config.sql`, `12_mails.sql`, `13_mails_hardening.sql` (optional), `14_purchases.sql`, `15_cron_jobs.sql`

`99_verify_player_schema.sql` — validation script. `Sql/supabase_player_tables.sql` — combined/ordered list for reference.

`Sql/edge-functions/` — Deno Edge Function source for: `displayname-get`, `displayname-set`, `withdrawal-cancel-issue`, `withdrawal-cancel-redeem`, `withdrawal-guard`.

## Samples

`Samples~/Examples/` — full feature showcase. Import via Package Manager > Samples tab. Key file: `ExampleSupabaseScenarios.cs` with keyboard-shortcut-driven test flows. Samples are not compiled until imported.

## Debug Logs

Temporary debug/session log files (e.g., `debug-*.log`) go at the **workspace root** (`d:\Project\TrueSoft.Supabase`), never under `Runtime/`, `Sql/`, or `Samples~/`. Do not commit them.
