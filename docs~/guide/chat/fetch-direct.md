# 귓속말 조회

```csharp
Task<SupabaseResult<IReadOnlyList<ChatMessage>>> Supabase.GetDirectChatAsync(
    string targetAccountId,
    long   afterId = 0,
    int    limit   = 50)
```

특정 친구와의 대화를 커서로 조회합니다. 귓속말은 [구독](/guide/chat/subscribe) 대상이 아니라 창을 열 때·주기적으로 직접 불러야 합니다.

```csharp
private long _lastId;

async Task OpenDirectChatAsync(string friendAccountId)
{
    var r = await Supabase.GetDirectChatAsync(friendAccountId, _lastId);
    if (!r.IsSuccess) return;

    foreach (var m in r.Data)
    {
        AddBubble(m);
        _lastId = m.Id;
    }
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `targetAccountId` | 대화 상대 친구의 계정 ID |
| `afterId` | 이 커서 이후 메시지만. 0 이하면 최근 `limit`개(기본값: 0) |
| `limit` | 한 번에 받을 최대 개수(기본값: 50) |

**반환**

`.Data`는 `ChatMessage` 목록입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Id` | long | 조회 커서. 다음 호출의 `afterId`로 씁니다 |
| `AccountId` | string | 보낸 사람 계정 ID |
| `DisplayName` | string | 보낸 시점의 닉네임 |
| `Content` | string | 본문. `Deleted`가 true면 null |
| `Deleted` | bool | 운영자가 숨긴 메시지입니다 |
| `CreatedAt` | DateTimeOffset | 보낸 시각 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendTargetRequired` | 대화 상대가 지정되지 않았습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip 친구가 아니어도 조회는 됩니다
이전에 친구였다가 삭제된 사이라도 지난 대화는 조회할 수 있습니다. 새로 보내려면 다시 친구여야 합니다.
:::
