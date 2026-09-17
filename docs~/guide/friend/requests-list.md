# 대기 중인 요청 목록

```csharp
Task<SupabaseResult<IReadOnlyList<FriendRequestSummary>>> Supabase.GetFriendRequestsAsync(
    FriendRequestDirection direction = FriendRequestDirection.Incoming)
```

받은 요청 또는 보낸 요청 목록을 가져옵니다. 실시간 알림이 아니므로 원하는 주기로 직접 호출해야 합니다.

```csharp
var r = await Supabase.GetFriendRequestsAsync(FriendRequestDirection.Incoming);
if (r.IsSuccess)
    UpdateFriendRequestBadge(r.Data.Count);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `direction` | `Incoming`은 내가 받은 요청, `Outgoing`은 내가 보낸 요청(기본값: FriendRequestDirection.Incoming) |

**반환**

`.Data`는 `FriendRequestSummary` 목록입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `RequestId` | string | [수락·거절](/guide/friend/request-respond) 또는 [취소](/guide/friend/request-cancel)에 넘길 ID |
| `AccountId` | string | 상대 계정 ID — 받은 요청이면 보낸 사람, 보낸 요청이면 받는 사람 |
| `Name` | string | 상대 닉네임 |
| `CreatedAt` | DateTimeOffset | 요청이 만들어진 시각 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip 배지 개수는 이 목록의 길이입니다
`direction: Incoming`으로 받은 목록의 개수를 그대로 알림 배지에 쓰면 됩니다.
:::
