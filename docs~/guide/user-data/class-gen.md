# 클래스 생성

`SupabaseSettings` 에셋 Inspector 하단에서 Secret 키를 입력하고 **스키마 가져오기 → 소스 생성 → 저장**으로 `PlayerSave.cs`를 생성합니다.

스키마를 가져오면 컬럼이 표로 나옵니다. 행마다 아래를 조정한 뒤 소스를 생성합니다.

| 칸 | 설명 |
|----|------|
| **컬럼** | DB 컬럼명(읽기 전용). `[DataColumn]` 매핑 키로 그대로 쓰입니다. |
| **필드명** | 생성될 C# 필드·프로퍼티 이름. 기본값은 컬럼명이고 자유롭게 바꿀 수 있습니다. 컬럼명과 다르면 직렬화 보존을 위해 `[JsonProperty("컬럼명")]`이 자동으로 붙습니다. |
| **타입** | 스칼라는 그대로, jsonb 컬럼은 `Dictionary` / `List` 중에서 고릅니다. 요소 타입은 값 타입 또는 2차원에서 선택하며, 단순 값 컬렉션은 생성 시 `AutoList` / `AutoDict`로 변환됩니다. [데이터 타입](../data-types/auto-collections)을 참고하세요. |
| **저장 주기** | 자동 저장 배치 우선순위 — 보통(기본) · 짧게 · 길게. |
| **기본값** | 새 유저 시작값. DB 컬럼에 기본값이 있으면 가져올 때 자동으로 채워집니다. 스칼라 필드는 `= 값` 초기화로, Auto 컬렉션은 `[AutoDefault]`로 생성됩니다. 적용할 수 없는 타입에서는 비활성화됩니다. |
| **포함** | 해제하면 그 컬럼은 생성에서 제외됩니다. |

타입·필드명·저장 주기·기본값 설정은 **재생성 시 컬럼 기준으로 보존**됩니다.

예를 들어 컬럼 `user_id`의 필드명을 `playerId`로 바꾸면 다음과 같이 생성됩니다.

```csharp
[DataColumn("user_id")] [JsonProperty("user_id")] internal int playerId;
public static int PlayerId { get => Instance.Current.playerId; set { Instance.Current.playerId = value; Instance.MarkDirty(); } }
```

**기본값** 칸을 채우면 타입에 따라 다르게 생성됩니다. 스칼라는 필드 초기화식, [Auto 컬렉션](../data-types/auto-collections)은 범위 밖 요소 기본값을 정하는 `[AutoDefault]`로 변환됩니다. DB 컬럼에 리터럴 기본값(`default 1` 등)이 설정돼 있으면 스키마를 가져올 때 이 칸에 자동으로 채워지며, 함수 기본값(`now()`·`gen_random_uuid()` 등)이나 jsonb 기본값은 제외됩니다.

```csharp
[DataColumn("level")]    internal int    level    = 1;        // 스칼라 → 필드 초기화
[DataColumn("nickname")] internal string nickname = "Guest";
[DataColumn("scores")] [AutoDefault(-1)] internal AutoList<int> scores = new AutoList<int>();  // Auto 컬렉션 → 요소 기본값
```

::: warning 타입을 정해야 생성됩니다
jsonb 컬럼에서 Dictionary의 value나 리스트 요소 타입을 정하지 않으면 ⚠ 로 표시되고, 그 상태로는 **소스 생성이 막힙니다**. 타입을 값 타입 등으로 좁히면(예: `Dictionary<string, int>`) 풀립니다.
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

    public static Task<bool> TryLoadAsync() => Instance.TryLoadAsync();

    // 필드는 internal — 데이터는 아래 정적 프로퍼티로 접근합니다.
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]   // Newtonsoft가 internal 필드를 저장/로드
    public sealed class Row
    {
        [DataColumn("level")]     internal int            level;
        [DataColumn("coins")]     internal int            coins;
        [DataColumn("inventory")] internal List<int>      inventory = new List<int>();   // 컬렉션은 빈 인스턴스로 초기화
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
