# 매치 로비

[친구](/guide/friend/)를 초대해 같은 대기방에 모으는 기능입니다. 팀인지 상대인지 그냥 같은 방인지는 게임마다 다르므로, SDK는 "누가 로비에 있고 어떤 상태인지"만 관리하고 역할 해석과 실제 접속은 게임이 합니다.

## 흐름 {#flow}

1. 호스트가 [로비 생성](/guide/match-lobby/create)과 동시에 친구를 초대합니다.
2. 초대받은 멤버는 [응답](/guide/match-lobby/respond)으로 수락·거절합니다. 진행 중에도 호스트가 [추가 초대](/guide/match-lobby/invite)할 수 있습니다.
3. 필요하면 호스트가 멤버마다 [역할 태그](/guide/match-lobby/set-role)를 지정합니다 — 팀 이름이든 진영이든 의미는 게임이 정합니다.
4. 호스트가 [시작](/guide/match-lobby/start)을 알리면, 폴링 중이던 멤버들이 상태 변화를 감지해 실제 게임에 접속합니다.

## 기존 매치 결과 신고와 연결됩니다 {#session-id}

[로비 생성](/guide/match-lobby/create)이 돌려주는 `SessionId`를 [매치 결과 신고](/guide/match-result/report)의 `sessionId`로 그대로 쓸 수 있습니다. 로비로 모은 뒤 게임이 붙여준 대로 플레이하고, 끝나면 참가자마다 결과를 신고하면 됩니다.

## 상태 변화는 폴링으로 확인합니다 {#polling}

실시간 알림이 아닙니다. [내 로비 목록](/guide/match-lobby/list)을 원하는 주기로 불러 초대 수락·시작·취소를 감지하세요.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`CreateMatchLobbyAsync`](/guide/match-lobby/create) | 로비 생성 + 초대 |
| [`InviteToMatchLobbyAsync`](/guide/match-lobby/invite) | 진행 중인 로비에 추가 초대 |
| [`RespondMatchLobbyAsync`](/guide/match-lobby/respond) | 초대 수락·거절 |
| [`LeaveMatchLobbyAsync`](/guide/match-lobby/leave) | 로비 나가기 |
| [`SetMatchLobbyMemberRoleAsync`](/guide/match-lobby/set-role) | 멤버 역할 태그 지정 |
| [`StartMatchLobbyAsync`](/guide/match-lobby/start) | 시작 알림 |
| [`CancelMatchLobbyAsync`](/guide/match-lobby/cancel) | 로비 취소 |
| [`ListMatchLobbiesAsync`](/guide/match-lobby/list) | 내 로비 목록 조회 |

::: warning 실제 접속은 게임이 처리합니다
SDK는 초대·수락·상태 변화만 다룹니다. 시작 신호를 받은 뒤 참가자를 실제 게임 서버에 붙이는 것은 게임의 몫입니다.
:::
