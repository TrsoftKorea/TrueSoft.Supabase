# 초대 응답

```csharp
Task<SupabaseResult> Supabase.RespondMatchLobbyAsync(string lobbyId, bool accept)
```

받은 로비 초대에 응답합니다.

```csharp
var r = await Supabase.RespondMatchLobbyAsync(invite.LobbyId, accept: true);
if (r.IsSuccess)
    ShowWaitingRoom();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | [내 로비 목록](/guide/match-lobby/list)에서 받은 `LobbyId` |
| `accept` | true면 수락, false면 거절 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotOpen` | 로비가 이미 시작됐거나 취소·만료되었습니다 |
| `SupabaseReason.MatchLobbyInviteNotFound` | 응답할 초대가 없습니다 |
| `SupabaseReason.MatchLobbyFull` | 기다리는 사이 자리가 다 찼습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
