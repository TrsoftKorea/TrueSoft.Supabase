# 매치 로비 API

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

::: tip
로비 `SessionId`는 [매치 결과 신고](/guide/match-result/report)의 `sessionId`로 그대로 쓸 수 있습니다.
:::
