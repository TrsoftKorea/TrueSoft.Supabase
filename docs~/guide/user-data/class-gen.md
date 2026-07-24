# 클래스 생성

메뉴 **TrueSoft > Supabase > 클래스 생성 > 유저 데이터**를 열고, **필드 목록 가져오기 → 소스 생성 → 저장**으로 `PlayerSave.cs`를 생성합니다. Secret 키는 `SupabaseSettings.asset` 인스펙터에서 먼저 입력합니다. 타입·필드명·기본값 설정은 재생성 시 컬럼 기준으로 보존되고, 기존 클래스가 있으면 그 파일에 바로 저장됩니다.

## 컬럼 목록 {#column-list}

스키마를 가져오면 컬럼이 표로 나옵니다. 컬럼이 많으면 표 위 **검색** 칸으로 이름을 필터링할 수 있습니다. 필터는 표시만 거를 뿐 생성 대상엔 영향이 없습니다.

| 칸 | 설명 |
|----|------|
| **컬럼** | DB 컬럼명. `[DataColumn]` 매핑 키로 그대로 쓰입니다. |
| **필드명** | 생성될 C# 필드·프로퍼티 이름. 기본값은 컬럼명. 컬럼명과 다르면 직렬화 보존을 위해 `[JsonProperty("컬럼명")]`이 자동으로 붙습니다. |
| **저장 주기** | 자동 저장 배치 우선순위 — 보통(기본) · 짧게 · 길게. |
| **기본값** | 새 유저 시작값. DB 컬럼에 기본값이 있으면 가져올 때 자동으로 채워집니다. 스칼라 필드는 `= 값` 초기화로, Auto 컬렉션은 `[AutoDefault]`로 생성됩니다. 요소가 클래스라도 마찬가지입니다. **모든 컬럼은 기본값이 필수** — 비어 있으면 경고가 뜨고 소스 생성이 막힙니다. |
| **포함** | 해제하면 그 컬럼은 생성에서 제외됩니다. |
| **타입** | 생성될 C# 타입. 단순 값 컬렉션은 생성 시 `AutoList` / `AutoDict`로 변환됩니다. [데이터 타입](../data-types/auto-collections)을 참고하세요. |

타입·필드명·저장 주기·기본값 설정은 **재생성 시 컬럼 기준으로 보존**됩니다.

## CSV로 편집 {#csv-edit}

1. 스키마를 가져온 뒤 **CSV로 저장하기**로 현재 설정을 파일로 씁니다. 저장 위치는 버튼 아래 **CSV 위치**에 표시되며, **위치 변경**으로 바꿀 수 있습니다.
2. 표시된 위치의 파일을 엑셀·구글시트에서 편집합니다.

   | 열 | 내용 |
   |----|------|
   | `column` | DB 컬럼명 — **매칭 키이므로 수정 금지** |
   | `field` | C# 필드명 |
   | `type` | `int`·`List<bool>`·`Dictionary<string, int>` 등 |
   | `priority` | `Normal`·`fast`(빠르게 저장)·`slow`(느리게 저장). 또는 `1`·`0`·`2`. 구버전 `Urgent`/`Lazy`·한글 `보통`/`짧게`/`길게`도 인식 |
   | `default` | 기본값. **모든 컬럼 필수** — 빈 칸이면 소스 생성이 막힙니다 |
   | `include` | `1`(포함) / `0`(제외) |

::: warning 모든 필드는 기본값 필수
포함된 모든 컬럼은 `default`를 반드시 채워야 합니다. 비어 있으면 표의 기본값 칸에 `⚠ 필요`가 표시되고 **소스 생성이 차단**됩니다. 스칼라는 `0`·`false`·문자열 같은 시작값을, 값 컬렉션은 슬롯 기본값을, 클래스 요소 컬렉션은 생성자 인자를 씁니다. 빈 문자열을 기본값으로 두려면 `""`처럼 명시하세요. (`account_id`·`user_id`·`updated_at` 등 시스템 컬럼은 애초에 목록에서 제외됩니다.)
:::

3. **CSV 불러오기**로 반영합니다. `column`으로 매칭되고, 채워진 칸만 덮어씁니다. 단, `default`는 빈 칸도 반영되어 기본값을 지웁니다. 일치하는 컬럼이 없는 행은 건너뜁니다.

