# 자동 확장 컬렉션

::: tip 실행 가능한 예제
`Examples` 샘플의 `SampleAutoCollections` 컴포넌트를 빈 GameObject에 붙이고 Play 후 **1·2·3·4** 키를 누르면 컬렉션 동작이 Console에 출력됩니다. 로그인·네트워크가 필요 없습니다.
:::

## 기본 사용법

게임 버전업으로 크기가 늘어나는 컬렉션(예: 3 → 5 스테이지)은 매 접속마다 칸을 미리 늘려두는 선작업이 번거롭습니다. `AutoList<T>` / `AutoDict<TKey,TValue>`를 쓰면 그 작업이 사라집니다.

- **범위 밖 읽기** → 지정한 기본값 반환 (확장하지 않음 → 단순 조회는 저장을 유발하지 않음)
- **범위 밖 쓰기** → 그 위치까지 기본값으로 채운 뒤 저장

`List<T>` / `Dictionary<TKey,TValue>`와 사용법·직렬화(일반 배열/객체)가 동일합니다.

::: tip 생성기 자동 적용
유저 데이터 클래스 생성기는 **단순 값 요소**(`int`·`float`·`bool`·`string` 등)의 리스트·배열·딕셔너리 컬럼을 자동으로 `AutoList`/`AutoDict`(중첩은 `AutoList2D`/`AutoDict2D`)로 생성합니다. 요소가 struct·클래스라도 CSV `default` 값을 지정하면 같은 방식으로 자동 승격되며, 타입 이름에 네임스페이스를 안 써도 생성기가 완전한 이름을 채워 넣습니다. 기본값을 지정하지 않았거나 타입을 찾지 못하면 `List`/`Dictionary`로 남습니다. 생성된 필드에 `[AutoDefault(...)]`를 직접 추가해도 되며, **재생성 시 컬럼 기준으로 자동 보존**됩니다.
:::

```csharp
[DataColumn("stage_clears")] internal AutoList<int> stageClears = new();   // 기본값 0
```
```csharp
PlayerSave.StageClears[4] = 1;        // 크기가 3이어도 자동 확장 후 저장
int s = PlayerSave.StageClears[4];    // 범위 밖이면 기본값 반환, 확장하지 않음
```

## 기본값 지정 · `[AutoDefault]`

기본값이 `0`/`false`/`null`이 아니어야 하면 **필드에 `[AutoDefault(...)]` 한 줄**을 붙입니다.

```csharp
[DataColumn("stage_scores")] [AutoDefault(-1)]    internal AutoList<int>        stageScores = new();  // 미클리어 = -1
[DataColumn("unlocked")]     [AutoDefault(false)] internal AutoList<bool>       unlocked    = new();
[DataColumn("counters")]     [AutoDefault(0)]     internal AutoDict<string,int> counters    = new();
```

- **단순 값 1개**는 그 값으로 변환됩니다(`int`·`long`·`float`·`bool`·`string`·`enum` 등).
- **복합 값 타입(struct·튜플·클래스)**은 상수 여러 개를 넘기면 **생성자로 조립**됩니다. 인스턴스 자체는 어트리뷰트에 못 넣지만, 그 재료(상수)를 넘기면 SDK가 만들어 줍니다:

```csharp
public struct Stage { public int score, stars; public Stage(int s, int st) { score = s; stars = st; } }

[DataColumn("stages")] [AutoDefault(-1, 0)] internal AutoList<Stage> stages = new();   // 빈 칸 = new Stage(-1, 0)
// 튜플도 동일: [AutoDefault(-1, 0)] internal AutoList<(int,int)> ...  → (-1, 0)
```

```csharp
public class HeroData { public int level; public HeroData(int level) { this.level = level; } }

[DataColumn("hero_data")] [AutoDefault(1)] internal AutoDict<HeroName, HeroData> heroData = new();   // 빈 칸 = new HeroData(1)
```

요소 타입에 인자와 맞는 생성자가 있어야 합니다. 슬롯마다 매번 새 인스턴스를 만들기 때문에 struct·튜플뿐 아니라 클래스 값도 자동 확장에 안전합니다 — 한 슬롯을 수정해도 다른 슬롯이 같이 바뀌지 않습니다.

기본값은 JSON 데이터에 저장되지 않고 SDK가 **로드 직후 인스턴스에 주입**하므로, 재로드·동기화에도 안전합니다.

### 클래스 값은 처음 읽을 때 저장됩니다 {#class-value-materializes-on-read}

`AutoDict`/`AutoDict2D`에 참조 타입을 `[AutoDefault(...)]`로 지정하면, 없는 키를 읽는 순간 그 자리에 저장됩니다. 그래서 별도 대입 없이 필드만 바로 고쳐도 반영됩니다.

