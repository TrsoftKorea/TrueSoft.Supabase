# Remote Config

---

## 패턴 선택

| 패턴 | API | 특징 |
|---|---|---|
| 읽기 함수 | `CreateRemoteConfigReader` | 필드에 선언 후 `await _getter()`. 만료 시 서버 응답 대기 후 반환 |
| 값 바인딩 | `CreateRemoteConfigBinding` | 폴링으로 자동 갱신. `Value`로 언제든 읽기 |
| 반응형 구독 | `CreateRemoteConfigListener` | 폴링으로 자동 갱신. 값 변경 시 콜백 호출 |

---

## 읽기 함수 (CreateRemoteConfigReader)

필드에 선언해두고 간결하게 호출합니다. 캐시가 만료됐으면 서버 응답을 기다린 후 반환합니다.

```csharp
// 선언
private readonly Func<Task<GameplayConfig>> _getConfig =
    Supabase.CreateRemoteConfigReader<GameplayConfig>("gameplay_v1");

// 사용 — 만료 시 서버 대기, 신선 시 즉시
var cfg = await _getConfig();
float dmg = cfg?.battle.playerDmg ?? 1f;
```

```csharp
// 유효시간 직접 지정 (기본값 300초)
private readonly Func<Task<GameplayConfig>> _getConfig =
    Supabase.CreateRemoteConfigReader<GameplayConfig>("gameplay_v1", maxStaleSeconds: 60);
```

## 값 바인딩 (CreateRemoteConfigBinding)

폴링으로 값을 최신 상태로 유지합니다. `Value`로 언제든 동기 읽기.

MonoBehaviour 안에서는 `this.CreateRemoteConfigBinding`을 사용하면 파괴 시 자동으로 해제됩니다.

```csharp
// MonoBehaviour — this. 하나로 자동 해제, OnDestroy 불필요
private RemoteConfigBinding<GameplayConfig> _gameplay;

void Start()
{
    _gameplay = this.CreateRemoteConfigBinding<GameplayConfig>("gameplay_v1", pollIntervalSeconds: 30f);
}

// 필요할 때 읽기 — async 없음
float dmg = _gameplay.Value?.battle.playerDmg ?? 1f;
```

```csharp
// MonoBehaviour 외부 또는 수동 관리가 필요한 경우
private readonly RemoteConfigBinding<GameplayConfig> _gameplay =
    Supabase.CreateRemoteConfigBinding<GameplayConfig>("gameplay_v1", pollIntervalSeconds: 30f);

void OnDestroy() => _gameplay.Dispose();
```

## 반응형 구독 (CreateRemoteConfigListener)

폴링으로 값이 바뀔 때마다 콜백을 호출합니다.

```csharp
// MonoBehaviour — this. 하나로 자동 해제, OnDestroy 불필요
void Start()
{
    this.CreateRemoteConfigListener<MaintenanceConfig>(
        "maintenance", pollIntervalSeconds: 30f, ApplyMaintenanceConfig);
}
```

```csharp
// MonoBehaviour 외부 또는 수동 관리가 필요한 경우
private IDisposable _maintenanceSub;

void Start()
{
    _maintenanceSub = Supabase.CreateRemoteConfigListener<MaintenanceConfig>(
        "maintenance", pollIntervalSeconds: 30f, cfg => ApplyMaintenanceConfig(cfg));
}

void OnDestroy() => _maintenanceSub?.Dispose();
```

> `invokeIfCached: false`를 지정하면 생성 시 캐시 값으로 즉시 콜백하지 않습니다 (기본 `true`).

---

## 설정 클래스 작성 규칙

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

**매개변수 없는 생성자 필요** — 중첩 클래스 포함, 생성자를 직접 정의했다면 기본 생성자를 명시해야 합니다.

```csharp
// ❌ 기본 생성자가 사라진 경우
public class BattleConfig
{
    public float playerDmg;
    public BattleConfig(float dmg) { playerDmg = dmg; }
}

// ✅ 기본 생성자 명시
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

## Cold Start 패턴

앱 시작 시 RemoteConfig를 자동으로 가져오지 않습니다.  
위 API를 처음 호출하는 순간 해당 키만 서버에서 조회합니다.  
이후에는 캐시에서 읽고, `maxStaleSeconds`가 지나면 갱신합니다.

## 설정 키 구조 권장

관련 설정은 하나의 키에 JSON 객체로 묶어 관리합니다.

```csharp
[Serializable]
public class GameplayConfig
{
    public StaminaConfig stamina;
    public BattleConfig battle;
}
```

DB 예시:
```
key:        "gameplay_v1"
value_json: {"stamina":{"maxEnergy":100,"regenSeconds":300},"battle":{"playerDmg":1.5}}
```

## Source Generator (선택)

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

## 로우레벨 폴링 설정

`SupabaseRuntime` Inspector의 **키별 폴링 주기 오버라이드** 목록에서 코드 없이 설정할 수 있습니다.  
코드로 설정할 경우 (초기화 시 1회):

```csharp
Supabase.SetRemoteConfigKeyPolling("maintenance", intervalSeconds: 30f);
```

## 로우레벨 구독

```csharp
Supabase.SubscribeRemoteConfig("gameplay_v1", json => {
    var cfg = JsonUtility.FromJson<GameplayConfig>(json);
    ApplyConfig(cfg);
}, invokeIfCached: true);
```

## 테이블 이름 변경

기본값은 `remote_config`입니다. `SupabaseSettings.remoteConfigTable`에서 변경할 수 있습니다.  
SQL: [`Sql/player/10_remote_config.sql`](../Sql/player/10_remote_config.sql)
