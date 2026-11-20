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

**실사용 예시**

대기실의 나가기 버튼에서 부르고, 호스트라면 방이 닫힌다고 먼저 확인받습니다.

```csharp
public async void OnClickLeaveRoom()
{
    if (IsHost && !await ShowConfirmPopup("방장이 나가면 방이 닫힙니다. 나갈까요?")) return;

    var result = await Supabase.LeaveMatchLobbyAsync(_currentLobbyId);
    if (!result.IsSuccess && result.Reason != SupabaseReason.MatchLobbyNotFound)
    {
        ShowToast("방을 나가지 못했습니다");
        return;
    }
    _currentLobbyId = null;   // 이미 없어진 방이어도 화면은 닫습니다
    CloseWaitingRoom();
}
```

::: warning 호스트가 나가면 전체가 취소됩니다
다른 멤버가 있어도 호스트가 나가는 순간 로비가 취소됩니다. 나머지 멤버의 다음 [내 로비 목록](/guide/match-lobby/list) 조회에서 그 로비가 빠집니다. **이미 시작한 로비도 이렇게 닫습니다** — [로비 취소](/guide/match-lobby/cancel)는 시작 전에만 듣습니다.
:::
