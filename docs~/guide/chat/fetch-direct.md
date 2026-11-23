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
| `Id` | `long` | 조회 커서. 다음 호출의 `afterId`로 씁니다 |
| `AccountId` | `string` | 보낸 사람 계정 ID |
| `UserId` | `string` | 보낸 사람의 플레이어 ID |
| `DisplayName` | `string` | 보낸 시점의 닉네임 |
| `Content` | `string` | 본문. `Deleted`가 true면 null |
| `Deleted` | `bool` | 운영자가 숨긴 메시지입니다 |
| `CreatedAt` | `DateTimeOffset` | 보낸 시각 |
| `ChannelCode` | `string` | 어느 채널의 메시지인지 |
| `IsNotice` | `bool` | 운영 콘솔에서 보낸 공지인지. 이 대화에서는 늘 `false` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendTargetRequired` | 대화 상대가 지정되지 않았습니다 |

**실사용 예시**

친구 목록에서 대화 버튼을 누르면 창을 열고, 창이 떠 있는 동안 5초마다 새 대화를 받습니다.

```csharp
public void OnClickTalk(FriendSummary friend)
{
    _friendId = friend.AccountId;
    _lastId = 0;   // 상대가 바뀌면 최근 대화부터 다시 받습니다
    directChatPanel.SetActive(true);
    InvokeRepeating(nameof(PollDirectChat), 0f, 5f);
}

async void PollDirectChat()
{
    var result = await Supabase.GetDirectChatAsync(_friendId, _lastId);
    if (!result.IsSuccess) return;   // 다음 주기에 다시 받습니다

    foreach (var m in result.Data) { AddBubble(m); _lastId = m.Id; }
}

public void OnClickCloseTalk() { CancelInvoke(nameof(PollDirectChat)); directChatPanel.SetActive(false); }
```

::: tip 친구가 아니어도 조회는 됩니다
이전에 친구였다가 삭제된 사이라도 지난 대화는 조회할 수 있습니다. 새로 보내려면 다시 친구여야 합니다.
:::
