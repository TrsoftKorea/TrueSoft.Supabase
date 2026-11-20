# 우편 상세 조회

```csharp
Task<SupabaseResult<Mail>> Supabase.GetMailAsync(string mailId)
```

우편 한 건의 상세 내용을 조회합니다. 첨부 보상이 없는 우편은 조회하는 순간 수령 처리됩니다. 기한이 지난 우편은 조회해도 수령 처리되지 않습니다.

```csharp
var result = await Supabase.GetMailAsync(mailId);
if (result.IsSuccess)
{
    Mail mail = result.Data;
    titleLabel.text = mail.Title;
    bodyLabel.text  = mail.Content;
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 조회할 우편 UUID |

**반환**

`.Data`는 `Mail` 한 건입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MailNotFound` | 본인 소유가 아니거나 존재하지 않는 우편입니다 |

**실사용 예시**

목록에서 우편을 누르면 게임 언어로 상세 창을 열고, 보상 없는 우편은 연 순간 수령되므로 배지를 새로 고칩니다.

```csharp
public async void OnClickMailRow(string mailId)
{
    var result = await Supabase.GetMailAsync(mailId);
    if (result.Reason == SupabaseReason.MailNotFound)
    {
        ShowToast("삭제된 우편입니다");
        RefreshMailbox();
        return;
    }
    if (!result.IsSuccess) { ShowToast("우편을 열지 못했습니다"); return; }

    var mail = result.Data;
    string lang = GameSettings.LanguageCode;
    mailPopup.Open(mail.TitleFor(lang), mail.ContentFor(lang), mail.Items, claimable: mail.HasUnclaimedItems);
    RefreshMailBadge();
}
```
