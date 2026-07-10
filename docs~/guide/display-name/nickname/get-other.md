# 다른 플레이어 닉네임 조회

```csharp
Task<SupabaseResult<string>> Supabase.GetPublicDisplayNameAsync(string userId)
```

다른 플레이어의 닉네임을 조회합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |

**반환**

`.Data` — 조회한 플레이어의 닉네임 문자열.
