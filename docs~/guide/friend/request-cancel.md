# 내가 보낸 요청 취소

```csharp
Task<SupabaseResult> Supabase.CancelFriendRequestAsync(string requestId)
```

내가 보낸 대기 중 요청을 취소합니다.

```csharp
var r = await Supabase.CancelFriendRequestAsync(request.RequestId);
if (r.IsSuccess)
    RemoveFromRequestList(request);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `requestId` | [보낸 요청 목록](/guide/friend/requests-list)의 `RequestId` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendRequestNotFound` | 해당 요청이 없거나 본인이 보낸 쪽이 아닙니다 |
| `SupabaseReason.FriendRequestNotPending` | 이미 처리된 요청입니다 |

**실사용 예시**

보낸 요청 창의 취소 버튼에서 부르고, 상대가 먼저 응답했으면 목록을 다시 받습니다.

```csharp
public async void OnClickCancelRequest(FriendRequestSummary request)
{
    var result = await Supabase.CancelFriendRequestAsync(request.RequestId);
    if (result.IsSuccess) { RemoveFromRequestList(request); return; }

    if (result.Reason == SupabaseReason.FriendRequestNotPending)
    {
        ShowToast("상대가 이미 응답한 요청입니다");
        RefreshOutgoingRequests();
        return;
    }
    ShowToast("요청을 취소하지 못했습니다");
}
```
