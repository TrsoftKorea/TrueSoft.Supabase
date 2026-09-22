# 로비 생성

```csharp
Task<SupabaseResult<MatchLobbyCreateOutcome>> Supabase.CreateMatchLobbyAsync(
    string                              gameCode,
    IEnumerable<string>                 invitedAccountIds = null,
    string                              name              = null,
    int?                                maxMembers        = null,
    IReadOnlyDictionary<string, object> metadata          = null)
```

로비를 만들고 지정한 계정들을 초대합니다. 호스트는 자동으로 참가 확정됩니다. **친구가 아니어도 초대할 수 있습니다** — [닉네임 검색](/guide/friend/search)으로 찾은 계정을 그대로 부르면 됩니다.

```csharp
var r = await Supabase.CreateMatchLobbyAsync(
    "arena_1v1",
    new[] { friend.AccountId },
    name:       "같이 하실 분",
    maxMembers: 4,
    metadata:   new Dictionary<string, object> { ["map"] = "desert", ["mode"] = "ranked" });
if (!r.IsSuccess) return;

_currentLobbyId = r.Data.LobbyId;
ShowWaitingRoom();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `gameCode` | 매치 종류를 구분하는 코드 |
| `invitedAccountIds` | 초대할 계정 ID. 생략하면 호스트만 있는 로비를 만들고, 나중에 [추가 초대](/guide/match-lobby/invite)할 수 있습니다(기본값: null) |
| `name` | 방 이름. 최대 40자, 생략하면 이름 없는 방(기본값: null) |
| `maxMembers` | 정원. 2~64, 생략하면 8명(기본값: null) |
| `metadata` | 맵·규칙·모드처럼 게임이 정하는 값. 서버는 해석하지 않습니다(기본값: null) |

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
| `SupabaseReason.MatchLobbyNameTooLong` | 방 이름이 40자를 넘었습니다 |
| `SupabaseReason.MatchLobbyMaxMembersInvalid` | 정원이 2~64를 벗어났습니다 |
| `SupabaseReason.MatchLobbyFull` | 초대 인원이 정원을 넘습니다. 호스트도 한 자리를 씁니다 |
| `SupabaseReason.MatchLobbyInviteLimitReached` | 초대 대상 중 대기 초대가 상한에 찬 계정이 있습니다 |
| `SupabaseReason.MatchLobbyInviteTooFast` | 연속 초대 최소 간격을 지키지 않았습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: info 방이 저절로 닫히기까지의 시간은 운영이 정합니다
아무도 시작하지 않은 방은 일정 시간이 지나면 자동으로 닫힙니다. 그 시간은 게임이 넘기는 값이 아니라 운영 콘솔의 매치 로비 설정에서 정하며, 기본값은 10분입니다. 남은 시간은 목록의 `ExpiresAt`으로 읽을 수 있습니다.
:::

::: tip metadata는 컬럼을 늘리는 대신 쓰는 칸입니다
맵·모드·규칙처럼 게임마다 다른 값은 여기에 넣습니다. 서버는 값을 들여다보지 않고 그대로 보관했다가 [목록](/guide/match-lobby/list)에서 돌려주므로, 넣은 쪽이 정한 의미 그대로 읽으면 됩니다.
:::
