# 친구 삭제

```csharp
Task<SupabaseResult> Supabase.RemoveFriendAsync(string friendAccountId)
```

친구 관계를 끊습니다. 이후 다시 요청을 보내면 처음부터 새로 시작합니다.

```csharp
var r = await Supabase.RemoveFriendAsync(friend.AccountId);
if (r.IsSuccess)
    RemoveFromFriendList(friend);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `friendAccountId` | [친구 목록](/guide/friend/list)의 `AccountId` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendNotFound` | 해당 계정과 친구 관계가 아닙니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
