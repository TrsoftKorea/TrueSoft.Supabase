# Listener

값이 바뀌는 순간 자동으로 콜백 함수를 호출하는 방식입니다.

```csharp
RemoteConfigListener<T> RemoteConfig<T>.CreateListener(Action<T> onChange, float pollInterval = 60f)
```

값이 변경될 때 콜백을 호출하는 리스너를 생성합니다.  
사용이 끝나면 반드시 `Dispose()`를 호출하세요.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `onChange` | 값이 바뀔 때 호출되는 콜백 |
| `pollInterval` | 자동 갱신 주기(초). `0`이면 폴링 없음 (기본값: `60`) |
