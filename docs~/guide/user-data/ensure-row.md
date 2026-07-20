# 세이브 행 보장

```csharp
Task<SupabaseResult> PlayerSave.EnsureRowAsync()
```

DB에 본인 세이브 행이 존재하도록 보장합니다. 행이 없으면 DB 기본값으로 생성하며, 로컬 데이터는 바꾸지 않습니다. 보통 [로드](./load)가 자동으로 행을 만드므로 직접 호출할 일은 드뭅니다.

```csharp
await PlayerSave.EnsureRowAsync();
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseErrorCode.UserSaveLoadFailed` | 행 생성·확인 실패 |