```csharp
PlayerSave.HeroData[hero].Count = 1;   // hero가 처음 등장해도 그 자리에 저장된 뒤 반영됨
```

같은 키를 다시 읽으면 방금 저장된 그 객체를 그대로 돌려주고, 서로 다른 키는 여전히 독립 인스턴스라 한쪽을 고쳐도 다른 쪽엔 영향이 없습니다. 값이 기본값 그대로인 항목은 저장 대상 비교에서 없는 것과 동일하게 취급되므로, 단순 조회만으로 불필요한 저장이 나가지는 않습니다.

::: warning 키 존재를 "보유 여부"로 쓰지 마세요
`ContainsKey`나 `foreach` 순회는 값을 한 번도 안 바꾼 항목도 존재로 보여줄 수 있습니다. 어떤 항목을 실제로 보유·해금했는지는 값 클래스 안에 `bool owned` 같은 별도 필드로 명시적으로 관리하세요.
:::

이 동작은 `AutoDict`/`AutoDict2D`의 참조 타입 `[AutoDefault]` 값에만 적용됩니다. `AutoList`/`AutoList2D`는 인덱스가 슬롯 순서를 나타내므로 항상 비파괴로 남아 있고, 스칼라·struct 값이나 `DefaultValue = ...`로 직접 대입한 경우도 기존처럼 비파괴입니다.

::: warning 필드·프로퍼티 타입을 모두 바꾸세요
자동 확장 인덱서는 정적 타입이 `AutoList`/`AutoDict`일 때만 동작합니다. 생성 클래스의 **Row 필드와 정적 프로퍼티를 모두** `AutoList<T>`(또는 `AutoDict<TKey,TValue>`)로 선언하세요. `List<T>`로 캐스팅하면 기본 인덱서가 쓰입니다.
:::

## 이중 리스트 · `AutoList2D<T>`

행·열이 모두 가변인 이중 리스트입니다. `grid[i]`가 **지연 프록시**를 돌려줘서 `[i][j]`·`[i, j]` 모두 **모든 경우 안전**합니다 — 읽기는 비파괴(없는 행/열이면 기본값, 아무것도 안 만듦), 쓰기는 그 시점에 행·열을 생성·저장합니다.

```csharp
[DataColumn("scores")] [AutoDefault(-1)] internal AutoList2D<int> scores = new();   // 예: [스테이지][웨이브]
```
```csharp
PlayerSave.Scores[5][3] = 100;    // 쓰기: 행 5·열 3을 그때 생성·저장
int a = PlayerSave.Scores[5][3];  // 읽기: 없으면 -1, 아무것도 안 만듦(비파괴)
int b = PlayerSave.Scores[5, 3];  // [i, j]도 동일하게 동작

var pos = PlayerSave.Scores[5].FindAll(n => n > 0);      // 행 연산
PlayerSave.Scores[5].Sort();                             // 정렬
int sum = PlayerSave.Scores[5].Where(n => n > 0).Sum();  // LINQ
```

- **읽기는 절대 데이터를 만들지 않습니다** — `grid[i]`·`grid[i][j]`를 조회만 하면 행이 생기지 않습니다(`Count`도 그대로).
- **쓰기만 생성** — `grid[i][j] = v`를 하는 그 순간에만 행·열이 만들어집니다.
- `grid[i]`(`AutoRow<T>`)는 **`IList<T>`를 구현**합니다 — `Add`·`AddRange`·`Insert`·`Remove`·`RemoveAt`·`RemoveAll`·`Clear`·`Contains`·`IndexOf`·`Find`·`FindAll`·`FindIndex`·`Sort`·`Reverse`·`ToArray`·`ForEach`·LINQ 등 List 연산을 그대로 씁니다. 실제 `AutoList` 행은 `Row(i)`, 통째 교체는 `SetRow(i, …)`.
- 프록시를 변수·파라미터·반환에 담을 땐 `AutoRow<T>` 타입으로 명명합니다: `AutoRow<int> row = grid[i];`.
- JSON에는 `[[...],[...]]` 중첩 배열로 직렬화됩니다(기존 컬럼과 호환).

::: info 왜 프록시인가
`grid[i]`는 값을 즉시 만들지 않고 `(grid, i)`만 담은 가벼운 접근자(`AutoRow<T>`)를 돌려줍니다. 읽기/쓰기 판단을 `[j]`(get/set)로 미뤄서 **조회는 비파괴, 쓰기만 저장**이 됩니다. `AutoRow<T>`는 `IList<T>`를 구현하므로 `IList<T>`·`IEnumerable<T>`를 받는 API엔 그대로 넘길 수 있습니다. 다만 구체 타입 `List<T>`는 아니므로, `List<int> row = grid[i]`(직접 대입)나 `List<T>`를 요구하는 API엔 `Row(i)`(진짜 `AutoList<int>`)를 쓰세요.
:::

