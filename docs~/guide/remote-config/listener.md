# Listener

```csharp
RemoteConfigListener<T> RemoteConfig<T>.CreateListener(Action<T> onChange, float pollInterval = 60f)
```

값이 바뀌는 순간 자동으로 콜백을 호출하는 리스너를 생성합니다. 사용이 끝나면 반드시 `Dispose()`를 호출하세요.

```csharp
var listener = RemoteConfig<GameplayConfig>.CreateListener(cfg =>
{
    maxStamina = cfg.maxStamina;   // 값이 바뀔 때마다 호출됨
});
// ...
listener.Dispose();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `onChange` | 값이 바뀔 때 호출되는 콜백 |
| `pollInterval` | 자동 갱신 주기, 초 단위. `0`이면 폴링 없음 (기본값: `60`) |

**반환**

값이 바뀔 때마다 콜백을 호출하는 리스너 객체. 사용 후 `Dispose()`를 호출하세요.
