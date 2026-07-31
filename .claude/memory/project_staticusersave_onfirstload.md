---
name: project_staticusersave_onfirstload
description: StaticUserSave 신규 유저 초기화 — OnFirstLoad 제거 → IsNewUser 플래그 + 로드 전 fallback 병합
metadata: 
  node_type: memory
  type: project
  originSessionId: 81278906-80cd-40f2-8a71-c24bedcd5493
---

**OnFirstLoad 이벤트 제거(2026-07-15), 두 기능으로 분리.** (이전엔 `OnFirstLoad` event가 신규 유저 초기값을 담당했으나 삭제.)

**① IsNewUser (로드 후 후처리) — 반환값에 담음(프로퍼티 아님):** `LoadAsync()` 반환 타입을 `SupabaseResult` → **`SupabaseLoadResult`(SupabaseResult 파생, `Runtime/Core/Models/SupabaseLoadResult.cs`)** 로 변경. `SupabaseLoadResult.Success(bool isNewUser)`/`new Fail(...)` 팩토리(SupabaseResult<T>의 Success/new Fail 패턴 미러). `var r = await PlayerSave.LoadAsync(); if (r.IsNewUser)`. **호출에 묶여 불변**(재로드해도 그 결과 안 바뀜) — 프로퍼티(가변)를 안 쓴 이유. 생성기 LoadAsync 래퍼 반환 타입도 `Task<...SupabaseLoadResult>`. **결정 배경:** SDK가 `SingletonGuard`로 세이브 클래스 **정확히 1개만** 허용(StaticUserSave.cs:60, "모든 데이터를 하나의 Row에") → 여러 세이브 없음 → `LoadAllUserSavesAsync`는 그 하나를 로드하는 얇은 집계 래퍼(bool 반환, IsNewUser 미노출). 그래서 신규 처리는 `PlayerSave.LoadAsync()` 결과 사용. (사용자가 "프로퍼티 대신 반환 클래스에 담자" 선택.)

**② 로드 전 fallback 병합 (컬렉션 전용, 하위호환/마이그레이션):** 로그인 후·로드 전에 컬렉션에 값 세팅(`PlayerSave.Items.EnsureCount(5); Items[2]=x`) → 로드 시 **서버에 값 있으면 서버 우선, 서버가 SQL NULL이면 세팅값 유지**. 신규·복귀·게임 업데이트로 컬럼 추가 모두 커버. **결정: 컬렉션/참조 타입만**(스칼라는 "없음 vs 기본값" 구분 불가 → DB DEFAULT로). **서버 SQL NULL일 때만 적용**(빈 `[]`는 값 있음 = 서버 우선). 대상 jsonb 컬럼은 **DB DEFAULT를 NULL로** 둬야 함.

**구현 핵심(StaticUserSave.cs):**
- `ApplyRow`는 **단일 경로 유지**(플래그·분기 없음). 내부 `CopyInto` → `DataSchema.MergeServerOverFallback(Current, row, _loadFallback)`로 교체. **`_loadFallback` null(미캡처)이면 CopyInto와 바이트 단위 동일** → 복사의 엄격한 상위집합, Nanoo 3경로(`NanooPatchFrom*`)도 인자 그대로라 동작 불변.
- `MergeServerOverFallback<T>`(DataSchema.cs): 매핑 멤버 순회, `MemberValueType.IsValueType`면 항상 serverRow. 참조 중 **Auto\* 컬렉션(IAutoDefaultable, server·fallback 둘 다 non-null)은 요소 병합**(2026-07-15). server·fallback 복제 → **복제본에 `[AutoDefault]` 레시피 복원**(`m.GetCustomAttribute<AutoDefaultAttribute>()` → `SetDefaultValue`, JSON 복제로 소실됨) → `IAutoDefaultable.FillMissingFrom(fbClone)`. 그 외 참조는 `serverVal ?? fallback`. **`FillMissingFrom`(기본값=빈곳):** AutoList=인덱스 없거나 `base[i]==FreshDefault()`면 `this[i]=fallback[i]`(EqualityComparer<T>.Default), AutoDict=키 없거나 값=FreshDefault면 채움, AutoList2D/AutoDict2D=행/안쪽 dict로 재귀(1D의 기본값=빈곳 상속, 레시피는 SetDefaultValue가 PropagateDefault). 인터페이스 `IAutoDefaultable`(AutoDefaultAttribute.cs)에 `FillMissingFrom(object)` 추가. **효과:** 서버에 없거나 값이 기본값인 슬롯/키를 fallback으로 채움, 서버의 비기본값·서버가 더 큰 tail은 유지. **참조 타입 원소**(클래스, Equals 미오버라이드)는 EqualityComparer가 참조비교라 서버에 **non-null 인스턴스** 있으면 기본값이어도 유지(값 타입·구조체만 값비교로 기본값=빈곳). **단 `null` 원소는 명시적 null 체크로 항상 빈 곳 처리**(2026-07-15 추가, `base[i] != null`·`sv != null` 가드) — `[AutoDefault]` non-null 기본값이어도 null 슬롯/값은 fallback으로 채움. AutoList·AutoDict에 적용, 2D는 재귀로 상속.
- fallback은 **첫 LoadAsync에서 1회 스냅샷**(`_loadFallback = CloneRow(Current)`, `_fallbackCaptured`) → 얼려둠. 재로그인 시 `ResetLocalState`가 Current 리셋해도 스냅샷 유지 → 이전 플레이 데이터 부활 방지. (`_loadFallback`/`_fallbackCaptured`는 로그아웃에도 유지.)
- **조기 저장 게이트:** 프로퍼티 setter가 `MarkDirty` 호출하므로 로드 전 세팅이 auto-flush로 서버에 조기 저장될 위험 → `HasDirty` 상단에 `if (!_hasLoadedOnce) return false;`. `_hasLoadedOnce`는 LoadAsync 끝에 true, ResetLocalState에서 false.
- `LoadAsync`: fallback 캡처 → fetch → `_isNewUser` → (신규면 EnsureMyRow+재로드) → `ApplyRow(row)` → **무조건 `SaveIfChangedAsync()`**(유지된 fallback/신규 diff 저장; 없으면 patch 비어 no-op) → `_hasLoadedOnce=true`. `SaveIfChangedAsync`는 BuildPatch 직접 사용, HasDirty 게이트 무관.

