# 로비 대화 발송

```csharp
Task<SupabaseResult<ChatSendResult>> Supabase.SendLobbyChatAsync(string lobbyId, string content)
```

[매치 로비](/guide/match-lobby/) 참가자끼리만 보이는 대화에 보냅니다. **초대를 수락해 실제로 방에 들어온 사람만** 쓸 수 있습니다.

```csharp
var r = await Supabase.SendLobbyChatAsync(_lobbyId, input.text);
if (r.IsSuccess) { input.text = ""; return; }

ShowToast(r.Reason switch
{
    SupabaseReason.MatchLobbyNotOpen         => "방이 닫혔습니다.",
    SupabaseReason.MatchLobbyMemberNotFound  => "이 방의 참가자가 아닙니다.",
    SupabaseReason.ChatMessageTooLong        => "글자 수를 넘었습니다.",
    _                                        => "보내지 못했습니다.",
});
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | 대상 로비의 `LobbyId` |
| `content` | 보낼 내용. 앞뒤 공백은 서버가 다듬습니다 |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Id` | long | 방금 보낸 메시지의 커서 |
| `CreatedAt` | DateTimeOffset | 서버가 기록한 보낸 시각 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotFound` | 로비가 지정되지 않았습니다 |
| `SupabaseReason.MatchLobbyNotOpen` | 로비가 취소·만료되었습니다 |
| `SupabaseReason.MatchLobbyMemberNotFound` | 아직 수락하지 않았거나 나간 방입니다 |
| `SupabaseReason.ChatMessageEmpty` | 보낼 내용이 비어 있습니다 |
| `SupabaseReason.ChatMessageTooLong` | 최대 글자 수를 넘었습니다 |
| `SupabaseReason.ChatMuted` | 채팅이 차단된 계정입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: info 수락해야 대화가 열립니다
초대만 받은 상태에서는 보내지도 읽지도 못합니다. 열어 두면 초대가 모르는 사람에게 말을 거는 통로가 되기 때문입니다. 수락 전에 물어볼 것이 있으면 들어와서 묻고 [나가면](/guide/match-lobby/leave) 됩니다.
:::

::: info 보낸 메시지는 조회로 돌아옵니다
발송에 성공했다고 화면에 직접 넣지 마세요. 다음 [대화 조회](/guide/chat/fetch-lobby)에서 같은 메시지가 오므로 두 번 보이게 됩니다.
:::
