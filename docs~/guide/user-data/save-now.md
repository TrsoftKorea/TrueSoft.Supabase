# 즉시 저장 후 대기

```csharp
Task<SupabaseSaveResult> Supabase.SaveNowAsync(int timeoutMs = 5000)
```

쿨다운을 무시하고 변경분을 즉시 전송한 뒤 완료까지 기다립니다. 완료를 기다리지 않아도 되면 [즉시 저장 요청](/guide/user-data/request-save)을 씁니다.

```csharp
await Supabase.SaveNowAsync();   // 전송 완료까지 대기
```

보낼 변경분이 없으면 네트워크 요청 없이 `UserSaveNoChanges` 사유의 실패를 반환합니다. 오류가 아니라 보낼 것이 없었다는 뜻이므로, 저장 뒤 다음 단계로 넘어갈지는 `IsSaved`로 판단합니다. 전송이 실패하면 그 사유를 그대로 돌려줍니다.

```csharp
var result = await Supabase.SaveNowAsync();
if (!result.IsSaved)
    ShowSaveError(result.ErrorCode);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `timeoutMs` | 전송 완료를 기다리는 최대 시간, 밀리초 단위. 초과 시 실패 (기본값: 5000) |

**반환**

`SupabaseSaveResult`를 돌려줍니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `IsSaved` | `bool` | 서버에 보낼 변경이 남지 않았는지. 전송에 성공했거나 보낼 변경이 없었으면 `true` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveNoChanges` | 변경분이 없어 전송을 건너뛰었습니다. 오류가 아닙니다 |
| `SupabaseReason.UserSaveNotReady` | 아직 [로드](/guide/user-data/load)하지 않았습니다. 로드 전 값이 서버 값을 덮지 않도록 보내지 않습니다 |
| `SupabaseReason.UserSaveTimeout` | `timeoutMs`를 넘었습니다. 전송은 계속 진행될 수 있습니다 |
| `SupabaseReason.UserSaveFlushFailed` | 전송에 실패했습니다. 원문은 `ErrorCode`에 있습니다 |
| `SupabaseReason.UserSaveSerializeFailed` | 세이브 값을 전송 형식으로 변환하지 못했습니다. 원문은 `ErrorCode`에 있습니다 |
| `SupabaseReason.DuplicateLogin` | 다른 기기에서 로그인해 전송이 막혔습니다 |
