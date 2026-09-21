# 닉네임 검색

```csharp
Task<SupabaseResult<FriendSearchResult>> Supabase.SearchFriendAsync(string nickname)
```

닉네임이 정확히 일치하는 유저를 찾습니다. 대소문자는 구분하지 않습니다.

```csharp
var r = await Supabase.SearchFriendAsync(input.text);
if (!r.IsSuccess)
{
    ShowToast(r.Reason == SupabaseReason.FriendNicknameNotFound ? "없는 닉네임입니다." : "검색에 실패했습니다.");
    return;
}

switch (r.Data.Relation)
{
    case FriendRelation.None:            ShowAddButton(r.Data);        break;
    case FriendRelation.Friends:         ShowAlreadyFriends(r.Data);   break;
    case FriendRelation.RequestSent:     ShowPendingSent(r.Data);      break;
    case FriendRelation.RequestReceived: ShowRespondPrompt(r.Data);    break;
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `nickname` | 찾을 닉네임. 정확히 일치해야 합니다 |

**반환**

`.Data`는 `FriendSearchResult`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `AccountId` | string | 찾은 유저의 계정 ID |
| `Name` | string | 닉네임 |
| `Relation` | `FriendRelation` | `None`·`Friends`·`RequestSent`·`RequestReceived` 중 하나 |
| `LastActivityAt` | DateTimeOffset? | 그 유저가 마지막으로 접속하거나 데이터를 저장한 시각. 기록이 없으면 null |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendNicknameEmpty` | 검색어가 비어 있습니다 |
| `SupabaseReason.FriendNicknameNotFound` | 해당 닉네임의 유저가 없습니다 |
| `SupabaseReason.FriendSelfRequest` | 본인 닉네임을 검색했습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip Relation으로 버튼을 바로 정합니다
검색 결과에 관계가 함께 오므로, 별도로 친구 목록·요청 목록과 대조할 필요 없이 `Relation` 값만으로 "추가"·"요청됨"·"이미 친구" 버튼을 바로 그릴 수 있습니다.
:::
