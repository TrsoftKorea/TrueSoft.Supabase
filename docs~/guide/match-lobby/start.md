# 시작 알림

```csharp
Task<SupabaseResult> Supabase.StartMatchLobbyAsync(string lobbyId)
```

로비 시작을 알립니다. 호스트만 부를 수 있습니다. [내 로비 목록](/guide/match-lobby/list)을 폴링 중인 멤버들이 상태 변화를 감지해 실제 접속을 시작합니다 — 접속 자체는 게임이 처리합니다.

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
