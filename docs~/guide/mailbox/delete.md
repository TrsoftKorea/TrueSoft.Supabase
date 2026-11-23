# 우편 삭제

```csharp
Task<SupabaseResult> Supabase.DeleteMailAsync(string mailId)
```

우편 1건을 소프트 삭제로 우편함에서 숨깁니다. 기한 안의 미수령 보상이 남아 있으면 서버가 거부합니다. 기한이 지난 우편은 보상이 남아 있어도 지울 수 있습니다.

```csharp
var result = await Supabase.DeleteMailAsync(mailId);
if (result.IsSuccess)
{
    RemoveMailFromUI(mailId);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 삭제할 우편 ID. [우편함 목록](/guide/mailbox/list)의 `Mail.Id` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CannotDeleteUnclaimed` | 미수령 보상이 있어 삭제할 수 없습니다. 먼저 수령하세요 |
| `SupabaseReason.MailNotFound` | 본인 소유가 아니거나 존재하지 않는 우편입니다 |

**실사용 예시**

우편 목록의 삭제 버튼에서 지우고, 받지 않은 보상이 남았으면 먼저 받으라고 안내합니다.

```csharp
public async void OnClickDelete(Mail mail)
{
    var result = await Supabase.DeleteMailAsync(mail.Id);
    if (result.IsSuccess) { RemoveMailRow(mail.Id); return; }

    switch (result.Reason)
    {
        case SupabaseReason.CannotDeleteUnclaimed:
            ShowToast("보상을 먼저 받아 주세요");
            break;
        case SupabaseReason.MailNotFound:
            RemoveMailRow(mail.Id);   // 이미 없는 우편이라 목록에서만 뺍니다
            break;
        default:
            ShowToast("우편을 지우지 못했습니다");
            break;
    }
}
```
