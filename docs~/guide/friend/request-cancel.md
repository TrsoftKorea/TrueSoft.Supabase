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
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
