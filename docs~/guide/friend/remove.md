# 친구 삭제

```csharp
Task<SupabaseResult> Supabase.RemoveFriendAsync(string friendAccountId)
```

친구 관계를 끊습니다. 이후 다시 요청을 보내면 처음부터 새로 시작합니다.

```csharp
var r = await Supabase.RemoveFriendAsync(friend.AccountId);
if (r.IsSuccess)
    RemoveFromFriendList(friend);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `friendAccountId` | [친구 목록](/guide/friend/list)의 `AccountId` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendNotFound` | 해당 계정과 친구 관계가 아닙니다 |

**실사용 예시**

친구 줄의 삭제 버튼에서 확인 팝업을 거쳐 지웁니다.

```csharp
public async void OnClickRemoveFriend(FriendSummary friend)
{
    if (!await ShowConfirmPopup($"{friend.Name}님을 친구에서 삭제할까요?")) return;

    var result = await Supabase.RemoveFriendAsync(friend.AccountId);
    if (result.IsSuccess || result.Reason == SupabaseReason.FriendNotFound)
    {
        RemoveFromFriendList(friend);   // 상대가 먼저 끊었어도 목록에서 뺍니다
        return;
    }
    ShowToast("친구를 삭제하지 못했습니다");
}
```
