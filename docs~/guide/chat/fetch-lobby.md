# 로비 대화 조회

```csharp
Task<SupabaseResult<IReadOnlyList<ChatMessage>>> Supabase.GetLobbyChatAsync(
    string lobbyId,
    long   afterId = 0,
    int    limit   = 50)
```

[매치 로비](/guide/match-lobby/) 대화를 커서로 조회합니다. [구독](/guide/chat/subscribe) 대상이 아니라 대기방이 떠 있는 동안 직접 불러야 합니다.

```csharp
private long _lastChatId;

async Task PollLobbyChat()
{
    var r = await Supabase.GetLobbyChatAsync(_lobbyId, _lastChatId);
    if (!r.IsSuccess) return;

    foreach (var m in r.Data)
    {
        AddBubble(m);
        _lastChatId = m.Id;
    }
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | 대상 로비의 `LobbyId` |
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
| `SupabaseReason.MatchLobbyNotFound` | 로비가 지정되지 않았습니다 |
| `SupabaseReason.MatchLobbyNotOpen` | 로비가 취소·만료되었습니다 |
| `SupabaseReason.MatchLobbyMemberNotFound` | 아직 수락하지 않았거나 나간 방입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: warning 로비가 닫히면 조회도 막힙니다
귓속말과 달리 지난 대화를 나중에 볼 수 없습니다. 방이 취소·만료되면 그 대화는 더 이상 읽히지 않고, 하루 뒤 서버가 지웁니다. 남겨야 할 내용이면 게임이 따로 보관하세요.
:::
