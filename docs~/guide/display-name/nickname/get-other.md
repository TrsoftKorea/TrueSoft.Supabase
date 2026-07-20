# 다른 플레이어 닉네임 조회

```csharp
Task<SupabaseResult<string>> Supabase.GetPublicNameAsync(string userId)
```

다른 플레이어의 닉네임을 조회합니다.

```csharp
var result = await Supabase.GetPublicNameAsync(userId);
if (result.IsSuccess)
{
    var displayName = result.Data;   // 조회한 닉네임
    ShowPlayerName(displayName);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |

**반환**

`.Data` — 조회한 플레이어의 닉네임 문자열.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseErrorCode.NotSignedIn` | 로그인 상태가 아닙니다 |
