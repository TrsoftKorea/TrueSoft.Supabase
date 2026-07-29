# 채널 구독

```csharp
SupabaseResult<ChatSubscription> Supabase.SubscribeChat(
    IEnumerable<string>                            channelCodes,
    Action<string, IReadOnlyList<ChatMessage>>     onMessages,
    float                                          minIntervalSeconds = 2,
    float                                          maxIntervalSeconds = 10)
```

채널을 구독해 새 메시지를 콜백으로 받습니다. 네트워크를 타지 않고 즉시 반환하며, 실제 조회는 이후 백그라운드에서 돕니다.

```csharp
private ChatSubscription _sub;

void OpenChatWindow()
{
    var r = Supabase.SubscribeChat(new[] { "shout", "server" }, OnMessages);
    if (!r.IsSuccess) { ShowToast("채팅을 열지 못했습니다."); return; }

    _sub = r.Data;
}

void OnMessages(string channelCode, IReadOnlyList<ChatMessage> messages)
{
    foreach (var m in messages)
        AppendBubble(channelCode, m.DisplayName, m.Deleted ? "(삭제된 메시지)" : m.Content);
}

void OnDestroy() { _sub?.Dispose(); _sub = null; }
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `channelCodes` | 구독할 채널 코드. 여러 개를 넘겨도 서버 요청은 한 번으로 묶입니다 |
| `onMessages` | 채널 코드와 새로 도착한 메시지를 받습니다. 오래된 순입니다 |
| `minIntervalSeconds` | 대화가 오갈 때의 조회 간격 (기본값: 2) |
| `maxIntervalSeconds` | 조용할 때 늘어나는 상한 (기본값: 10) |

**반환**

| 멤버 | 설명 |
|------|------|
| `Dispose()` | 구독을 해제하고 조회를 멈춥니다 |
| `Reload()` | 커서를 되감아 다음 조회에서 지난 대화를 다시 받습니다 |
| `ChannelCodes` | 구독 중인 채널 코드 |
| `CurrentIntervalSeconds` | 현재 조회 간격. 조용하면 늘어납니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.ChatChannelsEmpty` | 채널 코드가 하나도 없습니다 |

## 콜백이 넘겨주는 메시지 {#message}

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Id` | long | 조회 커서. 채널 안에서 시간순으로 증가합니다 |
| `AccountId` | string | 보낸 사람의 계정 |
| `UserId` | string | 보낸 사람의 플레이어 ID |
| `DisplayName` | string | 보낸 시점의 닉네임 |
| `Content` | string | 본문. `Deleted`가 true면 null |
| `Deleted` | bool | 운영자가 지운 채팅입니다 |
| `CreatedAt` | DateTimeOffset | 보낸 시각 |

내가 보낸 메시지도 이 콜백으로 되돌아옵니다. 발송 직후 화면에 직접 넣지 말고 콜백에서 한 번만 그리세요. 그러지 않으면 같은 말이 두 번 보입니다.

::: info 닉네임은 보낸 시점 기준입니다
`DisplayName`은 그때 쓰던 이름이 그대로 남습니다. 개명해도 지난 대화의 이름은 바뀌지 않습니다.
:::

## 조회 간격이 스스로 조절됩니다 {#adaptive}

대화가 오가면 `minIntervalSeconds`를 유지하고, 아무 말도 없으면 `maxIntervalSeconds`까지 점점 늘어납니다. 조회에 실패하면 간격을 더 크게 벌려 장애 상황에서 서버를 계속 두드리지 않습니다.

구독이 하나도 없으면 네트워크를 전혀 쓰지 않습니다. 채팅창을 열지 않은 유저는 비용이 발생하지 않습니다.

::: warning 해제를 빠뜨리면 계속 돕니다
`Dispose`를 부르지 않으면 채팅창을 닫아도 조회가 이어집니다. 씬 전환에도 안전하도록 `OnDestroy`에서 정리하세요. 로그아웃과 계정 전환 시에는 SDK가 알아서 정리합니다.
:::
