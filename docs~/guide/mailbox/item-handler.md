# 아이템 핸들러 등록

우편 보상(`items[].key`)을 실제로 게임에 지급하는 로직은 `IMailItemHandler`를 구현해 앱 시작 시 등록합니다. 로그인 전에 등록해도 됩니다.

```csharp
public sealed class GoldMailItemHandler : IMailItemHandler
{
    public string ItemKey => "gold";

    public async Task<SupabaseResult<ClaimResult>> HandleAsync(
        string mailId, int itemIndex, string itemKey, int count)
    {
        MyWallet.AddGold(count);
        return SupabaseResult<ClaimResult>.Success(new ClaimResult
        {
            MailId = mailId, ItemIndex = itemIndex, ItemKey = itemKey, Count = count
        });
    }
}

MailItemHandlerRegistry.Register(new GoldMailItemHandler());
```

`ItemKey`가 `items[].key`와 일치하는 핸들러가 [보상 수령](/guide/mailbox/claim) 성공 직후 `itemIndex` 오름차순으로 호출됩니다.

::: warning
수령 RPC를 호출하기 **전에** 대상 우편의 모든 아이템 key에 핸들러가 등록됐는지 검증합니다. 하나라도 없으면 `mail_item_handler_missing:<key>`로 즉시 실패하고 서버 상태는 바뀌지 않습니다 — 핸들러 누락으로 수령 처리만 되고 아이템이 유실되는 상황을 막기 위한 설계입니다.
:::
