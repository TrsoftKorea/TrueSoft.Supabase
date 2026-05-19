# 유저 세이브 (User Saves)

## StaticUserSave — 권장 패턴

> [!TIP]
> `StaticUserSave<TRow>`를 상속하면 스냅샷 관리·더티 추적·자동 동기화 등록을 모두 베이스 클래스가 처리합니다.

```csharp
public sealed class GameSave : StaticUserSave<GameSave.Row>
{
    public static readonly GameSave Instance = new();
    private GameSave() : base() { }

    // DB 테이블: user_data (고정)
    [Serializable]
    public sealed class Row
    {
        [DataColumn("level")] public int level;
        [DataColumn("coins")] public int coins;
    }
}
```

### 로드

```csharp
// 신규 유저: DB에 행을 자동 생성하고 컬럼 기본값을 로드합니다
bool ok = await GameSave.Instance.TryLoadAsync();
int lv = GameSave.Instance.Current.level;
```

### 저장

```csharp
GameSave.Instance.Current.level = 10;
GameSave.Instance.MarkDirty();  // 쿨타임 배치 자동 전송

// 즉시 저장 (변경분만 PATCH, 변경 없으면 네트워크 없음)
bool ok = await GameSave.Instance.TrySaveIfChangedAsync();
```

> [!TIP]
> `Row` 필드를 static 프로퍼티로 래핑하면 `GameSave.Level = 10;`처럼 간결하게 호출할 수 있습니다.

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

새 게임 데이터 컬럼은 `admin_add_user_data_column` RPC로 추가합니다:

```sql
-- 컬럼명, 타입·제약 순서로 입력
SELECT admin_add_user_data_column('exp',           'int not null default 0');
SELECT admin_add_user_data_column('last_login_at', 'timestamptz');
```

이미 존재하는 컬럼이면 아무것도 하지 않습니다(멱등 실행 안전).

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

메뉴 **TrueSoft > Supabase > 유저 데이터 클래스 생성**에서 DB 스키마 기반으로 `[DataColumn]`이 붙은 C# 클래스 초안을 자동 생성합니다. Secret 키(**Project Settings > API > Secret key**)를 창에 직접 입력한 뒤 저장합니다.

> [!NOTE]
> 생성기가 타입을 추론하지 못한 컬럼(`string /* refine */`)은 직접 수정해야 합니다.

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
