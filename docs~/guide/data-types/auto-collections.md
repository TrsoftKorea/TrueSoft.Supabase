# 자동 확장 컬렉션 (AutoList · AutoDict)

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

## 기본값 지정 — `[AutoDefault]`

기본값이 `0`/`false`/`null`이 아니어야 하면 **필드에 `[AutoDefault(...)]` 한 줄**을 붙입니다(필드마다 클래스를 만들 필요 없음).

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

## 이중 리스트 — `AutoList2D<T>`

행·열이 모두 가변인 이중 리스트는 `AutoList2D<T>` 하나로 처리합니다. `[i, j]` 단일 인덱서로 접근하며, **읽기는 비파괴**(범위 밖이면 기본값), **쓰기만 양쪽 차원을 확장 후 저장**합니다.

```csharp
[DataColumn("scores")] [AutoDefault(-1)] internal AutoList2D<int> scores = new();   // 예: [스테이지][웨이브]
```
```csharp
PlayerSave.Scores[5, 3] = 100;     // 행 5·열 3까지 자동 확장 후 저장
int v = PlayerSave.Scores[5, 3];   // 범위 밖이면 -1 반환, 확장하지 않음
```

- JSON에는 `[[...],[...]]` 중첩 배열로 직렬화됩니다(`List<List<T>>`와 동일 형태 → 기존 컬럼과 호환).
- `[i, j]` 한 번의 호출이라 읽기가 데이터를 만들지 않습니다 — `AutoList`를 두 번 중첩할 때 생기는 "조회가 저장이 되는" 문제가 없습니다.
- 값 타입 `T`에 적합합니다.

## 이중 딕셔너리 — `AutoDict2D<TKey1, TKey2, TValue>`

2단계 딕셔너리는 `AutoDict2D`로 처리합니다. `[k1, k2]` 인덱서로 접근하며, 없는 키 조합 읽기는 기본값 반환(비파괴), 쓰기는 안쪽 딕셔너리를 자동 생성 후 저장합니다.

```csharp
[DataColumn("counts")] [AutoDefault(0)] internal AutoDict2D<string, int, int> counts = new();
```
```csharp
PlayerSave.Counts["fire", 3] = 10;     // 안쪽 딕셔너리 자동 생성 후 저장
int c = PlayerSave.Counts["ice", 9];   // 없으면 0 반환
```

JSON에는 `{"k1":{"k2":v}}` 중첩 객체로 직렬화됩니다(`Dictionary` 중첩과 동일 형태).

---
