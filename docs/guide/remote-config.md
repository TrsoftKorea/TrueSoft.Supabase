# Remote Config

Remote Config는 앱을 업데이트하지 않고도 서버에서 게임 수치를 바꿀 수 있는 기능입니다.  
예를 들어 스테미나 최대치, 몬스터 체력 배율, 이벤트 배너 텍스트 등을 DB에서 관리할 수 있습니다.

---

## 빠른 시작

### 1단계 — 설정 클래스 작성

DB에 저장된 JSON 구조와 같은 모양의 C# 클래스를 만들고 `[RemoteConfigKey]`를 붙입니다.

```csharp
using Newtonsoft.Json;
using TrueBase.Unity;

[RemoteConfigKey("gameplay_v1")]   // DB의 키 이름
public class GameplayConfig
{
    public bool  enabled;
    public int   maxStamina;
    public float spawnInterval;
}
```

### 2단계 — DB에 값 입력

Retool에서 `remote_config` 테이블에 값을 입력합니다.

> 자세한 내용은 추후 업데이트 예정입니다.

### 3단계 — 코드에서 사용

```csharp
// 가장 간단한 사용법 — 값이 필요할 때 한 번 읽기
var reader = RemoteConfig<GameplayConfig>.CreateReader();
var cfg = await reader();

if (cfg != null)
{
    maxStamina = cfg.maxStamina;
}
```

이게 전부입니다. 이후 패턴은 상황에 따라 선택하세요.

---

## 어떤 패턴을 써야 하나요?

| 상황 | 추천 패턴 |
|------|-----------|
| 가끔 읽으면 충분할 때 | **Reader** |
| 매 프레임 또는 자주 값을 읽어야 할 때 | **Binding** |
| 값이 바뀌는 순간 즉시 반응해야 할 때 | **Listener** |

> 대부분의 경우 **Reader**나 **Binding**으로 충분합니다.

---

## Reader

"지금 이 값이 필요해"라고 요청하는 방식입니다.  
서버에서 한 번 가져와 저장해두고, 다음 호출부터는 저장된 값을 빠르게 반환합니다.  
저장된 값이 오래됐으면 서버에서 새 값을 받아온 뒤 반환합니다.

```csharp
private Func<Task<GameplayConfig>> _getConfig;

private async Task LoadConfigAsync()
{
    // 처음 한 번만 생성하고 재사용합니다
    _getConfig ??= RemoteConfig<GameplayConfig>.CreateReader();

    var cfg = await _getConfig();
    float interval = cfg?.spawnInterval ?? 3f;  // 값이 없으면 기본값 3f 사용
}
```

```csharp
// 저장된 값을 무시하고 항상 서버에서 새로 받고 싶을 때
_getConfig ??= RemoteConfig<GameplayConfig>.CreateReader(maxStale: 0);
```

---

## Binding

백그라운드에서 주기적으로 값을 자동 갱신하는 방식입니다.  
`Value`로 언제든 최신 값을 바로 읽을 수 있습니다.

```csharp
private RemoteConfigBinding<GameplayConfig> _gameplay;

private void Start()
{
    // 60초마다 서버에서 자동으로 값을 갱신합니다
    _gameplay = RemoteConfig<GameplayConfig>.CreateBinding(pollInterval: 60f);
}

private void Update()
{
    // .Value로 저장된 최신 값을 즉시 읽습니다 (네트워크 호출 없음)
    float interval = _gameplay.Value?.spawnInterval ?? 3f;
}

private void OnDestroy() => _gameplay?.Dispose();  // 반드시 해제
```

---

## Listener

값이 바뀌는 순간 자동으로 콜백 함수를 호출하는 방식입니다.

```csharp
private RemoteConfigListener<GameplayConfig> _listener;

private void Start()
{
    _listener = RemoteConfig<GameplayConfig>.CreateListener(
        onChange: cfg => ApplyConfig(cfg),  // 값이 바뀔 때 호출
        pollInterval: 60f);
}

private void ApplyConfig(GameplayConfig cfg)
{
    spawnInterval = cfg?.spawnInterval ?? 3f;
    Debug.Log("설정 갱신됨");
}

private void OnDestroy() => _listener?.Dispose();  // 반드시 해제
```

---

## 동작 방식

앱을 시작할 때 Remote Config를 자동으로 가져오지 않습니다.  
`CreateReader()`, `CreateBinding()`, `CreateListener()` 중 하나를 처음 호출하는 순간 해당 키만 서버에서 가져옵니다.

이후에는 가져온 값을 메모리에 보관해두고 빠르게 반환합니다.  
설정된 유효 시간이 지나면 낡은 값을 즉시 반환하면서 **동시에** 백그라운드에서 서버 갱신을 시작합니다. 갱신이 완료되면 다음 호출부터 새 값이 반환됩니다.

> 즉, 값을 읽는 속도는 항상 빠르고, 서버 갱신은 뒤에서 알아서 처리됩니다.

---

## 더 알아보기

더 복잡한 JSON 구조나 타입이 필요할 때 참고하세요.

### 설정 클래스 작성

DB에 저장된 JSON 구조에 맞게 클래스를 작성합니다. 지원하는 타입은 다음과 같습니다.

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using TrueBase.Unity;

[RemoteConfigKey("gameplay_v1")]
public class GameplayConfig
{
    // ── 기본 타입 ────────────────────────────────────────────────────────────
    public bool   enabled;          // JSON: "enabled"
    public int    maxLevel;         // JSON: "maxLevel"
    public float  spawnInterval;    // JSON: "spawnInterval"
    public string announcement;     // JSON: "announcement"

    // ── JSON 키와 필드명이 다를 때 ────────────────────────────────────────────
    [JsonProperty("max_stamina")]
    public int MaxStamina;          // JSON: "max_stamina" → C#: MaxStamina

    // ── nullable (JSON에 키가 없을 때 null로 남겨두고 싶을 때) ────────────────
    public int?   bonusLevel;       // null이면 기본값 적용
    public float? eventMultiplier;

    // ── 컬렉션 ────────────────────────────────────────────────────────────────
    public List<string>            bannedWords;    // JSON: ["word1", "word2"]
    public int[]                   stageClearExp;  // JSON: [100, 200, 400]
    public Dictionary<string, int> itemDropRate;   // JSON: {"sword": 10, "shield": 5}

    // ── 중첩 클래스 ───────────────────────────────────────────────────────────
    public StaminaConfig stamina;   // JSON: "stamina": { ... }
    public BattleConfig  battle;    // JSON: "battle":  { ... }

    public class StaminaConfig
    {
        public int   max;
        public int   regenSec;
        public float regenAmount;
    }

    public class BattleConfig
    {
        public float playerDmg;
        public float enemyHpScale;

        // 중첩 안의 중첩도 가능
        public BossConfig boss;

        public class BossConfig
        {
            public float hpMultiplier;
            public int   spawnFloor;
        }
    }
}
```

::: warning
중첩 클래스를 포함한 모든 설정 클래스에 **매개변수 없는 생성자**가 필요합니다.  
생성자를 직접 정의했다면 기본 생성자도 명시해야 합니다.
:::

**중첩 객체는 `?.`로 접근** — JSON에 해당 키가 없으면 `null`이므로 반드시 null 체크를 합니다.

```csharp
float dmg    = cfg?.battle?.playerDmg ?? 1f;
float bossHp = cfg?.battle?.boss?.hpMultiplier ?? 1f;
int   maxSt  = cfg?.stamina?.max ?? 100;
```