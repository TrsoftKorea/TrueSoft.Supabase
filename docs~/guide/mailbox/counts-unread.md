# 미읽음 수

```csharp
Task<SupabaseResult<int>> Supabase.GetUnreadMailCountAsync(
    string userId   = null,
    string category = null)
```

읽지 않은 우편 개수를 조회합니다. 우편함 배지 표시에 사용합니다.

```csharp
var result = await Supabase.GetUnreadMailCountAsync();
if (result.IsSuccess)
{
    int count = result.Data;
    mailBadge.SetCount(count);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 계약 호환용. 무시됩니다 (기본값: `null`) |
| `category` | 조회할 분류. `null`이면 전체 분류 (기본값: `null`) |

**반환**

`.Data`에 미읽음 우편 개수(`int`)가 담깁니다. 지정한 분류에 활성 우편이 없으면 `0`입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
