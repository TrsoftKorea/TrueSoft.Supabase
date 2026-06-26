# 다른 플레이어 닉네임 조회

```csharp
Task<string> Supabase.TryGetPublicDisplayNameAsync(string userId, string defaultValue = "")
```

다른 플레이어의 닉네임을 조회합니다. 조회 실패 시 `defaultValue`를 반환합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |
| `defaultValue` | 조회 실패 시 반환할 기본값 (기본값: `""`) |

**반환**

조회한 플레이어의 닉네임 문자열. 조회 실패 시 `defaultValue`.
