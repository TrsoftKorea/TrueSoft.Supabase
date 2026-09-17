# 추가 초대

```csharp
Task<SupabaseResult> Supabase.InviteToMatchLobbyAsync(string lobbyId, string accountId)
```

진행 중인 로비에 친구를 더 초대합니다. 호스트만 부를 수 있고, 대상은 내 친구여야 합니다.

```csharp
var r = await Supabase.InviteToMatchLobbyAsync(_currentLobbyId, anotherFriend.AccountId);
if (r.IsSuccess)
    AddPendingMemberRow(anotherFriend);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | [로비 생성](/guide/match-lobby/create)이 돌려준 `LobbyId` |
| `accountId` | 초대할 친구의 계정 ID |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotFound` | 해당 로비가 없거나 호스트가 아닙니다 |
| `SupabaseReason.MatchLobbyNotOpen` | 로비가 이미 시작됐거나 취소·만료되었습니다 |
| `SupabaseReason.MatchLobbyInviteNotFriend` | 초대 대상이 내 친구가 아닙니다 |
| `SupabaseReason.MatchLobbyFull` | 로비 정원이 가득 찼습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