불러오기 시 `type` 열의 타입을 에디터가 찾을 수 있는지 확인합니다. 못 찾으면 빨간 ✕로 표시하지만 생성은 막지 않습니다 — 철자가 맞으면 그대로 생성되고, 오타라면 컴파일 시 에러로 드러납니다. 중첩 타입이나 다른 네임스페이스의 타입은 정규화 이름으로 씁니다.

::: tip 커스텀 타입 지정
`type` 열에는 스칼라·컬렉션 외에 `Dictionary<HeroName, HeroData>`처럼 enum 키나 커스텀 클래스 값도 쓸 수 있습니다. `default` 값을 함께 지정하면 이 컬럼은 `AutoDict`로 자동 승격되고, 이름이 유일하게 식별되는 한 **네임스페이스 없이 써도 생성기가 완전한 이름을 채워 넣습니다.** 이름이 여러 네임스페이스에 겹치거나 기본값을 지정하지 않는 경우에는 `MyGame.HeroName`처럼 정규화 이름을 직접 쓰세요. enum 키는 멤버 **이름**이 DB에 저장되므로, 이름 변경·삭제는 저장 데이터 마이그레이션이 필요합니다.
:::

::: tip
처음 한 번만 파일 위치를 정하면, 이후 **저장·불러오기는 다이얼로그 없이** 그 파일을 바로 쓰고 결과를 Console에 남깁니다. 위치를 바꾸려면 **위치 변경** 버튼을 쓰세요. `Dictionary<string, int>`처럼 콤마가 든 타입은 CSV에서 큰따옴표로 감싸집니다 — 스프레드시트가 자동 처리하니 그대로 두면 됩니다. 불러온 뒤에는 **소스 생성**을 다시 눌러 미리보기를 갱신하세요.
:::

## 생성 결과 {#generated-output}

예를 들어 컬럼 `user_id`의 필드명을 `playerId`로 바꾸면 다음과 같이 생성됩니다.

```csharp
[DataColumn("user_id")] [JsonProperty("user_id")] internal int playerId;
public static int PlayerId { get => Instance.Current.playerId; set { Instance.Current.playerId = value; Instance.MarkDirty(); } }
```

**기본값** 칸을 채우면 타입에 따라 다르게 생성됩니다. 스칼라는 필드 초기화식, [Auto 컬렉션](../data-types/auto-collections)은 범위 밖 요소 기본값을 정하는 `[AutoDefault]`로 변환됩니다. DB 컬럼에 `default 1` 같은 리터럴 기본값이 설정돼 있으면 스키마를 가져올 때 이 칸에 자동으로 채워지며, `now()`·`gen_random_uuid()` 같은 함수 기본값이나 jsonb 기본값은 제외됩니다.

```csharp
[DataColumn("level")]    internal int    level    = 1;        // 스칼라 → 필드 초기화
[DataColumn("nickname")] internal string nickname = "Guest";
[DataColumn("scores")] [AutoDefault(-1)] internal AutoList<int> scores = new AutoList<int>();  // Auto 컬렉션 → 요소 기본값
```

요소가 클래스라도 마찬가지입니다. CSV엔 네임스페이스 없이 `type=Dictionary<HeroName, HeroData>` · `default=1`만 넣으면, 생성기가 이름을 찾아 `AutoDict`로 승격하고 완전한 이름으로 채워 넣습니다.

```csharp
[DataColumn("hero_data")] [AutoDefault(1)] internal AutoDict<Quantum.HeroName, Game.Data.HeroData> heroData = new AutoDict<Quantum.HeroName, Game.Data.HeroData>();
```

요소 타입에는 기본값 인자와 맞는 생성자가 있어야 합니다. 이름이 여러 네임스페이스에 겹치거나 아직 찾을 수 없는 타입이면 승격 대신 `SeedDefault_필드명` partial 메서드가 선언됩니다. 별도 partial 파일에 구현하면 로드될 때마다 자동 호출됩니다.

