# 메시지 발송

```csharp
Task<SupabaseResult<ChatSendResult>> Supabase.SendChatAsync(string channelCode, string content)
```

채널에 메시지를 보냅니다. 길이·발언 차단·연속 발화 검사는 모두 서버가 합니다.

```csharp
var r = await Supabase.SendChatAsync("shout", input.text);
if (r.IsSuccess) { input.text = ""; return; }

ShowToast(r.Reason switch
{
    SupabaseReason.ChatMessageTooLong  => "글자 수를 넘었습니다.",
    SupabaseReason.ChatMuted           => "채팅이 제한된 상태입니다.",
    SupabaseReason.ChatTooFast         => "조금 뒤에 다시 보내세요.",
    SupabaseReason.ChatChannelInactive => "지금은 사용할 수 없는 채널입니다.",
    _                                  => "보내지 못했습니다.",
});
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `channelCode` | [채널 목록](/guide/chat/channels)에서 받은 코드 |
| `content` | 보낼 내용. 앞뒤 공백은 서버가 다듬습니다 |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Id` | long | 방금 보낸 메시지의 커서 |
| `CreatedAt` | DateTimeOffset | 서버가 기록한 발화 시각 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.ChatMessageEmpty` | 보낼 내용이 비어 있습니다 |
| `SupabaseReason.ChatMessageTooLong` | 채널의 최대 글자 수를 넘었습니다 |
| `SupabaseReason.ChatMuted` | 발언이 차단된 계정입니다 |
| `SupabaseReason.ChatTooFast` | 채널에 설정된 발언 간격을 지키지 않았습니다 |
| `SupabaseReason.ChatChannelInactive` | 운영이 발언을 중지한 채널입니다 |
| `SupabaseReason.ChatChannelNotFound` | 존재하지 않는 채널입니다 |
| `SupabaseReason.ChatScopeUnavailable` | 서버가 정해지지 않아 서버 채팅을 쓸 수 없습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip 입력창 길이 제한
`ChatChannelInfo.MaxLength`를 입력창에 그대로 걸어 두면 `ChatMessageTooLong`을 만날 일이 없습니다. 서버 검사는 그래도 남겨 두는 안전장치입니다.
:::

::: info 보낸 메시지는 구독으로 돌아옵니다
발송에 성공했다고 화면에 직접 넣지 마세요. [구독 콜백](/guide/chat/subscribe#message)으로 같은 메시지가 도착하므로 두 번 보이게 됩니다.
:::
