# 우편 삭제

```csharp
Task<SupabaseResult> Supabase.DeleteMailAsync(string mailId)
```

우편 1건을 우편함에서 숨깁니다(소프트 삭제). 미수령 보상이 남아 있으면 서버가 거부합니다.

```csharp
var result = await Supabase.DeleteMailAsync(mailId);
if (result.IsSuccess)
{
    RemoveMailFromUI(mailId);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 삭제할 우편 UUID |

**에러 코드**

| ErrorCode | 설명 |
|--------|------|
| `cannot_delete_unclaimed` | 미수령 보상이 있어 삭제할 수 없습니다. 먼저 수령하세요 |
| `mail_not_found` | 본인 소유가 아니거나 존재하지 않는 우편입니다 |
| `SupabaseErrorCode.NotSignedIn` | 로그인 상태가 아닙니다 |
