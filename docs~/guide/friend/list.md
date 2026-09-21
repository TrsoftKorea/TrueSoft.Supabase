# 친구 목록

```csharp
Task<SupabaseResult<IReadOnlyList<FriendSummary>>> Supabase.GetFriendsAsync()
```

내 친구 목록을 가져옵니다.

```csharp
var r = await Supabase.GetFriendsAsync();
if (r.IsSuccess)
    foreach (var f in r.Data)
        AddFriendRow(f.AccountId, f.Name);
```

**반환**

`.Data`는 `FriendSummary` 목록입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `AccountId` | string | 친구의 계정 ID. [귓속말](/guide/chat/send-direct)·[로비 초대](/guide/match-lobby/invite)에 씁니다 |
| `Name` | string | 친구 닉네임 |
| `Since` | DateTimeOffset | 친구가 된 시각 |
| `LastActivityAt` | DateTimeOffset? | 그 친구가 마지막으로 접속하거나 데이터를 저장한 시각. [갱신 시점](/guide/friend/#last-activity) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
