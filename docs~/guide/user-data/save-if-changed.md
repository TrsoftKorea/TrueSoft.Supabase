# 변경분 저장

```csharp
Task<SupabaseResult> PlayerSave.SaveIfChangedAsync()
```

마지막 동기화 이후 변경된 필드만 즉시 PATCH합니다. 변경이 없으면 네트워크 요청 없이 성공을 반환합니다. 보통은 자동 저장이 처리하므로, 자동 저장 쿨다운을 기다리지 않고 특정 시점에 직접 저장하고 싶을 때만 호출합니다.

```csharp
await PlayerSave.SaveIfChangedAsync();
```
