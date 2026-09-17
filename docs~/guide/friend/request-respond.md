# 받은 요청 수락·거절

```csharp
Task<SupabaseResult> Supabase.RespondFriendRequestAsync(string requestId, bool accept)
```

받은 친구 요청을 수락하거나 거절합니다. 본인이 받는 쪽인 대기 중 요청만 처리할 수 있습니다.

```csharp
var r = await Supabase.RespondFriendRequestAsync(request.RequestId, accept: true);
if (r.IsSuccess)
    RemoveFromRequestList(request);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `requestId` | [받은 요청 목록](/guide/friend/requests-list)의 `RequestId` |
| `accept` | true면 수락, false면 거절 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendRequestNotFound` | 해당 요청이 없거나 본인이 받는 쪽이 아닙니다 |
| `SupabaseReason.FriendRequestNotPending` | 이미 처리된 요청입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: info 화면을 갱신하는 시점
호출이 성공하면 그 자리에서 목록에서 빼고, 수락이면 [친구 목록](/guide/friend/list)에도 바로 추가하세요. 따로 다시 불러올 필요는 없습니다.
:::
