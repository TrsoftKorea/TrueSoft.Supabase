# 즉시 저장

```csharp
Task<SupabaseCallResult> Supabase.TrySaveAllAsync(int timeoutMs = 5000)
```

변경된 모든 세이브 데이터를 즉시 서버에 업로드합니다. 씬 전환·결제 완료·앱 종료처럼 지금 당장 저장해야 할 때 사용합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `timeoutMs` | 최대 대기 시간 ms (기본값: `5000`) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserSaveFlushFailed` | 저장 실패 (네트워크 오류 또는 타임아웃) |

::: info
`SupabaseRuntime`을 씬에 배치하면 `OnApplicationPause` / `OnApplicationQuit` 시 자동으로 플러시합니다.
:::
