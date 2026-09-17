# 로비 취소

```csharp
Task<SupabaseResult> Supabase.CancelMatchLobbyAsync(string lobbyId)
```

로비를 취소합니다. 호스트만 부를 수 있습니다.

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
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
