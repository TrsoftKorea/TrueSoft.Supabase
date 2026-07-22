# 전체 즉시 저장

```csharp
Task<SupabaseResult> Supabase.SaveAllAsync(int timeoutMs = 5000)
```

변경된 모든 세이브 데이터를 즉시 서버에 업로드합니다. 특정 세이브만 다루려면 [즉시 저장 후 대기](./flush-now)·[즉시 저장 요청](./request-immediate-save)를 씁니다. 씬 전환·결제 완료·앱 종료처럼 지금 당장 저장해야 할 때 사용합니다.

```csharp
var result = await Supabase.SaveAllAsync();
if (result.IsSuccess)
{
    // 저장 완료
}
else if (result.Reason != SupabaseReason.UserSaveNoChanges)
{
    // 실패 처리
}
```

보낼 변경분이 없으면 전송하지 않고 `UserSaveNoChanges` 사유의 실패를 반환합니다. 오류가 아니라 보낼 것이 없었다는 뜻이므로 위 예제처럼 걸러냅니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `timeoutMs` | 최대 대기 시간 ms (기본값: `5000`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveFlushFailed` | 저장 실패 (네트워크 오류 또는 타임아웃) |
| `SupabaseReason.UserSaveNoChanges` | 변경분이 없어 전송을 건너뜀. 오류 아님 |

::: info
`SupabaseRuntime`을 씬에 배치하면 `OnApplicationPause` / `OnApplicationQuit` 시 자동으로 플러시합니다.
:::