**touch point:** 생성기 `PostgrestOpenApiUserSaveClass.cs`(OnFirstLoad 래퍼 삭제, LoadAsync 래퍼 반환타입 `Task<...SupabaseLoadResult>`로, IsNewUser 프로퍼티 래퍼 없음), 샘플 `ExampleSupabaseScenarios.cs`(`GiftNewUserIfNeeded(bool)` 헬퍼; Start·DeletePlayerData는 `SamplePlayerSave.LoadAsync()` 결과의 IsNewUser 전달, LoadPlayerData는 LoadAll 데모라 gift 없음), docs `user-data/load.md`(#new-user=result.IsNewUser, #preload-fallback 신설), `CLAUDE.md`. **DefenceR는 PlayerSave 재생성 필요**(게임 코드 OnFirstLoad 미사용이라 무영향). **Retool `ColumnManagementTab.tsx`**: JSON(컬렉션) 기본값 입력에 "NULL로 두면 클라 fallback, 값 넣으면 서버 우선" 안내 추가(전체 코드 채팅 제공, 게시 사용자).

**LoadAllUserSavesAsync 제거(2026-07-15):** 세이브 클래스가 SingletonGuard로 1개뿐이라 `PlayerSave.LoadAsync()`와 중복 → 파사드 `Supabase.LoadAllUserSavesAsync`·`SupabaseSDK.TryLoadAllUserSavesAsync`·`UserSaveStaticSyncRegistry.LoadAllAsync`/`SafeLoadAsync`·`Entry.LoadAsync` 필드·`Register`/`RegisterUserSaveStaticSync`의 `loadAsync` 파라미터 전부 삭제. `StaticUserSave.EnsureRegistered`도 loadAsync 인자 제거. 저장 쪽 `SaveAllAsync`/`RequestImmediateFlushAll`은 유지(플러시는 유효). docs game-data.md 행 삭제·auto-login.md·load.md·CLAUDE 정리. **DefenceR `SupabaseManager.cs:118`가 이 API 사용 → `PlayerSave.LoadAsync()`로 교체 필요**(패키지 갱신 시 컴파일 깨짐).

**DB 적용됨(2026-07-15):** fallback이 동작하려면 컬렉션 jsonb 컬럼이 SQL NULL이어야 함 → 양 프로젝트 `public.user_data`의 **모든 jsonb 컬럼에 `DROP DEFAULT`+`DROP NOT NULL`** 적용(jsonb 컬럼 순회 DO 블록, migration `user_data_jsonb_default_null`). ProjectR=1개(`hero_data`, 원래 nullable), DevilSlayer=143개(**전부 NOT NULL이었음** → nullable로 전환). 기존 행 값은 불변(`'[]'`/`'{}'` 유지 → "값 있음=서버 우선", fallback은 NULL에만). **DevilSlayer 라이브 유의점:** 신규 행 jsonb가 NULL로 들어올 수 있어 클라가 NULL jsonb를 견뎌야 함(새 SDK는 ApplyAutoDefaults로 NULL→빈 컬렉션). 구 클라 병행 시 확인 필요.

**미검증:** Unity 컴파일(러너 없음). 커밋·PlayerSave 재생성·Retool 게시·DefenceR 수정은 사용자. [[project_playnanoo_parallel_login]] · seed 훅 [[project_datacolumn_single_source]]는 별개(OnCurrentApplied/SeedDefault_* 유지).
