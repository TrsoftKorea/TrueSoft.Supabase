# 매치 로비

사람을 초대해 같은 대기방에 모으는 기능입니다. 초대는 [친구](/guide/friend/)가 아니어도 보낼 수 있습니다. 팀인지 상대인지 그냥 같은 방인지는 게임마다 다르므로, SDK는 "누가 로비에 있고 어떤 상태인지"만 관리하고 방 이름·참가자 칸의 해석과 실제 접속은 게임이 합니다.

## 흐름 {#flow}

1. 호스트가 [로비 생성](/guide/match-lobby/create)과 동시에 초대를 보냅니다. 이때 방 이름·정원·게임이 정하는 값을 함께 넘길 수 있습니다.
2. 초대받은 멤버는 [응답](/guide/match-lobby/respond)으로 수락·거절합니다. 진행 중에도 호스트가 [추가 초대](/guide/match-lobby/invite)할 수 있습니다.
3. 필요하면 [참가자 칸](/guide/match-lobby/set-member-meta)을 채웁니다 — 팀이든 진영이든 준비 상태든 의미는 게임이 정합니다.
4. 호스트가 [시작](/guide/match-lobby/start)을 알리면, 폴링 중이던 멤버들이 상태 변화를 감지해 실제 게임에 접속합니다.

## 한 화면에 엮기 {#quick-start}

```csharp
private string _lobbyId;

// 호스트 — 친구를 골라 방을 만들면서 한 번에 초대한다.
async Task OnCreateRoom(IReadOnlyList<FriendSummary> picked)
{
    var made = await Supabase.CreateMatchLobbyAsync(
        "raid", picked.Select(f => f.AccountId), name: _roomName, maxMembers: 4);
    if (!made.IsSuccess) { ShowToast("방을 만들지 못했습니다."); return; }

    _lobbyId = made.Data.LobbyId;   // 결과 신고 때 쓸 SessionId 도 같은 값이다.
}

// 초대받은 쪽 — 수락하면 그때부터 아래 폴링이 시작 신호를 기다린다.
async Task OnAcceptInvite(MatchLobbySummary lobby)
{
    if (await Supabase.RespondMatchLobbyAsync(lobby.LobbyId, accept: true))
        _lobbyId = lobby.LobbyId;
}

// 모두 — 5초쯤마다 부른다. 호스트는 인원을, 멤버는 시작 신호를 여기서 본다.
async Task PollLobby()
{
    var mine = await Supabase.ListMatchLobbiesAsync();
    if (!mine.IsSuccess) return;

    var lobby = mine.Data.FirstOrDefault(l => l.LobbyId == _lobbyId);
    if (lobby == null) return;

    switch (lobby.Status)
    {
        case MatchLobbyState.Open:
            ShowMembers(lobby.Members.Where(m => m.Status == MatchLobbyMemberState.Accepted));
            break;
        case MatchLobbyState.Started:
            EnterGame(lobby.SessionId, lobby.Members);   // 실제 접속은 게임이 한다.
            break;
        case MatchLobbyState.Cancelled:
        case MatchLobbyState.Expired:
            ShowToast("방이 닫혔습니다.");
            _lobbyId = null;
            break;
    }
}

// 호스트 — 인원이 찼으면 시작을 알린다. 멤버들은 위 폴링에서 Started 를 보게 된다.
async Task OnStart()
{
    if (!await Supabase.StartMatchLobbyAsync(_lobbyId)) ShowToast("시작하지 못했습니다.");
}
```

팀을 나눠야 하면 시작 전에 [참가자 칸](/guide/match-lobby/set-member-meta)을 채우고, 멤버는 `Metadata`를 읽어 해석하면 됩니다.

## 기존 매치 결과 신고와 연결됩니다 {#session-id}

[로비 생성](/guide/match-lobby/create)이 돌려주는 `SessionId`를 [매치 결과 신고](/guide/match-result/report)의 `sessionId`로 그대로 쓸 수 있습니다. 로비로 모은 뒤 게임이 붙여준 대로 플레이하고, 끝나면 참가자마다 결과를 신고하면 됩니다.

## 로비 안에서 대화할 수 있습니다 {#chat}

참가자끼리만 보이는 대화가 따로 있습니다 — [보내기](/guide/chat/send-lobby)·[조회](/guide/chat/fetch-lobby). 초대가 친구로 제한되지 않아 친구가 아닌 사람이 한 방에 모이므로, 귓속말로는 안 되는 자리를 이 대화가 채웁니다.

## 방 이름과 자유 칸 {#metadata}

방마다 이름을 붙일 수 있고, 게임마다 다른 값은 `metadata`에 담습니다 — 맵·모드·규칙처럼 방 전체에 붙는 것은 [로비 생성](/guide/match-lobby/create) 때, 팀·진영·준비 상태처럼 사람마다 다른 것은 [참가자 칸](/guide/match-lobby/set-member-meta)에 넣습니다. 서버는 어느 쪽도 해석하지 않고 그대로 보관했다가 [목록](/guide/match-lobby/list)에서 돌려줍니다.

## 상태 변화는 폴링으로 확인합니다 {#polling}

실시간 알림이 아닙니다. [내 로비 목록](/guide/match-lobby/list)을 원하는 주기로 불러 초대 수락·시작·취소를 감지하세요.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`CreateMatchLobbyAsync`](/guide/match-lobby/create) | 로비 생성 + 초대 |
| [`InviteToMatchLobbyAsync`](/guide/match-lobby/invite) | 진행 중인 로비에 추가 초대 |
| [`RespondMatchLobbyAsync`](/guide/match-lobby/respond) | 초대 수락·거절 |
| [`LeaveMatchLobbyAsync`](/guide/match-lobby/leave) | 로비 나가기 |
| [`SetMatchLobbyMemberMetaAsync`](/guide/match-lobby/set-member-meta) | 참가자 칸 지정 |
| [`StartMatchLobbyAsync`](/guide/match-lobby/start) | 시작 알림 |
| [`CancelMatchLobbyAsync`](/guide/match-lobby/cancel) | 로비 취소 |
| [`ListMatchLobbiesAsync`](/guide/match-lobby/list) | 내 로비 목록 조회 |

::: warning 실제 접속은 게임이 처리합니다
SDK는 초대·수락·상태 변화만 다룹니다. 시작 신호를 받은 뒤 참가자를 실제 게임 서버에 붙이는 것은 게임의 몫입니다.
:::

::: info 돌려보고 싶다면
[SampleMatchLobby](/guide/samples/examples#samplematchlobby)를 씬에 붙이면 키보드로 생성·초대·시작을 바로 시험할 수 있습니다.
:::
