# 즉시 저장

씬 전환·결제 완료·앱 종료처럼 지금 당장 저장해야 할 때 사용합니다.

```csharp
Task<bool> Supabase.TrySaveAllAsync(int timeoutMs = 5000)
```

변경된 모든 세이브 데이터를 즉시 서버에 업로드합니다. 성공 시 `true`, 타임아웃 또는 실패 시 `false`를 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `timeoutMs` | 최대 대기 시간 ms (기본값: `5000`) | `int` |

::: info
`SupabaseRuntime`을 씬에 배치하면 `OnApplicationPause` / `OnApplicationQuit` 시 자동으로 플러시합니다.
:::
