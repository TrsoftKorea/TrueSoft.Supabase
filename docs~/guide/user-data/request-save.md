# 즉시 저장 요청

```csharp
SupabaseResult PlayerSave.RequestSave()
```

쿨다운을 무시하고 즉시 전송을 요청하되 완료를 기다리지 않습니다(fire-and-forget). 완료를 확인해야 하면 [즉시 저장 후 대기](./save-now)를 씁니다.

```csharp
PlayerSave.RequestSave();   // 완료를 기다리지 않음
```

보낼 변경분이 없으면 요청 자체를 하지 않고 `UserSaveNoChanges` 사유의 실패를 반환합니다. 오류가 아니라 보낼 것이 없었다는 뜻입니다.

여러 번 호출해도 안전합니다. 이미 전송 중이면 완료 후 1회 재전송이 예약되므로 중복 전송이 쌓이지 않고, 전송 중에 바뀐 값도 그 재전송에 함께 실립니다. 매 프레임 호출해도 문제없습니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.UserSaveFlushFailed` | 세이브가 아직 초기화되지 않아 요청을 접수하지 못함 |
| `SupabaseReason.UserSaveNoChanges` | 변경분이 없어 요청하지 않음. 오류 아님 |
