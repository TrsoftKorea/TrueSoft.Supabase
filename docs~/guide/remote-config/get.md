# 값 읽기

```csharp
Task<SupabaseResult<T>> RemoteConfig<T>.GetAsync(int maxStale = 0)
```

값을 한 번 읽습니다. 캐시가 신선하면 즉시, 오래됐으면 서버에서 새 값을 받은 뒤 반환합니다.

```csharp
var cfg = await RemoteConfig<GameplayConfig>.GetAsync();
if (!cfg.IsSuccess) return;

maxStamina = cfg.Data.maxStamina;
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `maxStale` | 유효로 간주할 최대 캐시 경과 초. `0`이면 DB 설정값을 따르고, DB에도 없으면 300초입니다 (기본값: `0`) |

**반환**

`.Data`에 역직렬화된 설정 객체가 담깁니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotInitialized` | SDK가 초기화되지 않았습니다 |
| `SupabaseReason.NetworkError` | 서버에 닿지 못했습니다 |
