# Reader

요청 시 캐시 값을 즉시 반환합니다. 캐시가 오래됐으면 서버에서 새 값을 받아온 뒤 반환합니다.

```csharp
Func<Task<T>> RemoteConfig<T>.CreateReader(int maxStale = 0)
```

값을 읽는 함수를 반환합니다. 반환된 함수를 호출할 때마다 캐시된 값 또는 서버 최신 값을 반환합니다.

```csharp
var reader = RemoteConfig<GameplayConfig>.CreateReader();
var cfg = await reader();
if (cfg != null)
{
    maxStamina = cfg.maxStamina;
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `maxStale` | 유효로 간주할 최대 캐시 경과 시간(초). `0`이면 DB 설정값(없으면 300초)을 따름 (기본값: `0`) |

**반환**

호출할 때마다 캐시 값(오래됐으면 서버 최신 값)을 비동기로 반환하는 함수.
