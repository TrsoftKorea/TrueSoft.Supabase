---
name: project-datacolumn-single-source
description: DataColumnAttribute/DataSavePriority는 Core에만 정의. Unity 미러는 제거 결정(2026-06-17). 재추가 금지.
metadata: 
  node_type: memory
  type: project
  originSessionId: 78f41135-f0e2-49db-a33c-a17d8d8417d5
---

`DataColumnAttribute`와 `DataSavePriority`는 **`TrueBase.Core.Data`에만** 정의한다. `Runtime/Unity`의 미러 버전은 2026-06-17 제거했다. **다시 추가하지 말 것.**

**배경/이유:**
- 미러가 있으면 생성된 `PlayerSave.cs`가 `using TrueBase.Core.Data;` + `using TrueBase.Unity;`를 둘 다 import할 때 `DataColumn`이 CS0104 모호성 발생(Core가 autoReferenced=true라 둘 다 보임). 이 모호성이 전체 컴파일을 깨뜨려 ScriptableObject "Type cannot be found: TrueBase.Unity.SupabaseSettings" 연쇄 에러까지 유발했다.
- 미러를 제거하면 타입이 하나뿐이라 기존 생성 파일(`using TrueBase.Core.Data;` 포함)이 **재생성 없이 그대로 컴파일**된다 — 여러 개발자 환경 일괄 해결.
- git 이력상 미러는 이미 "불필요 삭제"→"복구"로 오락가락했음. 이번이 최종 결정.

**현재 일관된 구성:**
- `Runtime/Core/TrueBase.Core.asmdef`: `autoReferenced: true` (게임 Assembly-CSharp이 Core 자동 참조)
- 생성기(`Editor/PostgrestOpenApiUserSaveClass.cs`): `using TrueBase.Core.Data;` + `using TrueBase.Unity;` 출력
- `DataSchema`의 이름 기반 attribute 매칭(폴백)은 방어 로직으로 유지 — 무해.

**주의:** Core를 참조하지 않는 커스텀 asmdef 게임은 Core 참조를 직접 추가해야 함(DefenceR는 Assembly-CSharp이라 해당 없음). [[project_defencer_consumes_sdk_via_github]] — SDK 변경은 커밋+푸시 후 각 프로젝트가 패키지 갱신해야 반영됨.
