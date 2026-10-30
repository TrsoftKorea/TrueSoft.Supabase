# 게임 중에 온 알림

```csharp
event Action<SupabasePushMessage> Supabase.OnPushReceived
```

게임이 화면에 떠 있는 동안 푸시 알림이 오면 발행됩니다. 이때 기기에는 알림을 띄우지 않으므로, 우편 배지나 새 답변 표시를 여기서 새로 고칩니다. 씬에 `SupabaseRuntime`이 있어야 발행됩니다.

```csharp
void OnEnable()  => Supabase.OnPushReceived += OnPush;
void OnDisable() => Supabase.OnPushReceived -= OnPush;

async void OnPush(SupabasePushMessage push)
{
    switch (push.Kind)
    {
        case SupabasePushKind.Mail:
            mailBadge.Refresh();
            break;
        case SupabasePushKind.SupportReply:
            var unread = await Supabase.GetCustomerCenterUnreadCountAsync();
            if (unread.IsSuccess) supportBadge.SetCount(unread.Data);
            break;
        case SupabasePushKind.FriendRequest:
        case SupabasePushKind.LobbyInvite:
            toast.Show(push.Title, push.Body);
            break;
    }
}
```

**SupabasePushMessage**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Kind` | `SupabasePushKind` | 어떤 상황의 알림인지 |
| `Title` | `string` | 알림 제목. 기기에 떴다면 보였을 문구입니다 |
| `Body` | `string` | 알림 본문 |
| `Data` | `IReadOnlyDictionary<string, string>` | 상황별 값 |

**SupabasePushKind와 Data**

| Kind | 상황 | Data에 든 값 |
|------|------|------|
| `Mail` | 우편&nbsp;도착 | `batch_id` 발송 묶음, `category` 우편 분류 |
| `SupportReply` | 고객센터&nbsp;답변 | `ticket_id` 문의 |
| `FriendRequest` | 친구&nbsp;요청 | `request_id` 친구 요청 |
| `LobbyInvite` | 매치&nbsp;로비&nbsp;초대 | `lobby_id` 로비 |
| `Unknown` | SDK보다&nbsp;새로&nbsp;생긴&nbsp;상황 | 서버가 보낸 값 그대로 |

구독하기 전에 온 알림은 1분 안의 것만 구독한 뒤에 발행됩니다. 로그아웃하거나 다른 계정으로 로그인하면 아직 발행하지 않은 알림은 버립니다.

게임이 뒤에 있거나 꺼져 있을 때 온 알림은 기기에 뜨고 이 이벤트는 발행되지 않습니다. 그 알림을 눌러 게임이 열려도 어떤 알림이었는지는 전달되지 않습니다.
