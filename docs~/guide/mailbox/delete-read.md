# 읽은 우편 일괄 삭제

```csharp
Task<SupabaseResult<int>> Supabase.DeleteReadMailsAsync(string category = null)
```

읽음 처리됐고 미수령 보상이 없는 우편만 일괄로 숨깁니다.

```csharp
var result = await Supabase.DeleteReadMailsAsync();
if (result.IsSuccess)
{
    int deleted = result.Data;   // 삭제된 우편 개수
    RefreshMailbox();
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `category` | 삭제 대상 분류. `null`이면 전체 분류 (기본값: `null`) |

**반환**

`.Data`에 삭제된 우편 개수(`int`)가 담깁니다.

**에러 코드**

| ErrorCode | 설명 |
|--------|------|
| `SupabaseErrorCode.NotSignedIn` | 로그인 상태가 아닙니다 |
