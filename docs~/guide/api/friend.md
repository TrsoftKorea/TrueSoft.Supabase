# 친구 API

| 메서드 | 설명 |
|--------|------|
| [`SearchFriendAsync`](/guide/friend/search) | 닉네임으로 유저 검색 |
| [`SendFriendRequestAsync`](/guide/friend/request-send) | 친구 요청 전송 |
| [`GetFriendRequestsAsync`](/guide/friend/requests-list) | 대기 중인 요청 목록 |
| [`RespondFriendRequestAsync`](/guide/friend/request-respond) | 받은 요청 수락·거절 |
| [`CancelFriendRequestAsync`](/guide/friend/request-cancel) | 내가 보낸 요청 취소 |
| [`GetFriendsAsync`](/guide/friend/list) | 친구 목록 |
| [`RemoveFriendAsync`](/guide/friend/remove) | 친구 삭제 |
| [`GetFriendLimitsAsync`](/guide/friend/limits) | 친구 수 상한 등 제한값 읽기 |

::: tip
새 요청은 실시간 알림이 아니라 폴링 전제입니다. [자세히](/guide/friend/#polling)
:::
