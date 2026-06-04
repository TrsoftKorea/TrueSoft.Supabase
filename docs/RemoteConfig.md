# Remote Config

---

## 기본 사용법

```csharp
// 비동기 — 캐시 없으면 서버 조회, 있으면 캐시 반환 후 백그라운드 갱신
var (ok, config) = await Supabase.TryGetRemoteConfigAsync<GameConfig>("gameplay_v1");

// 동기 — 메모리 캐시만 읽음 (서버 요청 없음)
var config = Supabase.GetRemoteConfig<GameConfig>("gameplay_v1");
```

## Cold Start 패턴

앱 시작 시 RemoteConfig를 자동으로 가져오지 않습니다.  
`GetRemoteConfigAsync`를 호출하는 순간 해당 키만 서버에서 조회합니다.  
이후에는 캐시에서 읽고, `max_stale_seconds`가 지나면 백그라운드에서 갱신합니다.

## 설정 키 구조 권장

관련 설정은 하나의 키에 JSON 객체로 묶어 관리합니다.

```csharp
[Serializable]
public class GameplayConfig
{
    public StaminaConfig stamina;
    public BattleConfig battle;
}

var (ok, cfg) = await Supabase.TryGetRemoteConfigAsync<GameplayConfig>("gameplay_v1");
if (ok) ApplyConfig(cfg);
```

Retool DB 예시:
```
key: "gameplay_v1"
value_json: {"stamina":{"maxEnergy":100,"regenSeconds":300},"battle":{"playerDmg":1.5}}
poll_interval_seconds: 60
max_stale_seconds: 300
```

## 폴링

DB `poll_interval_seconds`가 0보다 큰 키는 `SupabaseRuntime`이 `Update`에서 만기 시 자동으로 폴링합니다.  
수동 갱신:

```csharp
await Supabase.TryRefreshRemoteConfigAsync();          // 전체 갱신
await Supabase.RefreshRemoteConfigOnDemandAsync();     // 즉시 갱신 + 다음 폴링 pushback
```

## 값 변경 구독

```csharp
Supabase.SubscribeRemoteConfig("gameplay_v1", json => {
    var cfg = JsonUtility.FromJson<GameplayConfig>(json);
    ApplyConfig(cfg);
}, invokeIfCached: true);
```

## 테이블 이름 변경

기본값은 `remote_config`입니다. `SupabaseSettings.remoteConfigTable`에서 변경할 수 있습니다.  
SQL: `Sql/player/08_remote_config.sql`
