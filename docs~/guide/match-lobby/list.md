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
| `LobbyId` | `string` | 다른 로비 API에 넘길 ID |
| `SessionId` | `string` | `LobbyId`와 같은 값. [매치 결과 신고](/guide/match-result/report)에 씁니다 |
| `GameCode` | `string` | 생성 시 지정한 게임 코드 |
| `Name` | `string` | 방 이름. 지정하지 않았으면 null |
| `Metadata` | `Dictionary<string, object>` | 생성 시 넘긴 자유 값. 넘기지 않았으면 빈 사전 |
| `MaxMembers` | `int` | 정원 |
| `HostAccountId` | `string` | 호스트 계정 ID |
| `Status` | `MatchLobbyState` | `Open`·`Started`. 취소·만료된 로비는 목록에서 빠집니다. 서버가 SDK보다 새 상태를 돌려주면 `Unknown` |
| `StatusRaw` | `string` | 서버가 보낸 상태 원문. `Status`이 `Unknown`일 때 로그에 남길 값 |
| `CreatedAt` | `DateTimeOffset` | 생성 시각 |
| `ExpiresAt` | `DateTimeOffset` | 아무도 시작하지 않으면 이 시각에 닫힙니다 |
| `StartedAt` | `DateTimeOffset?` | 시작 알림 시각. 아직이면 null |
| `Members` | `List<MatchLobbyMember>` | 참가자 목록 |

`MatchLobbyMember`:

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `AccountId` | `string` | 멤버 계정 ID |
| `Name` | `string` | 멤버 닉네임 |
| `Status` | `MatchLobbyMemberState` | `Invited`·`Accepted`·`Declined`·`Left`. 서버가 SDK보다 새 상태를 돌려주면 `Unknown` |
| `Metadata` | `Dictionary<string, object>` | [지정한 참가자 칸](/guide/match-lobby/set-member-meta). 지정한 적이 없으면 빈 사전 |

**실사용 예시**

로비 화면에서 몇 초마다 불러 새 초대를 띄우고, 들어가 있던 방이 시작되거나 닫힌 것을 알아챕니다.

```csharp
public async void PollLobbies()
{
    var result = await Supabase.ListMatchLobbiesAsync();
    if (!result.IsSuccess) return;   // 다음 주기에 다시 묻습니다

    var current = result.Data.FirstOrDefault(l => l.LobbyId == _currentLobbyId);
    if (_currentLobbyId != null && current == null) { ShowToast("방이 닫혔습니다"); CloseWaitingRoom(); }   // 취소·만료된 방은 목록에서 빠집니다
    else if (current?.Status == MatchLobbyState.Started) EnterMatch(current.SessionId);

    foreach (var lobby in result.Data)
    {
        var me = lobby.Members.Find(m => m.AccountId == Supabase.UserId);
        if (me?.Status == MatchLobbyMemberState.Invited) ShowInvitePopupOnce(lobby.LobbyId, lobby.Name);
    }
}
```

::: info 응답하지 않고 방치된 초대
아무도 시작하지 않은 로비는 `ExpiresAt`이 지나면 서버가 자동으로 `Expired`로 정리합니다. 그 시간은 운영 콘솔에서 정하므로 게임이 따로 시간 제한을 관리할 필요는 없습니다.
:::
