# 로비 생성

```csharp
Task<SupabaseResult<MatchLobbyCreateOutcome>> Supabase.CreateMatchLobbyAsync(
    string              gameCode,
    IEnumerable<string> invitedAccountIds = null)
```

로비를 만들고 지정한 친구들을 초대합니다. 호스트는 자동으로 참가 확정됩니다. 초대 대상은 전부 내 친구여야 합니다.

```csharp
var r = await Supabase.CreateMatchLobbyAsync("arena_1v1", new[] { friend.AccountId });
if (!r.IsSuccess) return;

_currentLobbyId = r.Data.LobbyId;
ShowWaitingRoom();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `gameCode` | 매치 종류를 구분하는 코드 |
| `invitedAccountIds` | 초대할 친구들의 계정 ID. 생략하면 호스트만 있는 로비를 만들고, 나중에 [추가 초대](/guide/match-lobby/invite)할 수 있습니다(기본값: null) |

**반환**

`.Data`는 `MatchLobbyCreateOutcome`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `LobbyId` | string | 이후 로비 관련 호출에 넘길 ID |
| `SessionId` | string | `LobbyId`와 같은 값. [매치 결과 신고](/guide/match-result/report)의 `sessionId`로 그대로 쓸 수 있습니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchGameCodeEmpty` | 게임 코드가 비어 있습니다 |
| `SupabaseReason.MatchLobbyInviteNotFriend` | 초대 대상 중 내 친구가 아닌 계정이 있습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
