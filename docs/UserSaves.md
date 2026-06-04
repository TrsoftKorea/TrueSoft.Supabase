# 유저 세이브 (User Saves)

---

## [DataColumn] 어노테이션

DB 컬럼과 C# 필드를 매핑합니다. 인자를 생략하면 멤버 이름이 컬럼명으로 사용됩니다.

```csharp
[Serializable]
public class MyData
{
    [DataColumn] public int level;
    [DataColumn] public int coins;
    [DataColumn("last_login_at")] public string lastLoginAt;
}
```

## StaticUserSave 패턴 (권장)

`StaticUserSave<TRow>`를 상속해 정적 싱글턴으로 사용합니다. dirty 감지, 쿨다운 배치 전송, 앱 종료 시 즉시 저장을 자동으로 처리합니다.

```csharp
public sealed class GameSave : StaticUserSave<GameSave.Row>
{
    public static readonly GameSave Instance = new();
    private GameSave() { }

    [Serializable]
    public sealed class Row
    {
        [DataColumn] public int level;
        [DataColumn] public int coins;
    }

    // GameSave.Level = 5; 처럼 간결하게 사용
    public static int Level
    {
        get => Instance.Current.level;
        set { if (Instance.Current.level == value) return; Instance.Current.level = value; Instance.MarkDirty(); }
    }
}
```

로드:

```csharp
await GameSave.Instance.TryLoadAsync();
```

값 변경 → dirty 플래그만 세팅, `SupabaseRuntime`이 쿨다운마다 자동 전송:

```csharp
GameSave.Level = 5;
```

중요한 타이밍(씬 전환, 앱 종료 직전)에 즉시 전송:

```csharp
await GameSave.Instance.TryFlushNowAsync();
// 또는 즉시 플러시 요청 (fire-and-forget)
GameSave.Instance.TryRequestImmediateSave();
```

## 저급 API (직접 제어)

```csharp
// 로드 (매핑 컬럼만 선택 조회)
var row = await Supabase.TryLoadUserDataAttributedAsync<MyData>();

// 신규 유저 여부 구분
var (success, hasRow, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<MyData>();
// hasRow == false → 첫 접속 (DB에 행 없음)
// hasRow == true  → 기존 유저

// 변경된 컬럼만 PATCH — 변경 없으면 전송 없음
await Supabase.TryPatchUserDataDiffAsync(prev, current);
```

## ApplyRow — 외부 데이터 주입

DB를 재조회하지 않고 `Row`를 직접 주입합니다. PlayNanoo 이관 등 외부 소스에서 데이터를 가져올 때 사용합니다.

```csharp
var nanooRow = JsonUtility.FromJson<GameSave.Row>(nanooJson);
GameSave.Instance.ApplyRow(nanooRow);
// Current와 _lastSynced 모두 갱신, dirty 초기화, OnLoaded 발행
```

## 에디터 클래스 생성기

메뉴 **TrueSoft > Supabase > 유저 데이터 클래스 생성**에서 DB OpenAPI 스키마 기반으로 `StaticUserSave<TRow>` 코드 초안을 자동 생성합니다.

1. `Resources/SupabaseSettings`가 있으면 URL·테이블명이 자동으로 채워집니다.
2. **Secret 키**는 Supabase 대시보드에서 복사해 창에 직접 입력합니다 (에셋에 저장하지 마세요).
3. 테이블명·제외 컬럼·클래스 이름·네임스페이스를 조정한 뒤 미리보기를 확인합니다.
4. **프로젝트에 .cs 저장…**으로 `Assets` 아래에 저장합니다.

생성된 모든 클래스에는 `updated_at` 필드가 Row에 자동으로 포함됩니다.  
이 필드는 DB trigger가 관리하므로 직접 설정하는 정적 setter는 생성되지 않습니다.

타입 추론이 안 된 컬럼(`string /* refine */`)은 직접 수정하세요.

## Newtonsoft.Json 주의사항

SDK는 Newtonsoft.Json으로 역직렬화합니다. `[DataColumn("other_name")]`은 select/PATCH 키만 변경합니다.

- DB 컬럼명과 C# 필드명이 다른 경우 `[JsonProperty("db_column_name")]`도 함께 추가하세요.
- `jsonb` 배열 등 복합 타입은 수동 설계가 필요합니다.

## 쿨다운 조정

기본 저장 주기는 `SupabaseSettings` Inspector에서 설정합니다. 코드에서 동적으로 변경할 수도 있습니다.

```csharp
// 모든 우선순위 동일 설정
GameSave.Instance.ConfigureCooldown(seconds: 5f);

// 특정 우선순위만 변경
GameSave.Instance.ConfigureCooldown(seconds: 1f, DataSavePriority.Urgent);
```

`SupabaseRuntime`이 씬에 있으면 앱 Pause/Quit 시 dirty가 있으면 즉시 전송을 시도합니다.
