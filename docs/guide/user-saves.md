# 유저 세이브 (User Saves)

## 목차
- [StaticUserSave — 권장 패턴](#staticusersave-권장-패턴)
- [저수준 API](#저수준-api-직접-사용)
- [에디터 클래스 생성기](#에디터-openapi-클래스-생성기)
- [JSON 직렬화 주의사항](#json-직렬화-주의사항)

---

## StaticUserSave — 권장 패턴

> [!TIP]
> `StaticUserSave<TRow>`를 상속하면 스냅샷 관리·더티 추적·자동 동기화 등록을 모두 베이스 클래스가 처리합니다.  
> 저수준 API는 특수한 경우에만 사용하세요.

```csharp
public sealed class GameSave : StaticUserSave<GameSave.Row>
{
    public static readonly GameSave Instance = new();
    private GameSave() : base() { }  // syncKey 기본값: typeof(Row).FullName

    // DB 테이블: user_data (고정)
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
// 특정 세이브만 즉시 전송 (결제 완료, 씬 전환 등)
GameSave.Instance.TryRequestImmediateSave();
await GameSave.Instance.TryFlushNowAsync(timeoutMs: 5000);

// 모든 StaticUserSave 인스턴스를 한 번에 즉시 전송 (앱 종료 직전 등)
await Supabase.TryFlushAllUserSaveImmediateAsync(timeoutMs: 5000);

// 쿨타임 조정
GameSave.Instance.ConfigureCooldown(seconds: 5f);
```


### 테이블 생성

`Sql/player/15_user_data.sql`을 Supabase 대시보드 **SQL Editor**(`Ctrl+E`)에서 실행하면 `user_data` 테이블과 `admin_add_user_data_column` RPC가 생성됩니다.

새 게임 데이터 컬럼은 `admin_add_user_data_column` RPC로 추가합니다:

```sql
-- 컬럼명, 타입·제약 순서로 입력
SELECT admin_add_user_data_column('exp',           'int not null default 0');
SELECT admin_add_user_data_column('last_login_at', 'timestamptz');
```

이미 존재하는 컬럼이면 아무것도 하지 않습니다(멱등 실행 안전).

> [!IMPORTANT]
> `StaticUserSave<TRow>`를 상속하는 클래스는 프로젝트 전체에서 **정확히 하나**여야 합니다.  
> 여러 클래스를 만들면 모두 같은 `user_data` 테이블에 접근해 컬럼 충돌이 발생할 수 있습니다.

---

## 저수준 API (직접 사용)

`StaticUserSave` 없이 직접 로드/저장이 필요한 경우 사용합니다.

### [DataColumn] 어노테이션

```csharp
// DB 테이블: user_data (고정)
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

> [!NOTE]
> 생성기가 타입을 추론하지 못한 컬럼(`string /* refine */`)은 직접 수정해야 합니다.

---

## JSON 직렬화 주의사항

SDK는 Newtonsoft.Json을 사용합니다. PostgREST가 반환하는 JSON 키와 C# 필드 이름이 일치해야 값이 채워집니다.

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
