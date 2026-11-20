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

`.Data`는 조회한 플레이어의 닉네임 `string`입니다.

**실사용 예시**

길드원 목록처럼 게임이 플레이어 ID만 들고 있는 화면에서 줄마다 이름을 채웁니다.

```csharp
public async void FillMemberName(MemberRow row, string userId)
{
    row.SetName("...");
    var result = await Supabase.GetPublicNameAsync(userId);
    row.SetName(result.IsSuccess && !string.IsNullOrEmpty(result.Data) ? result.Data : "알 수 없는 플레이어");
}
```
