# Binding

백그라운드에서 주기적으로 값을 자동 갱신하는 방식입니다.  
`Value`로 언제든 최신 값을 바로 읽을 수 있습니다.

```csharp
RemoteConfigBinding<T> RemoteConfig<T>.CreateBinding(float pollInterval = 60f)
```

백그라운드 자동 갱신 바인딩을 생성합니다. `.Value`로 캐시된 최신 값을 동기적으로 읽을 수 있습니다.  
사용이 끝나면 반드시 `Dispose()`를 호출하세요.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `pollInterval` | 자동 갱신 주기(초). `0`이면 폴링 없음 (기본값: `60`) |
