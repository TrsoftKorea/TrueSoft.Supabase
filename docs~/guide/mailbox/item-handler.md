# 아이템 핸들러 등록

```csharp
SupabaseResult Supabase.RegisterMailItemHandlers(MailItemHandlers handlers)
```

데이터 필드에 더하는 것만으로 끝나지 않는 보상을 게임 코드로 지급할 때, 보상 키마다 지급 함수를 한 번에 등록합니다. 함수는 지급 수량을 받고, 기다려야 하는 지급은 `async` 함수로 씁니다. 숫자 데이터 필드나 리스트·딕셔너리 원소에 더하는 보상은 [서버가 지급](/guide/mailbox/#server-grant)하므로 등록하지 않아도 됩니다.

```csharp
Supabase.RegisterMailItemHandlers(new MailItemHandlers
{
    { "energy",  async count => await Energy.AddAsync(count) },   // 충전 시각 계산처럼 기다리는 지급
    { "skin_01", _ => Skins.Unlock("skin_01") },
});
```

[보상 수령](/guide/mailbox/claim) 직후 보상 키가 같은 함수가 우편에 담긴 순서대로 불리고, 앞 함수가 끝난 뒤 다음 함수가 불립니다. 등록한 키는 서버가 지급할 수 있는 키여도 등록한 함수가 지급합니다. 같은 키를 다시 등록하면 새 함수로 바뀌고, 등록 해제는 `Supabase.UnregisterMailItemHandler("energy")`입니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `handlers` | 보상 키와 지급 함수 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MailItemHandlerInvalid` | 목록이 비었거나, 키가 빈 줄·함수가 null인 줄·앞뒤 공백을 빼면 키가 같은 줄이 있습니다. 이때는 아무것도 등록하지 않습니다 |

**실사용 예시**

앱 시작 때 한 번 등록하고, 받기 버튼에서 보상을 받아 팝업을 띄웁니다.

```csharp
void Awake()
{
    var result = Supabase.RegisterMailItemHandlers(new MailItemHandlers   // 로그인 전이어도 됩니다
    {
        { "energy",  async count => await Energy.AddAsync(count) },
        { "revive",  count => Inventory.AddRevive(count) },
        { "skin_01", _ => Skins.Unlock("skin_01") },
    });
    if (!result.IsSuccess)
        Debug.LogError($"우편 보상 등록 실패: {result.Reason}");
}

public async void OnClickClaim(string mailId)
{
    var claim = await Supabase.ClaimMailItemsAsync(mailId);
    if (!claim.IsSuccess) { ShowToast("보상을 받지 못했습니다"); return; }

    foreach (var reward in claim.Data)   // 지급은 끝났고 표시만 합니다
        ShowRewardPopup(reward.ItemKey, reward.Count);
}
```

::: info 지급 함수에서 오류가 나면
지급 함수가 예외를 던지면 수령 결과가 `SupabaseReason.MailItemHandlerFailed` 실패로 돌아옵니다. 우편은 이미 받은 것으로 처리되고, 그 뒤 순서의 보상은 지급 함수에 넘어가지 않습니다. 등록한 함수도 서버가 더할 곳도 없는 키가 있으면 `SupabaseReason.MailItemHandlerMissing`으로 실패하고 우편은 받지 않은 채 남습니다. 운영 콘솔 **아이템 카탈로그**에서 지급 방식이 「자동 지급」으로 보이는 아이템이 서버가 지급하는 보상입니다.
:::
