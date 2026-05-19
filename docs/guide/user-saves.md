# 유저 세이브

## 클래스 생성

`SupabaseSettings` 에셋 Inspector 하단에서 Secret 키를 입력하고 **스키마 가져오기 → 소스 생성 → 저장**으로 `PlayerSave.cs`를 생성합니다.

> [!NOTE]
> 타입을 자동으로 결정하지 못한 컬럼은 ⚠ 표시되며, 드롭다운에서 직접 지정할 수 있습니다.

생성된 파일은 다음과 같은 구조입니다.

```csharp
// PlayerSave.cs — 생성기로 자동 생성, 직접 수정하지 않습니다
public sealed partial class PlayerSave : StaticUserSave<PlayerSave.Row>
{
    public static readonly PlayerSave Instance = new();
    private PlayerSave() : base() { }

    public static Task<bool> TryLoadAsync() => Instance.TryLoadAsync();

    [Serializable]
    public sealed class Row
    {
        [DataColumn("level")] public int level;
        [DataColumn("coins")] public int coins;
    }

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
}
```

새 컬럼이 생기면 생성기를 다시 실행해 덮어씁니다.

---

## 로드

로그인 완료 후 한 번 호출합니다.

```csharp
bool ok = await PlayerSave.TryLoadAsync();
```

로드가 끝난 뒤 실행할 코드가 있으면 `OnLoaded` 이벤트를 구독합니다.

```csharp
// 구독은 TryLoadAsync() 호출 전 어디서든 한 번만
PlayerSave.Instance.OnLoaded += ApplyGameData;
```

> [!NOTE]
> 신규 유저는 DB 행이 자동으로 생성됩니다.

---

## 읽기 / 쓰기

생성된 static 프로퍼티로 접근합니다.

```csharp
int lv = PlayerSave.Level;

PlayerSave.Level = 10;
PlayerSave.Coins += 100;
```

값을 쓰면 `MarkDirty()`가 자동으로 호출되고, `SupabaseRuntime`이 쿨타임 주기로 자동 저장합니다.

---

## 즉시 저장

씬 전환·결제 완료·앱 종료처럼 지금 당장 저장해야 할 때 사용합니다.

```csharp
await Supabase.TryFlushAllUserSaveImmediateAsync(timeoutMs: 5000);
```

> [!NOTE]
> `SupabaseRuntime`을 씬에 배치하면 `OnApplicationPause` / `OnApplicationQuit` 시 자동으로 플러시합니다.

---

## 컬럼 추가

새 컬럼은 Supabase SQL Editor 또는 Retool에서 추가합니다. 게임 클라이언트에서 호출하지 않습니다.

추가 후 생성기를 다시 실행하면 `PlayerSave.cs`에 해당 프로퍼티가 자동으로 추가됩니다.

---

## 참고

### 클래스 직접 작성

생성기를 사용하지 않고 직접 작성할 수도 있습니다.

```csharp
public sealed partial class PlayerSave : StaticUserSave<PlayerSave.Row>
{
    public static readonly PlayerSave Instance = new();
    private PlayerSave() : base() { }

    [Serializable]
    public sealed class Row
    {
        [DataColumn("level")] public int level;
    }

    public static int Level
    {
        get => Instance.Current.level;
        set { Instance.Current.level = value; Instance.MarkDirty(); }
    }
}
```

> [!IMPORTANT]
> 이 클래스는 프로젝트 전체에서 **정확히 하나**만 존재해야 합니다.  
> 모든 게임 데이터를 하나의 `Row` 안에 선언하세요.

### JSON 직렬화 주의사항

> [!WARNING]
> `[DataColumn("other_name")]`은 select/PATCH 키만 바꿉니다. 역직렬화는 **필드 이름** 기준입니다.  
> DB 컬럼명과 C# 필드명이 다를 때는 `[JsonProperty]`를 함께 사용하세요.

```csharp
[DataColumn("last_login_at")]
[JsonProperty("last_login_at")]
public string lastLoginAt;
```

