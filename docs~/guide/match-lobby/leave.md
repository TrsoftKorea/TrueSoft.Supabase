# 로비 나가기

```csharp
Task<SupabaseResult> Supabase.LeaveMatchLobbyAsync(string lobbyId)
```

로비를 나갑니다. 호스트가 나가면 로비 전체가 취소됩니다.

```csharp
var r = await Supabase.LeaveMatchLobbyAsync(_currentLobbyId);
if (r.IsSuccess)
    CloseWaitingRoom();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | [로비 생성](/guide/match-lobby/create) 또는 [내 로비 목록](/guide/match-lobby/list)에서 받은 `LobbyId` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotFound` | 해당 로비가 없습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: warning 호스트가 나가면 전체가 취소됩니다
다른 멤버가 있어도 호스트가 나가는 순간 로비 상태가 `Cancelled`로 바뀝니다. 다음 [내 로비 목록](/guide/match-lobby/list) 조회에서 나머지 멤버가 이를 확인합니다.
:::
