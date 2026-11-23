# 우편 보상 수령

```csharp
Task<SupabaseResult<IReadOnlyList<ClaimResult>>> Supabase.ClaimMailItemsAsync(string mailId)
```

우편 1건의 첨부 보상을 수령합니다. 보상 키와 같은 이름의 숫자 데이터 필드에는 서버가 수령과 함께 더하고, 그 밖의 키는 등록해 둔 [아이템 핸들러](/guide/mailbox/item-handler)가 지급합니다. 이 호출 안에서 지급까지 끝나므로 결과는 보상 표시에만 씁니다.

```csharp
var result = await Supabase.ClaimMailItemsAsync(mailId);
if (result.IsSuccess)
{
    foreach (var reward in result.Data)
        ShowRewardPopup(reward.ItemKey, reward.Count);   // 지급은 이미 끝났으니 표시만 합니다
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 수령할 우편 UUID |

**반환**

`.Data`는 지급된 보상의 `ClaimResult` 목록입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `ItemKey` | `string` | 아이템 키 |
| `Count` | `int` | 지급 수량 |
| `ItemIndex` | `int` | 우편의 몇 번째 보상인지, 0부터 시작 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MailItemHandlerMissing` | 핸들러도 없고 같은 이름의 숫자 데이터 필드도 없는 key가 있습니다. 우편은 수령되지 않습니다. `ErrorCode`는 `mail_item_handler_missing:<key>`입니다 |
| `SupabaseReason.MailExpired` | 만료된 우편입니다 |
| `SupabaseReason.MailNotFound` | 본인 소유가 아니거나 존재하지 않는 우편입니다 |
| `SupabaseReason.AlreadyClaimed` | 이미 보상을 수령한 우편입니다 |

**실사용 예시**

우편 상세 창의 받기 버튼에서 보상을 받고, 받을 수 없는 우편이면 까닭을 알린 뒤 목록을 새로 고칩니다.

```csharp
public async void OnClickClaim(Mail mail)
{
    var result = await Supabase.ClaimMailItemsAsync(mail.Id);
    if (result.IsSuccess)
        ShowRewardSummary(result.Data);   // 지급은 끝났고 표시만 합니다
    else if (result.Reason == SupabaseReason.MailExpired)
        ShowToast("받을 수 있는 기한이 지났습니다");
    else if (result.Reason == SupabaseReason.AlreadyClaimed)
        ShowToast("이미 받은 보상입니다");   // 다른 기기에서 먼저 받았습니다
    else
        ShowToast("보상을 받지 못했습니다");

    RefreshMailbox();
}
```

::: info 수령과 지급이 한 번에 일어납니다
서버가 지급하는 보상은 수령 표시와 같은 처리 안에서 데이터 필드에 더해지므로, 지급 도중 앱이 꺼져도 보상이 사라지거나 두 번 지급되지 않습니다. 게임 안의 값에도 SDK가 바로 반영합니다.
:::
