# 데이터 타입

유저 세이브 필드에 쓸 수 있는 타입과 직렬화 규칙입니다. 직렬화는 Newtonsoft.Json 기반이라 폭넓은 타입을 지원합니다.

---

## 지원 타입

| C# 타입 | DB 컬럼 타입 | 비고 |
|---|---|---|
| `bool` `int` `long` `float` `double` | `bool` `int4` `int8` `float4` `float8` | |
| `decimal` | `numeric` | 정밀 금액 |
| `string` | `text` | |
| `DateTime` / `DateTimeOffset` | `timestamptz` | ISO8601 자동 파싱 |
| `int?` `long?` 등 nullable | 같은 타입(NULL 허용) | null/0 구분 시 |
| `List<T>` / `List<List<T>>` | `jsonb` | 이중 리스트 가능 |
| `T[]` | `jsonb` | |
| `Dictionary<K,V>` | `jsonb` | |
| 중첩 클래스 | `jsonb` | 요소 클래스는 파라미터 없는 생성자 필요 |

생성 창에서 컬럼 타입을 지정할 수 있고(컬렉션은 요소 타입을 자유 텍스트로 입력), 지정한 타입은 재생성 시에도 보존됩니다.

---

## 직렬화 규칙

::: warning
`Row`는 `[JsonObject(MemberSerialization.Fields)]`로 직렬화되어 **필드 이름**을 JSON 키로 사용합니다.
`[DataColumn("other_name")]`은 select/PATCH 키만 바꿀 뿐 역직렬화 키는 바꾸지 않습니다.
DB 컬럼명과 C# 필드명이 다를 때는 `[JsonProperty]`를 함께 지정하세요.
:::

```csharp
[DataColumn("last_login_at")]
[JsonProperty("last_login_at")]
internal string lastLoginAt;
```

::: info 커스텀 클래스 요소
`List<MyItem>` · `Dictionary<string, MyItem>`처럼 요소가 클래스이고 그 **private 필드까지** 저장하려면, `MyItem`에도 `[JsonObject(MemberSerialization.Fields)]`를 붙이세요. 없으면 public 멤버만 저장됩니다.
:::
