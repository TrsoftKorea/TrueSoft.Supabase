# Remote Config

## 패턴 선택

> [!TIP]
> 대부분의 경우 **읽기 함수** 또는 **값 바인딩** 패턴으로 충분합니다.  
> 값이 바뀔 때 UI를 즉시 갱신해야 하는 경우에만 반응형 구독을 사용하세요.

| 패턴 | API | 특징 |
|---|---|---|
| 읽기 함수 | `RemoteConfig<T>.CreateReader()` | 필드에 선언 후 `await _getter()`. 만료 시 서버 응답 대기 후 반환 |
| 값 바인딩 | `RemoteConfig<T>.CreateBinding()` | 폴링으로 자동 갱신. `Value`로 언제든 읽기 |
| 반응형 구독 | `RemoteConfig<T>.CreateListener(onChange)` | 폴링으로 자동 갱신. 값 변경 시 콜백 호출 |

---

## 설정 클래스 작성

설정 클래스에 `[RemoteConfigKey("키이름")]` 어트리뷰트를 붙이면 `RemoteConfig<T>`가 자동으로 키를 읽습니다.

```csharp
using Newtonsoft.Json;
using Truesoft.Supabase.Unity;

[RemoteConfigKey("gameplay_v1")]
public class GameplayConfig
{
    // 기본 타입
    public int count;
    public float damage;
    public bool enabled;
    public string message;

    // 중첩 클래스
    public StaminaConfig stamina;
    public BattleConfig  battle;

    public class StaminaConfig { public int max; public int regenSec; }
    public class BattleConfig  { public float playerDmg; }
}
```

**필드명과 JSON 키가 다를 때** — `[JsonProperty]`로 매핑합니다.

```csharp
[JsonProperty("player_dmg")]
public float PlayerDmg;
```

**중첩 객체는 항상 `?.`로 접근** — JSON에 해당 키가 없으면 `null`입니다.

```csharp
float dmg = cfg?.battle?.playerDmg ?? 1f;  // battle이 없어도 안전
```

### 규칙

> [!WARNING]
> 중첩 클래스를 포함한 모든 설정 클래스에 **매개변수 없는 생성자**가 필요합니다.  
> 생성자를 직접 정의했다면 기본 생성자를 명시해야 합니다.

---

## 대시보드에서 값 입력

관련 설정은 하나의 키에 JSON 객체로 묶어 관리합니다. 스칼라 값마다 키를 만들지 않아도 됩니다.

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

## 읽기 함수 (CreateReader)

첫 호출 시 생성(`??=`)해두고 간결하게 호출합니다. 캐시가 만료됐으면 서버 응답을 기다린 후 반환합니다.

```csharp
private Func<Task<GameplayConfig>> _getConfig;

private async Task LoadConfigAsync()
{
    _getConfig ??= RemoteConfig<GameplayConfig>.CreateReader();

    var cfg = await _getConfig();
    float dmg = cfg?.battle?.playerDmg ?? 1f;
}
```

```csharp
// 유효시간 직접 지정 (기본값 300초)
_getConfig ??= RemoteConfig<GameplayConfig>.CreateReader(maxStaleSeconds: 60);
```

---

## 값 바인딩 (CreateBinding)

폴링으로 값을 최신 상태로 유지합니다. `Value`로 언제든 동기 읽기.

```csharp
private RemoteConfigBinding<GameplayConfig> _gameplay;

private void Start()
{
    _gameplay = RemoteConfig<GameplayConfig>.CreateBinding(pollIntervalSeconds: 30f);
}

private void Update()
{
    float dmg = _gameplay.Value?.battle?.playerDmg ?? 1f;
}

private void OnDestroy() => _gameplay?.Dispose();
```

---

## 반응형 구독 (CreateListener)

폴링으로 값이 바뀔 때마다 콜백을 호출합니다.

```csharp
private RemoteConfigListener<MaintenanceConfig> _maintenanceSub;

private void Start()
{
    _maintenanceSub = RemoteConfig<MaintenanceConfig>.CreateListener(
        ApplyMaintenanceConfig, pollIntervalSeconds: 30f);
}

private void OnDestroy() => _maintenanceSub?.Dispose();
```

> `invokeIfCached: false` 옵션이 필요하면 `new RemoteConfigListener<T>(key, interval, callback, false)` 로 직접 생성합니다.

---

## Cold Start 패턴

> [!NOTE]
> 앱 시작 시 RemoteConfig를 자동으로 가져오지 않습니다.  
> 위 API를 처음 호출하는 순간 해당 키만 서버에서 조회합니다.  
> 이후에는 캐시에서 읽고, `maxStaleSeconds`가 지나면 **만료된 캐시 값을 즉시 반환하면서 동시에 백그라운드에서 서버 갱신을 시작합니다** (stale-while-revalidate 패턴). 갱신이 완료되면 다음 호출부터 새 값이 반환됩니다.

---

## 고급 설정

### 폴링 주기 사전 설정

초기화 시 1회 호출합니다. 이후 해당 키의 Binding·Listener 생성 시 별도 지정이 없어도 이 주기가 사용됩니다.

```csharp
Supabase.SetRemoteConfigKeyPolling("maintenance", intervalSeconds: 30f);
```

### 테이블 이름

`remote_config`로 고정되어 있습니다.  
SQL: `Sql/player/08_remote_config.sql`
