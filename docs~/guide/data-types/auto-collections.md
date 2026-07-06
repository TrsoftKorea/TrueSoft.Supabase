# 자동 확장 컬렉션

## 기본 사용법

게임 버전업으로 크기가 늘어나는 컬렉션(예: 3 → 5 스테이지)은 매 접속마다 칸을 미리 늘려두는 선작업이 번거롭습니다. `AutoList<T>` / `AutoDict<TKey,TValue>`를 쓰면 그 작업이 사라집니다.

- **범위 밖 읽기** → 지정한 기본값 반환 (확장하지 않음 → 단순 조회는 저장을 유발하지 않음)
- **범위 밖 쓰기** → 그 위치까지 기본값으로 채운 뒤 저장

`List<T>` / `Dictionary<TKey,TValue>`와 사용법·직렬화(일반 배열/객체)가 동일합니다.

::: tip 생성기 자동 적용
유저 데이터 클래스 생성기는 **단순 값 요소**(`int`·`float`·`bool`·`string` 등)의 리스트·배열·딕셔너리 컬럼을 자동으로 `AutoList`/`AutoDict`(중첩은 `AutoList2D`/`AutoDict2D`)로 생성합니다. 튜플·struct·클래스 요소 컬렉션은 `List`/`Dictionary`로 둡니다. 커스텀 기본값이 필요하면 생성된 필드에 `[AutoDefault(...)]`만 추가하면 되고, **재생성 시 컬럼 기준으로 자동 보존**됩니다.
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
- **복합 값 타입(struct·튜플)**은 상수 여러 개를 넘기면 **생성자로 조립**됩니다. struct 인스턴스 자체는 어트리뷰트에 못 넣지만, 그 재료(상수)를 넘기면 SDK가 만들어 줍니다:

```csharp
public struct Stage { public int score, stars; public Stage(int s, int st) { score = s; stars = st; } }

[DataColumn("stages")] [AutoDefault(-1, 0)] internal AutoList<Stage> stages = new();   // 빈 칸 = new Stage(-1, 0)
// 튜플도 동일: [AutoDefault(-1, 0)] internal AutoList<(int,int)> ...  → (-1, 0)
```
요소 타입에 인자와 맞는 생성자가 있어야 합니다(struct·튜플은 값 타입이라 자동 확장에도 안전).

기본값은 JSON 데이터에 저장되지 않고 SDK가 **로드 직후 인스턴스에 주입**하므로, 재로드·동기화에도 안전합니다.

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
- `grid[i]`는 `Count`·`FindAll`·`Sort`·인덱서·LINQ 열거를 지원합니다. 실제 `AutoList` 행이 필요하면 `Row(i)`, 통째 교체는 `SetRow(i, …)`.
- 프록시를 변수·파라미터·반환에 담을 땐 `AutoRow<T>` 타입으로 명명합니다: `AutoRow<int> row = grid[i];`.
- JSON에는 `[[...],[...]]` 중첩 배열로 직렬화됩니다(기존 컬럼과 호환).

::: info 왜 프록시인가
`grid[i]`는 값을 즉시 만들지 않고 `(grid, i)`만 담은 가벼운 접근자(`AutoRow<T>`)를 돌려줍니다. 읽기/쓰기 판단을 `[j]`(get/set)로 미뤄서 **조회는 비파괴, 쓰기만 저장**이 됩니다. 대신 `AutoRow<T>`는 진짜 `AutoList`가 아니므로(그 위에 없는 `Add`·`Remove` 등 List 메서드가 필요하면 `Row(i)`), `List<T>`를 받는 API엔 `Row(i)`를 넘기세요.
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
- `dict[k1]`은 `Count`·`ContainsKey`·`TryGetValue`·열거를 지원합니다. 실제 `AutoDict`가 필요하면 `Inner(k1)`, 통째 설정은 `SetInner(k1, …)`. 프록시를 담을 땐 `AutoDictRow<TKey1, TKey2, TValue>` 타입으로 명명합니다.
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
