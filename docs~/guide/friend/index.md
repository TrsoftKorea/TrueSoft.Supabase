# 친구

닉네임으로 검색해 요청을 보내고, 상대가 수락하면 친구가 됩니다. 친구끼리는 [귓속말](/guide/chat/#direct)을 보내거나 [매치 로비](/guide/match-lobby/)에 초대할 수 있습니다.

## 요청 흐름 {#flow}

1. [닉네임 검색](/guide/friend/search)으로 상대를 찾습니다.
2. [요청 전송](/guide/friend/request-send). 상대가 이미 나에게 보낸 요청이 있으면 그 자리에서 바로 친구가 됩니다.
3. 상대는 [받은 요청 목록](/guide/friend/requests-list)에서 확인해 [수락·거절](/guide/friend/request-respond)합니다.

## 한 화면에 엮기 {#quick-start}

```csharp
// 1. 검색 — 이미 친구이거나 요청이 오간 상대는 Relation 으로 걸러 버튼을 바꾼다.
async Task OnSearch(string nickname)
{
    var found = await Supabase.SearchFriendAsync(nickname);
    if (!found.IsSuccess) { ShowToast("그런 닉네임이 없습니다."); return; }

    ShowSearchResult(found.Data.Name, found.Data.Relation);
}

// 2. 요청 — 서로 보낸 상태였다면 그 자리에서 친구가 된다.
async Task OnSendRequest(FriendSearchResult target)
{
    var sent = await Supabase.SendFriendRequestAsync(target.AccountId);
    if (!sent.IsSuccess) { ShowToast(FriendFailMessage(sent.Reason)); return; }

    ShowToast(sent.Data.Accepted ? "친구가 되었습니다." : "요청을 보냈습니다.");
}

// 3. 폴링 — 받은 요청 개수를 배지로 띄운다. 30초쯤이면 충분하다.
async Task PollIncoming()
{
    var incoming = await Supabase.GetFriendRequestsAsync();
    if (incoming.IsSuccess) SetBadge(incoming.Data.Count);
}

// 4. 수락 — 성공하면 친구 목록을 다시 불러 화면을 맞춘다.
async Task OnAccept(FriendRequestSummary request)
{
    if (!await Supabase.RespondFriendRequestAsync(request.RequestId, accept: true)) return;

    var friends = await Supabase.GetFriendsAsync();
    if (friends.IsSuccess) ShowFriendList(friends.Data);
}
```

`FriendFailMessage`는 게임이 만드는 함수입니다. 아래 제한 사유와 [전체 카탈로그](/guide/api/fail-reasons)를 보고 안내 문구를 붙이세요.

## 새 요청은 폴링으로 확인합니다 {#polling}

실시간 알림이 아닙니다. [받은 요청 목록](/guide/friend/requests-list)을 원하는 주기로 직접 불러 배지·알림을 표시하세요.

## 제한값은 운영이 정합니다 {#limits}

친구 수 상한, 보낸 뒤 응답을 기다리는 요청의 개수 상한, 연속 요청 최소 간격 세 가지를 서버가 검사합니다. 기본값은 각각 100명·50건·3초이며 운영 콘솔에서 바꿉니다. 아래 네 가지로 실패하므로 안내 문구를 붙여 두세요.

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendLimitReached` | 내 친구 수가 상한에 도달했습니다 |
| `SupabaseReason.FriendTargetLimitReached` | 상대의 친구 수가 상한에 도달했습니다 |
| `SupabaseReason.FriendPendingLimitReached` | 보낸 뒤 응답을 기다리는 요청이 상한에 도달했습니다 |
| `SupabaseReason.FriendRequestTooFast` | 연속 요청 최소 간격을 지키지 않았습니다 |

::: warning 대기 상한은 취소해야 풀립니다
상대가 응답하지 않으면 그 요청은 계속 자리를 차지합니다. 서버가 30일 뒤 정리하지만 그전까지는 상한이 그대로 차 있으므로, [보낸 요청 목록](/guide/friend/requests-list)과 [요청 취소](/guide/friend/request-cancel)를 함께 넣어 플레이어가 직접 비울 수 있게 하세요.
:::

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
| [`GetFriendLimitsAsync`](/guide/friend/limits) | 제한값 읽기 |

::: tip 닉네임은 정확히 일치해야 합니다
부분 검색은 지원하지 않습니다. 전체 유저를 훑을 수 있게 여는 걸 막기 위한 설계입니다.
:::

::: info 돌려보고 싶다면
[SampleFriend](/guide/samples/examples#samplefriend)를 씬에 붙이면 키보드로 검색·요청·수락·귓속말을 바로 시험할 수 있습니다.
:::
