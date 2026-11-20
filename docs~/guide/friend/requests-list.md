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
| `RequestId` | `string` | [수락·거절](/guide/friend/request-respond) 또는 [취소](/guide/friend/request-cancel)에 넘길 ID |
| `AccountId` | `string` | 상대 계정 ID. 받은 요청이면 보낸 사람, 보낸 요청이면 받는 사람 |
| `Name` | `string` | 상대 닉네임 |
| `CreatedAt` | `DateTimeOffset` | 요청이 만들어진 시각 |
| `LastActivityAt` | `DateTimeOffset?` | 상대가 마지막으로 접속하거나 데이터를 저장한 시각. [갱신 시점](/guide/friend/#last-activity) |

**실사용 예시**

메인 화면에서 30초마다 받은 요청 수로 배지를 갱신하고, 보낸 요청 탭을 열면 목록을 그립니다.

```csharp
async void Start()
{
    while (this != null)   // 화면이 닫히면 멈춥니다
    {
        var result = await Supabase.GetFriendRequestsAsync();
        if (result.IsSuccess) UpdateFriendRequestBadge(result.Data.Count);   // 실패하면 배지를 그대로 둡니다
        await Task.Delay(30_000);
    }
}

public async void OnOpenSentRequestsTab()
{
    var result = await Supabase.GetFriendRequestsAsync(FriendRequestDirection.Outgoing);
    if (!result.IsSuccess) { ShowRetryButton(); return; }
    foreach (var request in result.Data)
        AddSentRequestRow(request);   // 취소 버튼이 request.RequestId 를 씁니다
}
```

::: tip 배지 개수는 이 목록의 길이입니다
`direction: Incoming`으로 받은 목록의 개수를 그대로 알림 배지에 쓰면 됩니다.
:::
