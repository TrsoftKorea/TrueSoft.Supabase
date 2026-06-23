# 클래스 생성

`SupabaseSettings` 에셋 Inspector 하단에서 Secret 키를 입력하고 **스키마 가져오기 → 소스 생성 → 저장**으로 `PlayerSave.cs`를 생성합니다.

::: info
타입을 자동으로 결정하지 못한 컬럼은 ⚠ 표시되며, 드롭다운에서 직접 지정할 수 있습니다.
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

---
