# Remote Config

## 패턴 선택

> [!TIP]
> 대부분의 경우 **읽기 함수** 또는 **값 바인딩** 패턴으로 충분합니다.  
> 값이 바뀔 때 UI를 즉시 갱신해야 하는 경우에만 반응형 구독을 사용하세요.

| 패턴 | API | 특징 |
|---|---|---|
| 읽기 함수 | `CreateRemoteConfigReader` | 필드에 선언 후 `await _getter()`. 만료 시 서버 응답 대기 후 반환 |
| 값 바인딩 | `CreateRemoteConfigBinding` | 폴링으로 자동 갱신. `Value`로 언제든 읽기 |
| 반응형 구독 | `CreateRemoteConfigListener` | 폴링으로 자동 갱신. 값 변경 시 콜백 호출 |

---

## 설정 클래스 작성

### 지원 타입

```csharp
public class GameplayConfig
{
    // 기본 타입
    public int count;
    public float damage;
    public bool enabled;
    public string message;

    // 컬렉션
    public int[] values;
    public List<string> items;
    public Dictionary<string, int> rates;

    // nullable
    public int? optionalCount;

    // 열거형 — 문자열("Normal") 또는 숫자(1) 모두 가능
    public Difficulty difficulty;

    // 중첩 클래스
    public BattleConfig battle;
    public List<RewardConfig> rewards;
}
```

### 규칙

> [!WARNING]
> 중첩 클래스를 포함한 모든 설정 클래스에 **매개변수 없는 생성자**가 필요합니다.  
> 생성자를 직접 정의했다면 기본 생성자를 명시해야 합니다.

```csharp
public class BattleConfig
{
    public float playerDmg;
    public BattleConfig() { }
    public BattleConfig(float dmg) { playerDmg = dmg; }
}
```

**필드명과 JSON 키가 다를 때** — `[JsonProperty]`로 매핑합니다.

```csharp
using Newtonsoft.Json;

public class BattleConfig
{
    [JsonProperty("player_dmg")]
    public float PlayerDmg;
}
```

**필수 필드 선언** — 키가 없으면 fetch 자체를 실패시킵니다.

```csharp
[JsonProperty("player_dmg", Required = Required.Always)]
public float PlayerDmg;
```

**중첩 객체는 항상 `?.`로 접근** — JSON에 해당 키가 없으면 `null`입니다.

```csharp
float dmg = cfg?.battle?.playerDmg ?? 1f;  // battle이 없어도 안전
```

---

## 대시보드에서 값 입력

관련 설정은 하나의 키에 JSON 객체로 묶어 관리합니다. 스칼라 값마다 키를 만들지 않아도 됩니다.

```csharp
[Serializable]
public class GameplayConfig
{
    public StaminaConfig stamina;
    public BattleConfig battle;
}
```

Supabase 대시보드 좌측 사이드바 **Table Editor > remote_config** 테이블에서 행을 추가합니다.

| 컬럼 | 값 예시 | 설명 |
|------|---------|------|
| `key` | `gameplay_v1` | C#에서 조회할 키 이름 |
| `value_json` | `{"stamina":{...},"battle":{...}}` | JSON 형태의 설정값 |
| `max_stale_seconds` | `300` | 캐시 유효 시간(초). 0이면 300초로 처리 |
| `poll_interval_seconds` | `60` | 백그라운드 갱신 주기(초). 0이면 폴링 없음 |

```json
{
  "stamina": { "maxEnergy": 100, "regenSeconds": 300 },
  "battle":  { "playerDmg": 1.5 }
}
```

---

## 읽기 함수 (CreateRemoteConfigReader)

첫 호출 시 생성(`??=`)해두고 간결하게 호출합니다. 캐시가 만료됐으면 서버 응답을 기다린 후 반환합니다.

```csharp
private Func<Task<GameplayConfig>> _getConfig;

private async Task LoadConfigAsync()
{
    _getConfig ??= Supabase.CreateRemoteConfigReader<GameplayConfig>("gameplay_v1");

    var cfg = await _getConfig();
    float dmg = cfg?.battle?.playerDmg ?? 1f;
}
```

```csharp
// 유효시간 직접 지정 (기본값 300초)
_getConfig ??= Supabase.CreateRemoteConfigReader<GameplayConfig>("gameplay_v1", maxStaleSeconds: 60);
```

---

## 값 바인딩 (CreateRemoteConfigBinding)

폴링으로 값을 최신 상태로 유지합니다. `Value`로 언제든 동기 읽기.

```csharp
private RemoteConfigBinding<GameplayConfig> _gameplay;

private void Start()
{
    _gameplay = Supabase.CreateRemoteConfigBinding<GameplayConfig>("gameplay_v1", pollIntervalSeconds: 30f);
}

private void Update()
{
    float dmg = _gameplay.Value?.battle?.playerDmg ?? 1f;
}

private void OnDestroy() => _gameplay?.Dispose();
```

---

## 반응형 구독 (CreateRemoteConfigListener)

폴링으로 값이 바뀔 때마다 콜백을 호출합니다.

```csharp
private RemoteConfigListener<MaintenanceConfig> _maintenanceSub;

private void Start()
{
    _maintenanceSub = Supabase.CreateRemoteConfigListener<MaintenanceConfig>(
        "maintenance", pollIntervalSeconds: 30f, ApplyMaintenanceConfig);
}

private void OnDestroy() => _maintenanceSub?.Dispose();
```

> `invokeIfCached: false`를 지정하면 생성 시 캐시 값으로 즉시 콜백하지 않습니다 (기본 `true`).

---

## Cold Start 패턴

> [!NOTE]
> 앱 시작 시 RemoteConfig를 자동으로 가져오지 않습니다.  
> 위 API를 처음 호출하는 순간 해당 키만 서버에서 조회합니다.  
> 이후에는 캐시에서 읽고, `maxStaleSeconds`가 지나면 **만료된 캐시 값을 즉시 반환하면서 동시에 백그라운드에서 서버 갱신을 시작합니다** (stale-while-revalidate 패턴). 갱신이 완료되면 다음 호출부터 새 값이 반환됩니다.

---

## 고급 설정

### Source Generator (선택)

`static partial` 메서드에 어노테이션을 붙이면 구현이 자동 생성됩니다.

```csharp
[RemoteConfig]
public static partial class GameRemoteConfig
{
    [RemoteConfigKey("gameplay_v1")]
    public static partial RemoteConfigEntry<GameplayConfig> Gameplay();
}

// 사용
var result = await GameRemoteConfig.Gameplay().FetchAsync();
```

패키지 루트 `RoslynAnalyzers/Truesoft.Supabase.RemoteConfig.SourceGenerator.dll`이 자동으로 포함됩니다.

### 폴링 주기 사전 설정

초기화 시 1회 호출합니다. 이후 해당 키의 Binding·Listener 생성 시 별도 지정이 없어도 이 주기가 사용됩니다.

```csharp
Supabase.SetRemoteConfigKeyPolling("maintenance", intervalSeconds: 30f);
```

### 테이블 이름

`remote_config`로 고정되어 있습니다.  
SQL: `Sql/player/08_remote_config.sql`
