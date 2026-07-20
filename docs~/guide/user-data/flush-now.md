# 즉시 저장 후 대기

```csharp
Task<SupabaseResult> PlayerSave.FlushNowAsync(int timeoutMs = 5000)
```

쿨다운을 무시하고 변경분을 즉시 전송한 뒤 완료까지 기다립니다. 씬 전환·로그아웃·앱 종료 등 데이터 유실이 치명적인 순간에 사용하세요. 완료를 기다리지 않아도 되면 [즉시 저장 요청](./request-immediate-save)을 씁니다.

```csharp
await PlayerSave.FlushNowAsync();   // 전송 완료까지 대기
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `timeoutMs` | 전송 완료를 기다리는 최대 시간(밀리초). 초과 시 실패 (기본값: 5000) |

**에러 코드**

| ErrorCode | 설명 |
|--------|------|
| `SupabaseErrorCode.UserSaveFlushFailed` | 전송 실패 또는 타임아웃 |
