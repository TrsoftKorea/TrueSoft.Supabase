# 닉네임 검색

```csharp
Task<SupabaseResult<FriendSearchResult>> Supabase.SearchFriendAsync(string nickname)
```

닉네임이 정확히 일치하는 플레이어를 찾습니다. 대소문자는 구분하지 않습니다.

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
| `AccountId` | `string` | 찾은 플레이어의 계정 ID |
| `Name` | `string` | 닉네임 |
| `Relation` | `FriendRelation` | `None`·`Friends`·`RequestSent`·`RequestReceived` 중 하나. 서버가 SDK보다 새 관계를 돌려주면 `Unknown` |
| `LastActivityAt` | `DateTimeOffset?` | 그 플레이어가 마지막으로 접속하거나 데이터를 저장한 시각. [갱신 시점](/guide/friend/#last-activity) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendNicknameEmpty` | 검색어가 비어 있습니다 |
| `SupabaseReason.FriendNicknameNotFound` | 해당 닉네임의 플레이어가 없습니다 |
| `SupabaseReason.FriendSelfRequest` | 본인 닉네임을 검색했습니다 |

**실사용 예시**

친구 추가 창의 검색 버튼에서 부르고, 못 찾은 이유를 입력란 아래에 띄웁니다.

```csharp
public async void OnClickSearch()
{
    var result = await Supabase.SearchFriendAsync(nicknameInput.text.Trim());
    if (result.IsSuccess) { ShowSearchResultCard(result.Data); return; }

    switch (result.Reason)
    {
        case SupabaseReason.FriendNicknameNotFound: SetInputHint("그 닉네임의 플레이어가 없습니다"); break;
        case SupabaseReason.FriendSelfRequest:      SetInputHint("내 닉네임입니다"); break;
        default:                                    ShowToast("검색하지 못했습니다"); break;
    }
}
```

::: tip Relation으로 버튼을 바로 정합니다
검색 결과에 관계가 함께 오므로, 별도로 친구 목록·요청 목록과 대조할 필요 없이 `Relation` 값만으로 "추가"·"요청됨"·"이미 친구" 버튼을 바로 그릴 수 있습니다.
:::
