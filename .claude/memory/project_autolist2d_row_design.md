---
name: project_autolist2d_row_design
description: "2D 자동확장 컬렉션은 grid[i]/dict[k1]가 지연 프록시(AutoRow/AutoDictRow) 반환 → [i][j]/[k1][k2]가 모든 경우 안전. struct프록시·구버전으로 되돌리지 말 것."
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
  modified: 2026-07-27T09:05:02.061Z
---

`AutoList2D<T>`·`AutoDict2D<TKey1,TKey2,TValue>`는 **지연 프록시(lazy proxy)** 방식이다(2026-07-06). `grid[i]`는 `new AutoRow this[int]`, `dict[k1]`은 `new AutoDictRow this[TKey1]`로 **프록시를 반환**하고, 실제 읽기/쓰기 판단을 `[j]`/`[k2]`의 get/set으로 미룬다:
- **읽기는 완전 비파괴** — `grid[i][j]`/`dict[k1][k2]` 조회는 없는 행/키여도 예외 없이 기본값 반환, **아무것도 안 만듦**(Count 그대로).
- **쓰기만 생성** — `grid[i][j] = v` 하는 그 순간에만 행·열(또는 안쪽 딕셔너리)을 생성·저장.

즉 "조회가 저장 유발"도, 쓰기 유실도, 없는 행/키 예외도 **모두 없다**.

**내부 구조:** `AutoList2D : List<AutoList<T>>`, `AutoDict2D : Dictionary<TKey1, AutoDict<TKey2,TValue>>`. 프록시는 **최상위 public 타입** `AutoRow<T>`·`AutoDictRow<TKey1,TKey2,TValue>`(중첩 아님 — 변수·파라미터·반환에 이름으로 담기 쉽게, 2026-07-06). owner의 헬퍼(`EnsureRow`/`EnsureInner`·`RawRowOrNull`/`InnerOrNull`)는 **internal**(같은 어셈블리라 프록시가 접근), 프록시 ctor도 internal(consumer는 `grid[i]`로만 획득). `grid[i]`/`dict[k1]` get은 `new`로 base 인덱서를 가림.

**프록시 API(2026-07-06 전체 확장):** `AutoRow<T> : IList<T>, IReadOnlyList<T>` — 인덱서·`Count`·`Add`/`AddRange`/`Insert`/`InsertRange`(materialize)·`Remove`/`RemoveAt`/`RemoveAll`/`RemoveRange`/`Clear`(없으면 no-op)·`Contains`/`IndexOf`/`LastIndexOf`/`Exists`/`TrueForAll`/`Find`/`FindLast`/`FindAll`/`FindIndex`/`FindLastIndex`/`GetRange`/`ForEach`/`ToArray`/`CopyTo`·`Sort`(2종)/`Reverse`·LINQ. `AutoDictRow<TKey1,TKey2,TValue> : IReadOnlyDictionary<TKey2,TValue>` — 인덱서·`Count`·`Add`/`Remove`/`Clear`·`ContainsKey`/`ContainsValue`/`TryGetValue`/`Keys`/`Values`·열거. **obsolete 회피:** AutoList의 hidden 메서드(Insert/RemoveAt/Reverse/Sort 등)는 `List<T>`로 업캐스트한 `private List<T> L => RawRowOrNull(i)`를 통해 base 메서드 호출. **기본값 규칙:** `FindAll`·열거·`Count`·`Contains`·`IndexOf`는 **저장분만**(가상 기본값 칸 제외). 단 **`Find`/`FindLast`(단일)는 못 찾으면 `default(T)`가 아니라 `DefaultValue`를 반환** — 인덱서 읽기(OOB→DefaultValue)와 일관되게 맞춤(2026-07-06, `FindIndex`로 매칭 판정 후 `l[idx]` 아니면 `_owner.DefaultValue`). **GC:** `grid[i]`/`dict[k1]`는 접근마다 프록시 할당 → hot path는 단일셀 `[i,j]`/`[k1,k2]`(무할당)·행반복 `Row(i)`/`Inner(k1)`(기존 행/키는 저장 객체 그대로, 무할당) 사용. 진짜 컬렉션은 `Row(i)`/`Inner(k1)`, 통째 교체는 `SetRow`/`SetInner`. `IList<T>`/`IReadOnlyDictionary` 구현이라 그 인터페이스 받는 API엔 전달 가능(구체 `List<T>`만 불가 → `Row(i)`).

**되돌리지 말 것:**
- **struct 프록시**는 `grid[i][j] = v`가 **CS1612**(임시 struct 수정 불가)로 컴파일 안 됨 → 반드시 **class** 프록시.
- `List<List<T>>` 구버전, "행이 진짜 AutoList고 없으면 예외"였던 중간 버전으로 되돌리지 말 것.

**비용(트레이드오프, 감수하기로 함):** ① `grid[i]`/`dict[k1]` 호출마다 프록시 객체 할당(GC) — 세이브 데이터 접근엔 대개 무해, hot-path만 `[i,j]` 사용. ② `grid[i]`가 진짜 `AutoList`/`List`가 아님 → 위임한 메서드만 지원(나머진 `Row(i)`/`Inner(k1)`).

**기타:** 기본값은 `SetDefaultValue`/`DefaultValue` setter의 `PropagateDefault()`로 각 행/안쪽에 전파(`DataSchema.ApplyAutoDefaults`가 로드 후 호출 → 역직렬화본에도 적용). 직렬화 `[[...]]`·`{"k1":{"k2":v}}` 유지 → DB/기존 세이브 호환. clone/diff는 JSON 기반(`DataSchema.CloneRow`/`HasChanges`)이라 투명. AutoDict는 참조형이라 CS1612 무관.

**분석기:** `TB0001`(Auto*→List 캐스팅)만 있음. `grid[i]`는 프록시(Auto 아님)라 TB0001에 안 걸림. `TB0002`는 완전 제거됨(단일 인덱서가 이제 안전). `AutoList`(1D) 시프트 메서드(`Insert`/`RemoveAt`/`Sort`/`Reverse`)는 `[Obsolete]` 유지, AutoList2D는 행 시프트(`Insert`/`RemoveAt`/`Reverse`) `[Obsolete]`.

[[feedback_verify_before_asserting]] 원칙대로 스크래치 하네스로 검증: AutoList2D+AutoDict2D 실소스 **39/39**(읽기 비파괴·쓰기 생성·Sort·LINQ·직렬화·기본값 전파), 분석기 TB0001=3/TB0002=0.
