# 변경분 저장

```csharp
Task<SupabaseResult> Supabase.SaveIfChangedAsync()
```

마지막 동기화 이후 변경된 필드만 즉시 PATCH합니다. 보통은 자동 저장이 처리하므로, 자동 저장 쿨다운을 기다리지 않고 특정 시점에 직접 저장하고 싶을 때만 호출합니다.

```csharp
await Supabase.SaveIfChangedAsync();
```

변경이 없으면 네트워크 요청 없이 `UserSaveNoChanges` 사유의 실패를 반환합니다. 오류가 아니라 보낼 것이 없었다는 뜻입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveNoChanges` | 변경분이 없어 전송을 건너뜀. 오류 아님 |
| `SupabaseReason.UserSaveNotReady` | 세이브 클래스가 아직 초기화되지 않음 |
| `SupabaseReason.UserSaveSerializeFailed` | 세이브 값을 전송 형식으로 변환하지 못함 |
| `SupabaseReason.UserSaveFlushFailed` | 서버 거부 또는 네트워크 오류 |
