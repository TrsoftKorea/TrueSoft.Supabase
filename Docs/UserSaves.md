# 유저 세이브 (User Saves)

---

## StaticUserSave\<TRow\> — 권장 패턴

`StaticUserSave<TRow>`를 상속하면 스냅샷 관리·더티 추적·자동 동기화 등록을 모두 베이스 클래스가 처리합니다.

```csharp
public sealed class GameSave : StaticUserSave<GameSave.Row>
{
    public static readonly GameSave Instance = new("com.mygame.GameSave");
    private GameSave(string key) : base(key) { }

    // [DataTable("테이블명")] → "data_" 접두사 자동 추가 (예: "basic" → "data_basic")
    [DataTable("basic")]
    [Serializable]
    public sealed class Row
    {
        [DataColumn("level")] public int level;
        [DataColumn("coins")] public int coins;
    }

    // static 프로퍼티로 선언하면 GameSave.Level = 5; 처럼 간결하게 사용 가능
    public static int Level
    {
        get => Instance.Current.level;
        set { if (Instance.Current.level == value) return; Instance.Current.level = value; Instance.MarkDirty(); }
    }

    public static int Coins
    {
        get => Instance.Current.coins;
        set { if (Instance.Current.coins == value) return; Instance.Current.coins = value; Instance.MarkDirty(); }
    }
}
```

### 로드

```csharp
// 행이 없으면 자동 생성 후 DB 기본값을 로드해 반환
bool ok = await GameSave.Instance.TryLoadAsync();

// 로드 후 프로퍼티로 접근
int lv = GameSave.Level;
```

행이 없는 신규 유저 첫 로드 시:
1. `EnsureMyRowAsync`로 DB에 행 생성 (`ts_ensure_my_row` RPC)
2. DB 컬럼 `DEFAULT` 값을 로드해 `Current`에 반영
3. 이후 `MarkDirty()` / `TrySaveIfChangedAsync()` 정상 동작

### 저장

```csharp
// 프로퍼티 세터가 자동으로 MarkDirty() 호출
GameSave.Level = 10;
GameSave.Coins = 500;

// 즉시 저장 (변경분만 PATCH, 변경 없으면 네트워크 없음)
bool ok = await GameSave.Instance.TrySaveIfChangedAsync();
```

### 자동 동기화 (쿨타임 배치)

`MarkDirty()` 호출 시 `UserSaveStaticSyncRegistry`에 등록되어 쿨타임 내 자동으로 flush됩니다.  
`SupabaseRuntime`이 씬에 있으면 앱 Pause/Quit 시 dirty가 있으면 즉시 전송을 시도합니다.

```csharp
// 중요한 타이밍(결제 완료, 씬 전환 등)에 즉시 전송
GameSave.Instance.TryRequestImmediateSave();
await GameSave.Instance.TryFlushNowAsync(timeoutMs: 5000);

// 쿨타임 조정
GameSave.Instance.ConfigureCooldown(seconds: 5f);
```

### 다중 테이블

테이블마다 별도 클래스를 정의합니다. `syncKey`는 고유한 문자열을 사용하세요.

```csharp
public sealed class InventorySave : StaticUserSave<InventorySave.Row>
{
    public static readonly InventorySave Instance = new("com.mygame.InventorySave");
    private InventorySave(string key) : base(key) { }

    [DataTable("inventory")]
    [Serializable]
    public sealed class Row { /* ... */ }
}
```

### 테이블 생성

`admin_create_user_table` RPC로 필수 컬럼(`id`, `user_id`, `account_id unique`, `server_id`, `updated_at`)과 RLS 정책이 자동 생성됩니다.

```sql
select admin_create_user_table(
  'private',          -- 'public' | 'private'
  'data_basic',       -- 테이블명 (^data_[a-z0-9_]+$ 패턴)
  '기본 세이브',      -- 설명
  true,               -- 활성화 여부
  'level int not null default 1, coins int not null default 0'  -- 추가 컬럼
);
```

`account_id unique` 제약이 있어야 `ts_ensure_my_row`의 `ON CONFLICT (account_id)` 동작합니다.

---

## 저수준 API (직접 사용)

`StaticUserSave` 없이 직접 로드/저장이 필요한 경우 사용합니다.

### [DataColumn] / [DataTable] 어노테이션

```csharp
[DataTable("basic")]   // → DB 테이블 "data_basic"
[Serializable]
public class MySave
{
    [DataColumn] public int level;
    [DataColumn] public int coins;
    [DataColumn("last_login_at")] public string lastLoginAt;
}
```

### 로드

```csharp
var (success, hasRow, save) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<MySave>();
// hasRow == false → 행 없음 (신규 유저)
// hasRow == true  → 기존 유저
```

### 저장 (변경분만 전송)

```csharp
var prev = DataSchema.CloneRow(save);  // 스냅샷

save.level = 5;
save.coins = 200;

// 변경된 컬럼만 PATCH — 변경 없으면 네트워크 전송 없음
await Supabase.TryPatchUserDataDiffAsync(prev, save);
```

---

## 에디터 OpenAPI 클래스 생성기

메뉴 **TrueSoft > Supabase > 유저 데이터 클래스 생성**에서 DB 스키마를 기반으로 `[DataColumn]`이 붙은 C# 클래스 초안을 자동 생성할 수 있습니다.

1. `Resources/SupabaseSettings`가 있으면 URL·테이블명이 자동으로 채워집니다.
2. **Secret 키**는 Supabase 대시보드에서 복사해 창에 직접 입력합니다 (에셋에 저장하지 마세요).
3. 테이블명·제외 컬럼·클래스 이름·네임스페이스를 조정한 뒤 미리보기를 확인합니다.
4. **프로젝트에 .cs 저장…**으로 `Assets` 아래에 저장합니다.

생성기가 타입을 추론하지 못한 컬럼(`string /* refine */`)은 직접 수정해야 합니다.

---

## JsonUtility 주의사항

- PostgREST가 반환하는 JSON 키와 C# 필드 이름이 **정확히 일치**해야 값이 채워집니다.
- `[DataColumn("other_name")]`은 select/PATCH 키만 바꿉니다. JSON 역직렬화 키는 바뀌지 않습니다.
- DB 컬럼명과 C# 이름이 다르게 두고 싶다면 Newtonsoft 등 별도 역직렬화가 필요합니다.
