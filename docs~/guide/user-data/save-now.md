# 즉시 저장 후 대기

```csharp
Task<SupabaseResult> PlayerSave.SaveNowAsync(int timeoutMs = 5000)
```

쿨다운을 무시하고 변경분을 즉시 전송한 뒤 완료까지 기다립니다. 씬 전환·로그아웃·앱 종료 등 데이터 유실이 치명적인 순간에 사용하세요. 완료를 기다리지 않아도 되면 [즉시 저장 요청](./request-save)을 씁니다.

```csharp
await PlayerSave.SaveNowAsync();   // 전송 완료까지 대기
```

보낼 변경분이 없으면 네트워크 요청 없이 `UserSaveNoChanges` 사유의 실패를 반환합니다. 오류가 아니라 보낼 것이 없었다는 뜻이므로, 저장 실패를 처리할 때 이 사유는 걸러내세요.

```csharp
var result = await PlayerSave.SaveNowAsync();
if (!result.IsSuccess && result.Reason != SupabaseReason.UserSaveNoChanges)
    ShowSaveError(result.ErrorCode);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `timeoutMs` | 전송 완료를 기다리는 최대 시간(밀리초). 초과 시 실패 (기본값: 5000) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveFlushFailed` | 전송 실패 또는 타임아웃 |
| `SupabaseReason.UserSaveNoChanges` | 변경분이 없어 전송을 건너뜀. 오류 아님 |
