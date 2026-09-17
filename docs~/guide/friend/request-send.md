# 친구 요청 전송

```csharp
Task<SupabaseResult<FriendRequestSendOutcome>> Supabase.SendFriendRequestAsync(string targetAccountId)
```

상대에게 친구 요청을 보냅니다. 상대가 이미 나에게 보낸 요청이 대기 중이었다면, 새 요청을 만드는 대신 그 자리에서 즉시 친구가 됩니다.

```csharp
var r = await Supabase.SendFriendRequestAsync(searchResult.AccountId);
if (!r.IsSuccess)
{
    ShowToast(r.Reason == SupabaseReason.FriendAlreadyFriends ? "이미 친구입니다." : "요청을 보내지 못했습니다.");
    return;
}

ShowToast(r.Data.Accepted ? "서로 요청이 있어 바로 친구가 되었습니다." : "요청을 보냈습니다.");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `targetAccountId` | 요청 대상의 계정 ID. [닉네임 검색](/guide/friend/search) 결과의 `AccountId`를 씁니다 |

**반환**

`.Data`는 `FriendRequestSendOutcome`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `RequestId` | string | 생성되거나 즉시 수락된 요청 ID |
| `Accepted` | bool | true면 상호 요청으로 즉시 친구가 된 것입니다. false면 상대 응답 대기 중입니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendTargetRequired` | 대상이 지정되지 않았습니다 |
| `SupabaseReason.FriendSelfRequest` | 자기 자신에게 보내려 했습니다 |
| `SupabaseReason.FriendAlreadyFriends` | 이미 친구입니다 |
| `SupabaseReason.FriendRequestAlreadySent` | 이미 보낸 요청이 대기 중입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
