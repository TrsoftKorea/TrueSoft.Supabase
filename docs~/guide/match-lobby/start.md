# 시작 알림

```csharp
Task<SupabaseResult> Supabase.StartMatchLobbyAsync(string lobbyId)
```

로비 시작을 알립니다. 호스트만 부를 수 있고, **초대받은 멤버가 아직 수락하지 않았어도 시작됩니다** — 언제 시작할지는 게임이 정합니다. [내 로비 목록](/guide/match-lobby/list)을 폴링 중인 멤버들이 상태 변화를 감지해 실제 접속을 시작합니다 — 접속 자체는 게임이 처리합니다.

```csharp
var r = await Supabase.StartMatchLobbyAsync(_currentLobbyId);
if (r.IsSuccess)
    ConnectToGameServer(_currentLobbyId);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | 대상 로비의 `LobbyId` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotOpen` | 로비가 없거나 호스트가 아니거나 이미 시작·취소·만료되었습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: warning 시작하면 되돌릴 수 없습니다
시작한 뒤에는 [로비 취소](/guide/match-lobby/cancel)가 `MatchLobbyNotOpen`으로 실패합니다. 시작된 로비를 닫으려면 호스트가 [로비 나가기](/guide/match-lobby/leave)를 부르세요.
:::

::: tip 인원이 찼는지는 게임이 판단합니다
서버는 수락 인원을 세지 않습니다. 시작 버튼을 열어 줄 조건은 [내 로비 목록](/guide/match-lobby/list)이 돌려주는 멤버 상태로 게임이 직접 정하세요.
:::
