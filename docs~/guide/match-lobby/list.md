# 내 로비 목록

```csharp
Task<SupabaseResult<IReadOnlyList<MatchLobbySummary>>> Supabase.ListMatchLobbiesAsync()
```

내가 호스트거나 멤버인 진행 중 로비 목록을 가져옵니다. 실시간 알림이 아니므로 이 호출을 원하는 주기로 반복해 초대 수락·시작·취소를 감지합니다.

```csharp
var r = await Supabase.ListMatchLobbiesAsync();
if (!r.IsSuccess) return;

foreach (var lobby in r.Data)
{
    if (lobby.Status == MatchLobbyState.Started && !_connected.Contains(lobby.LobbyId))
        ConnectToGameServer(lobby.SessionId);
}
```

**반환**

`.Data`는 `MatchLobbySummary` 목록입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `LobbyId` | string | 다른 로비 API에 넘길 ID |
| `SessionId` | string | `LobbyId`와 같은 값. [매치 결과 신고](/guide/match-result/report)에 씁니다 |
| `GameCode` | string | 생성 시 지정한 게임 코드 |
| `HostAccountId` | string | 호스트 계정 ID |
| `Status` | `MatchLobbyState` | `Open`·`Started`·`Cancelled`·`Expired` |
| `CreatedAt` | DateTimeOffset | 생성 시각 |
| `StartedAt` | DateTimeOffset? | 시작 알림 시각. 아직이면 null |
| `Members` | `List<MatchLobbyMember>` | 참가자 목록 |

`MatchLobbyMember`:

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `AccountId` | string | 멤버 계정 ID |
| `Name` | string | 멤버 닉네임 |
| `Status` | `MatchLobbyMemberState` | `Invited`·`Accepted`·`Declined`·`Left` |
| `RoleTag` | string | [지정한 역할 태그](/guide/match-lobby/set-role). 미지정이면 null |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: info 응답하지 않고 방치된 초대
아무도 응답하지 않은 로비는 일정 시간 뒤 서버가 자동으로 `Expired`로 정리합니다. 게임이 따로 시간 제한을 관리할 필요는 없습니다.
:::
