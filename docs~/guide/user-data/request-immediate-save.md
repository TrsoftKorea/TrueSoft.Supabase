# 즉시 저장 요청

```csharp
SupabaseResult PlayerSave.RequestImmediateSave()
```

쿨다운을 무시하고 즉시 전송을 요청하되 완료를 기다리지 않습니다(fire-and-forget). 이미 전송 중이면 완료 후 1회 재전송이 예약됩니다. 완료를 확인해야 하면 [즉시 저장 후 대기](./flush-now)를 씁니다.

```csharp
PlayerSave.RequestImmediateSave();   // 완료를 기다리지 않음
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveFlushFailed` | 전송 요청 실패 |
