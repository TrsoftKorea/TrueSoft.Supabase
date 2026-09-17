# 친구

닉네임으로 검색해 요청을 보내고, 상대가 수락하면 친구가 됩니다. 친구끼리는 [귓속말](/guide/chat/#direct)을 보내거나 [매치 로비](/guide/match-lobby/)에 초대할 수 있습니다.

## 요청 흐름 {#flow}

1. [닉네임 검색](/guide/friend/search)으로 상대를 찾습니다.
2. [요청 전송](/guide/friend/request-send). 상대가 이미 나에게 보낸 요청이 있으면 그 자리에서 바로 친구가 됩니다.
3. 상대는 [받은 요청 목록](/guide/friend/requests-list)에서 확인해 [수락·거절](/guide/friend/request-respond)합니다.

## 새 요청은 폴링으로 확인합니다 {#polling}

실시간 알림이 아닙니다. [받은 요청 목록](/guide/friend/requests-list)을 원하는 주기로 직접 불러 배지·알림을 표시하세요.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`SearchFriendAsync`](/guide/friend/search) | 닉네임으로 유저 검색 |
| [`SendFriendRequestAsync`](/guide/friend/request-send) | 친구 요청 전송 |
| [`GetFriendRequestsAsync`](/guide/friend/requests-list) | 대기 중인 요청 목록 |
| [`RespondFriendRequestAsync`](/guide/friend/request-respond) | 받은 요청 수락·거절 |
| [`CancelFriendRequestAsync`](/guide/friend/request-cancel) | 내가 보낸 요청 취소 |
| [`GetFriendsAsync`](/guide/friend/list) | 친구 목록 |
| [`RemoveFriendAsync`](/guide/friend/remove) | 친구 삭제 |

::: tip 닉네임은 정확히 일치해야 합니다
부분 검색은 지원하지 않습니다. 전체 유저를 훑을 수 있게 여는 걸 막기 위한 설계입니다.
:::
