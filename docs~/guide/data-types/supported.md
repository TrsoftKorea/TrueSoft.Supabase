# 지원 타입

유저 세이브 필드에 쓸 수 있는 타입입니다. 직렬화는 Newtonsoft.Json 기반이라 폭넓은 타입을 지원합니다.

| C# 타입 | DB 컬럼 타입 | 비고 |
|---|---|---|
| `bool` `int` `long` `short` `float` `double` | `bool` `int4` `int8` `int2` `float4` `float8` | |
| `decimal` | `numeric` | 정밀 금액 |
| `string` | `text` | |
| `DateTime` / `DateTimeOffset` | `timestamptz` | ISO8601 자동 파싱 |
| `DateOnly` / `TimeOnly` | `date` / `time` | 날짜·시각만 |
| `List<T>` | `jsonb` | |
| `T[]` | `jsonb` | |
| `Dictionary<K,V>` | `jsonb` | |
| 중첩 클래스 | `jsonb` | 요소 클래스는 파라미터 없는 생성자 필요 |

컬렉션은 자유롭게 중첩·조합할 수 있습니다 — `List<List<int>>`, `int[][]`, `Dictionary<string, List<int>>`, `List<MyItem>` 등 모두 `jsonb` 하나에 저장됩니다.

생성기에서 지정한 컬럼 타입은 재생성 시에도 보존됩니다.