`AutoDict`/`AutoDict2D`로 승격된 필드는 [처음 읽는 순간 그 자리에 저장](../data-types/auto-collections#class-value-materializes-on-read)되므로, `PlayerSave.HeroData[hero].Count = 1;`처럼 대입 없이 필드만 바로 고쳐도 됩니다.

값이 enum이면 타입 해석·네임스페이스 자동완성은 클래스와 동일하게 동작하지만, `default`엔 멤버 이름 대신 **정수값**을 씁니다.

```csharp
[DataColumn("hero_grade")] [AutoDefault(0)] internal AutoDict<HeroName, HeroGrade> heroGrade = new();   // 빈 칸 = (HeroGrade)0
```

::: warning enum 멤버 이름 대신 정수를 쓰세요
`default`가 enum이면 생성기가 서식 변환 없이 CSV 셀 값을 그대로 어트리뷰트에 넣습니다. 생성 파일엔 커스텀 네임스페이스 `using`이 없어서 `Rare`처럼 짧게 쓰면 컴파일 에러가 나고, `MyGame.HeroGrade.Rare`처럼 완전한 이름을 써야 컴파일됩니다. 정수값(`0`·`1`…)을 쓰면 이 문제가 없고 런타임에서 동일한 값으로 변환됩니다.
:::

```csharp
// PlayerSave.HeroDefaults.cs — 직접 작성하는 별도 파일
public sealed partial class PlayerSave
{
    static partial void SeedDefault_HeroData(Row row)
    {
        foreach (var name in Enum.GetValues<HeroName>())
            if (!row.hero_data.ContainsKey(name))
                row.hero_data[name] = new HeroData { level = 1 };
    }
}
```

::: tip
구현하지 않고 남겨둬도 됩니다 — 컴파일러가 미구현 partial 메서드 호출을 자동으로 제거합니다. `ContainsKey`로 방어하면 새 유저뿐 아니라 열거형에 값이 추가된 뒤 기존 유저가 재로그인할 때도 누락분만 채워집니다.
:::

::: warning 타입을 정해야 생성됩니다
jsonb 컬럼은 가져올 때 미지정 상태를 뜻하는 `Dictionary<string, object>`로 시작하며 ⚠ 로 표시되고, 그 상태로는 **소스 생성이 막힙니다**. CSV의 `type` 열에서 `Dictionary<string, int>`처럼 타입을 좁히면 풀립니다.
:::

생성된 파일은 다음과 같은 구조입니다.

```csharp
// PlayerSave.cs — 생성기로 자동 생성, 직접 수정하지 않습니다
using Newtonsoft.Json;
using TrueBase.Core.Data;
using TrueBase.Unity;

public sealed partial class PlayerSave : StaticUserSave<PlayerSave.Row>
{
    public static readonly PlayerSave Instance = new();
    private PlayerSave() : base() { }

    // 로드·저장·삭제는 Supabase 파사드에 있습니다 — 이 파일에는 컬럼 접근만 들어갑니다.

    // 필드는 internal — 데이터는 아래 정적 프로퍼티로 접근합니다.
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]   // Newtonsoft가 internal 필드를 저장/로드
    public sealed class Row
    {
        [DataColumn("level")]     internal int            level;
        [DataColumn("coins")]     internal int            coins;
        [DataColumn("inventory")] internal List<int>      inventory = new List<int>();   // 컬렉션은 빈 인스턴스로 초기화
        [DataColumn("updated_at")] internal string        updated_at;                    // 동기화 기준 컬럼 — 항상 포함
    }

    // 스칼라: get/set — 쓰면 MarkDirty 자동
    public static int Level
    {
        get => Instance.Current.level;
        set { Instance.Current.level = value; Instance.MarkDirty(); }
    }

    public static int Coins
    {
        get => Instance.Current.coins;
        set { Instance.Current.coins = value; Instance.MarkDirty(); }
    }

    // 컬렉션: 일반 컬렉션처럼 사용 — 제자리 수정도 자동 동기화에 반영
    public static List<int> Inventory
    {
        get => Instance.Current.inventory;
        set { Instance.Current.inventory = value ?? new List<int>(); Instance.MarkDirty(); }
    }
}
```

새 컬럼이 생기면 생성기를 다시 실행해 덮어씁니다.

::: info 다시 생성해야 하는 시점
생성 파일에는 컬럼 접근만 들어갑니다. 로드·저장·삭제는 [`Supabase` 파사드](/guide/api/game-data)에 있으므로, SDK를 올려도 이 파일은 그대로 두면 됩니다. **DB 컬럼이 바뀔 때만** 다시 생성하세요.
:::
