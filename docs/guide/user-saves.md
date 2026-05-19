# 유저 세이브 (User Saves)

## StaticUserSave — 권장 패턴

> [!TIP]
> `StaticUserSave<TRow>`를 상속하면 스냅샷 관리·더티 추적·자동 동기화 등록을 모두 베이스 클래스가 처리합니다.

```csharp
public sealed partial class GameSave : StaticUserSave<GameSave.Row>
{
    public static readonly GameSave Instance = new();
    private GameSave() : base() { }

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

`partial`이므로 게임 로직은 별도 파일로 분리할 수 있습니다.

```csharp
// GameSave.Hooks.cs
public sealed partial class GameSave
{
    public static void OnLoadAll() { /* 로드 후 처리 */ }
}
```

### 로드

```csharp
// 신규 유저: DB에 행을 자동 생성하고 컬럼 기본값을 로드합니다
bool ok = await GameSave.Instance.TryLoadAsync();
int lv = GameSave.Level;
```

### 저장

```csharp
GameSave.Level = 10;  // setter가 MarkDirty() 자동 호출

// 즉시 저장 (변경분만 PATCH, 변경 없으면 네트워크 없음)
bool ok = await GameSave.Instance.TrySaveIfChangedAsync();
```

### 자동 동기화 (쿨타임 배치)

```csharp
// 특정 세이브만 즉시 전송 (결제 완료, 씬 전환 등)
GameSave.Instance.TryRequestImmediateSave();
await GameSave.Instance.TryFlushNowAsync(timeoutMs: 5000);

// 모든 StaticUserSave 인스턴스를 한 번에 즉시 전송 (앱 종료 직전 등)
await Supabase.TryFlushAllUserSaveImmediateAsync(timeoutMs: 5000);

// 쿨타임 조정
GameSave.Instance.ConfigureCooldown(seconds: 5f);
```

### 컬럼 추가

새 게임 데이터 컬럼은 Supabase SQL Editor 또는 Retool에서 `admin_add_user_data_column` RPC로 추가합니다 (게임 클라이언트에서 호출하지 않습니다).

> [!IMPORTANT]
> `StaticUserSave<TRow>`를 상속하는 클래스는 프로젝트 전체에서 **정확히 하나**여야 합니다.  
> 두 번째 서브클래스의 인스턴스가 생성되는 순간 `InvalidOperationException`이 발생합니다.  
> 모든 게임 데이터는 하나의 `Row` 클래스 안에 `[DataColumn]` 필드로 선언하세요.

---

## 참고

### 저수준 API

`StaticUserSave` 없이 REST API를 직접 호출합니다.

```csharp
// [DataColumn] 어노테이션으로 컬럼 매핑
[Serializable]
public class MySave
{
    [DataColumn] public int level;
    [DataColumn("last_login_at")] public string lastLoginAt;
}

// 로드
var (success, hasRow, save) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<MySave>();

// 저장 (변경분만 전송)
var prev = DataSchema.CloneRow(save);
save.level = 5;
await Supabase.TryPatchUserDataDiffAsync(prev, save);
```

### 에디터 클래스 생성기

`SupabaseSettings` 에셋 Inspector 하단에서 Secret 키(**Project Settings > API > Secret key**)를 입력하고 **스키마 가져오기**를 누르면 DB 컬럼 목록이 표시됩니다. 컬럼별로 타입을 확인·수정한 뒤 **소스 생성 → .cs 저장**으로 `PlayerSave` 클래스를 생성합니다.

> [!NOTE]
> 타입을 자동으로 결정하지 못한 컬럼은 ⚠ 표시되며, 드롭다운에서 직접 지정할 수 있습니다.

### JSON 직렬화 주의사항

> [!WARNING]
> `[DataColumn("other_name")]`은 select/PATCH 키만 바꿉니다. 역직렬화 키는 **필드 이름** 기준입니다.  
> DB 컬럼명과 C# 필드명이 달라야 한다면 `[JsonProperty("db_column_name")]`으로 매핑하세요.

```csharp
using Newtonsoft.Json;

public sealed class Row
{
    [DataColumn("last_login_at")]
    [JsonProperty("last_login_at")]  // DB 컬럼명과 필드명이 다를 때
    public string lastLoginAt;
}
```
