# 로비 취소

```csharp
Task<SupabaseResult> Supabase.CancelMatchLobbyAsync(string lobbyId)
```

로비를 취소합니다. 호스트만 부를 수 있고, **아직 시작하지 않은 로비에서만** 동작합니다.

```csharp
var r = await Supabase.CancelMatchLobbyAsync(_currentLobbyId);
if (r.IsSuccess)
    CloseWaitingRoom();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | 대상 로비의 `LobbyId` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotOpen` | 로비가 없거나 호스트가 아니거나 이미 시작·취소·만료되었습니다 |

**실사용 예시**

대기실의 방 닫기 버튼에서 부르고, 그사이 시작된 방이면 나가기로 닫습니다.

```csharp
public async void OnClickCloseRoom()
{
    var result = await Supabase.CancelMatchLobbyAsync(_currentLobbyId);
    if (result.IsSuccess) { CloseWaitingRoom(); return; }

    if (result.Reason == SupabaseReason.MatchLobbyNotOpen)
    {
        await Supabase.LeaveMatchLobbyAsync(_currentLobbyId);   // 시작한 방은 호스트가 나가야 닫힙니다
        CloseWaitingRoom();
        return;
    }
    ShowToast("방을 닫지 못했습니다");
}
```

::: tip 시작한 뒤에는 나가기로 닫습니다
[시작](/guide/match-lobby/start)을 알린 로비는 이 API로 취소되지 않습니다. 호스트가 [로비 나가기](/guide/match-lobby/leave)를 부르면 그때 닫힙니다.
:::