::: tip 성능 · hot path
`grid[i]`/`dict[k1]`는 접근마다 작은 프록시를 **할당**합니다. 세이브 데이터 접근엔 무해하지만, **매 프레임 큰 격자를 훑는** 코드라면 무할당 경로를 쓰세요:
- **단일 셀** → `grid[i, j]` / `dict[k1, k2]` (프록시를 안 거침, 무할당)
- **행/안쪽 반복** → `var row = grid[i]`로 한 번만 받아 재사용, 또는 `Row(i)`·`Inner(k1)`(기존 행/키는 저장된 객체를 그대로 반환 → 무할당)
:::

## 이중 딕셔너리 · `AutoDict2D<TKey1, TKey2, TValue>`

2단계 딕셔너리입니다. `dict[k1]`이 **지연 프록시**를 돌려줘서 `[k1][k2]`·`[k1, k2]` 모두 **모든 경우 안전**합니다(`AutoList2D`와 동일). 읽기는 비파괴(없는 키면 기본값), 쓰기는 그 시점에 안쪽 딕셔너리를 생성·저장합니다.

```csharp
[DataColumn("counts")] [AutoDefault(0)] internal AutoDict2D<string, int, int> counts = new();
```
```csharp
PlayerSave.Counts["fire"][3] = 10;    // 쓰기: 안쪽 딕셔너리를 그때 생성·저장
int c = PlayerSave.Counts["ice"][9];  // 읽기: 없으면 0, 아무것도 안 만듦(비파괴)
int d = PlayerSave.Counts["fire", 3]; // [k1, k2]도 동일

bool has = PlayerSave.Counts["fire"].ContainsKey(3);   // 프록시 연산
```

- 읽기는 비파괴(키 생성 없음), 쓰기만 생성 — `AutoList2D`와 동일한 지연 프록시 방식.
- `dict[k1]`(`AutoDictRow<…>`)은 **`IReadOnlyDictionary<TKey2, TValue>`를 구현**하고 `Add`·`Remove`·`Clear`·`ContainsKey`·`ContainsValue`·`TryGetValue`·`Keys`·`Values`·열거를 지원합니다. 실제 `AutoDict`는 `Inner(k1)`, 통째 설정은 `SetInner(k1, …)`. 담을 땐 `AutoDictRow<TKey1, TKey2, TValue>` 타입으로 명명합니다.
- JSON에는 `{"k1":{"k2":v}}` 중첩 객체로 직렬화됩니다(기존 컬럼과 호환).

## 일반 리스트처럼 쓸 때 주의

`AutoList`·`AutoList2D`는 리스트를 상속해 대부분의 List 연산이 그대로 되지만, **인덱스가 슬롯 의미**(예: `stageClears[stageId]`)라 일반 리스트처럼 다루면 매핑이 깨질 수 있습니다.

- **`Count`·`foreach`·LINQ는 실제 저장된 값만** 봅니다. 자동 확장 기본값은 미리 채워지지 않으므로, 안 쓴 슬롯은 열거·`Count`에 잡히지 않습니다.
- **인덱스를 미는 연산은 매핑을 깨뜨립니다.** `Insert`·`RemoveAt`·`Sort`·`Reverse` 등은 호출 시 `[Obsolete]` 경고가 표시됩니다. 슬롯을 비우려면 제거가 아니라 `list[i] = 기본값`으로 덮어쓰세요.

::: warning `List<T>`로 캐스팅하지 마세요
`AutoList<T>`를 `List<T>`·`IList<T>`로 캐스팅하거나 그 타입 파라미터로 넘기면 안전 인덱서가 사라져 **범위 밖 접근이 예외**가 됩니다. 필드·프로퍼티·지역변수·파라미터를 모두 `AutoList<T>`로 유지하세요. SDK에 포함된 Roslyn 분석기가 이 변환을 **컴파일 경고(`TB0001`)로 잡아줍니다.**
:::

### 안전 헬퍼

| 메서드 | 대상 | 설명 |
|--------|------|------|
| `GetOrDefault(i)` | `AutoList` | 범위 밖이면 기본값 반환(확장 안 함) |
| `EnsureCount(n)` | `AutoList` | `n`칸까지 기본값으로 확장 — `Count`를 논리 크기에 맞출 때 |
| `Row(i)` | `AutoList2D` | 행 반환, 없으면 빈 리스트(비파괴) — 행 단위 조회·LINQ |
| `Cells()` | `AutoList2D` | 모든 셀을 행 순서로 평탄화 열거 |

```csharp
if (PlayerSave.Scores.Row(0).Count > 0) { /* ... */ }   // 예외 없이 행 조회
int total = PlayerSave.Scores.Cells().Sum();            // 모든 셀 합
PlayerSave.StageClears.EnsureCount(10);                 // 10칸 확보
```
