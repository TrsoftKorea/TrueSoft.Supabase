# 변경 구독

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

`RemoteConfigListener<T>`를 돌려줍니다. 값이 바뀔 때마다 콜백을 부르는 리스너이며, 다 쓰면 `Dispose()`를 호출하세요.

로그인·로그아웃해도 리스너는 계속 동작합니다. 그때 값을 다시 받지만, 값이 같으면 콜백을 부르지 않습니다. `Dispose()`를 부르기 전까지는 `pollInterval` 주기로 서버에 요청이 나갑니다.

::: info 자동 갱신은 한 요청으로 묶입니다
리스너가 여럿이어도 갱신 시각이 된 키를 모아 요청 하나로 확인합니다. 남은 시간이 주기의 절반 이하인 키도 함께 확인하므로 `pollInterval`보다 일찍 갱신될 수 있고, 늦어지지는 않습니다.
:::
