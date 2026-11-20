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

**실사용 예시**

초대 팝업의 수락·거절 버튼에서 부르고, 수락했으면 대기실로 넘어갑니다.

```csharp
public async void OnClickInviteAnswer(string lobbyId, bool accept)
{
    CloseInvitePopup();
    var result = await Supabase.RespondMatchLobbyAsync(lobbyId, accept);
    if (result.IsSuccess)
    {
        if (accept) { _currentLobbyId = lobbyId; OpenWaitingRoom(); }
        return;
    }

    switch (result.Reason)
    {
        case SupabaseReason.MatchLobbyFull:    ShowToast("자리가 다 찼습니다"); break;
        case SupabaseReason.MatchLobbyNotOpen: ShowToast("이미 시작했거나 닫힌 방입니다"); break;
        default:                               ShowToast("응답하지 못했습니다"); break;
    }
}
```
