# 즉시 저장 요청

```csharp
SupabaseResult PlayerSave.RequestImmediateSave()
```

쿨다운을 무시하고 즉시 전송을 요청하되 완료를 기다리지 않습니다(fire-and-forget). 이미 전송 중이면 완료 후 1회 재전송이 예약됩니다. 완료를 확인해야 하면 [즉시 저장 후 대기](./flush-now)를 씁니다.

```csharp
PlayerSave.RequestImmediateSave();   // 완료를 기다리지 않음
```

보낼 변경분이 없으면 요청 자체를 하지 않고 `UserSaveNoChanges` 사유의 실패를 반환합니다. 오류가 아니라 보낼 것이 없었다는 뜻입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveFlushFailed` | 전송 요청 실패 |
| `SupabaseReason.UserSaveNoChanges` | 변경분이 없어 요청하지 않음. 오류 아님 |
