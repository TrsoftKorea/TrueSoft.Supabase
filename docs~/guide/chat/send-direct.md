# 귓속말 발송

```csharp
Task<SupabaseResult<ChatSendResult>> Supabase.SendDirectChatAsync(string targetAccountId, string content)
```

[친구](/guide/friend/)에게 1:1 메시지를 보냅니다. 친구가 아니면 실패합니다.

```csharp
var r = await Supabase.SendDirectChatAsync(friend.AccountId, input.text);
if (r.IsSuccess) { input.text = ""; return; }

ShowToast(r.Reason switch
{
    SupabaseReason.FriendNotFound     => "친구가 아닙니다.",
    SupabaseReason.ChatMessageTooLong => "글자 수를 넘었습니다.",
    SupabaseReason.ChatMuted          => "채팅이 제한된 상태입니다.",
    _                                 => "보내지 못했습니다.",
});
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `targetAccountId` | 받을 친구의 계정 ID |
| `content` | 보낼 내용. 앞뒤 공백은 서버가 다듬습니다 |

**반환**

`.Data`는 `ChatSendResult`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Id` | `long` | 방금 보낸 메시지의 커서 |
| `CreatedAt` | `DateTimeOffset` | 서버가 기록한 보낸 시각 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendTargetRequired` | 받을 대상이 지정되지 않았습니다 |
| `SupabaseReason.FriendSelfRequest` | 자기 자신에게 보내려 했습니다 |
| `SupabaseReason.FriendNotFound` | 친구가 아닙니다 |
| `SupabaseReason.ChatMessageEmpty` | 보낼 내용이 비어 있습니다 |
| `SupabaseReason.ChatMessageTooLong` | 최대 글자 수를 넘었습니다 |
| `SupabaseReason.ChatMuted` | 채팅이 차단된 계정입니다 |

**실사용 예시**

대화창의 보내기 버튼에서 보내고, 친구가 끊긴 상대면 창을 닫고 친구 목록을 새로 고칩니다.

```csharp
public async void OnClickSendDirect()
{
    if (string.IsNullOrWhiteSpace(input.text)) return;
    sendButton.interactable = false;   // 두 번 눌러 같은 말이 가지 않게 합니다

    var result = await Supabase.SendDirectChatAsync(_friendId, input.text);
    sendButton.interactable = true;

    if (result.IsSuccess) { input.text = ""; return; }   // 말풍선은 다음 조회에서 그립니다
    if (result.Reason == SupabaseReason.FriendNotFound)
    {
        ShowPopup("더 이상 친구가 아닙니다", onClose: CloseDirectChat);
        RefreshFriendList();
    }
    else ShowToast("보내지 못했습니다");
}
```

::: info 보낸 메시지는 조회로 돌아옵니다
발송에 성공했다고 화면에 직접 넣지 마세요. 다음 [대화 조회](/guide/chat/fetch-direct)에서 같은 메시지가 오므로 두 번 보이게 됩니다.
:::
