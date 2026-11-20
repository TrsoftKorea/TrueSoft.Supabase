# 아이템 핸들러 등록

```csharp
SupabaseResult Supabase.RegisterMailItemHandler(IMailItemHandler handler)
```

세이브의 숫자 칸에 더하는 것만으로 끝나지 않는 보상을 게임 코드로 지급할 때 등록합니다. 보상 키가 [유저 데이터](/guide/user-data/)의 같은 이름 숫자 칸이면 핸들러 없이 서버가 수령과 함께 그 칸에 더하므로 등록하지 않아도 됩니다.

```csharp
public sealed class EnergyMailItemHandler : IMailItemHandler
{
    public string ItemKey => "energy";

    public async Task<SupabaseResult<ClaimResult>> HandleAsync(
        string mailId, int itemIndex, string itemKey, int count)
    {
        await Energy.AddAsync(count);   // 충전 시각 계산처럼 더하기만으로 안 되는 지급
        return SupabaseResult<ClaimResult>.Success(new ClaimResult
        {
            MailId = mailId, ItemIndex = itemIndex, ItemKey = itemKey, Count = count
        });
    }
}

Supabase.RegisterMailItemHandler(new EnergyMailItemHandler());
```

[보상 수령](/guide/mailbox/claim) 직후 보상 키와 `ItemKey`가 같은 핸들러가 우편에 담긴 순서대로 호출됩니다. 등록한 키는 세이브에 같은 이름 칸이 있어도 핸들러가 지급합니다. 등록 해제는 `Supabase.UnregisterMailItemHandler("energy")`입니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `handler` | 지급할 아이템 키와 지급 로직 |

**핸들러가 받는 값**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 받은 우편 ID |
| `itemIndex` | 우편의 몇 번째 보상인지, 0부터 시작 |
| `itemKey` | 보상 키 |
| `count` | 지급 수량 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MailItemHandlerInvalid` | 핸들러가 null이거나 `ItemKey`가 비어 있습니다 |

**실사용 예시**

앱 시작 때 등록하고, 받기 버튼에서 보상을 받아 팝업을 띄웁니다.

```csharp
void Awake()
{
    Supabase.RegisterMailItemHandler(new EnergyMailItemHandler());   // 로그인 전이어도 됩니다
}

public async void OnClickClaim(string mailId)
{
    var result = await Supabase.ClaimMailItemsAsync(mailId);
    if (!result.IsSuccess) { ShowToast("보상을 받지 못했습니다"); return; }

    foreach (var reward in result.Data)   // 지급은 끝났고 표시만 합니다
        ShowRewardPopup(reward.ItemKey, reward.Count);
}
```

::: info 서버가 지급하는 보상
핸들러가 없는 키는 서버가 수령과 함께 세이브의 같은 이름 숫자 칸에 더하고, 게임 안 세이브 값에도 바로 반영됩니다. 받음 표시와 지급이 한 처리라 도중에 앱이 꺼져도 보상이 사라지거나 두 번 지급되지 않습니다. 핸들러도 숫자 칸도 없는 키가 있으면 `SupabaseReason.MailItemHandlerMissing`으로 실패합니다.
:::
