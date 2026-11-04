# 보상 전체 수령

```csharp
Task<SupabaseResult<IReadOnlyList<ClaimResult>>> Supabase.ClaimAllMailItemsAsync(string category = null)
```

미수령 보상이 있는 모든 우편의 보상을 한 번에 수령합니다. 지급은 [아이템 핸들러](/guide/mailbox/item-handler)가 이 호출 안에서 끝내므로, 결과는 보상 표시에만 씁니다.

```csharp
var result = await Supabase.ClaimAllMailItemsAsync();
if (result.IsSuccess)
{
    foreach (var reward in result.Data)
        ShowRewardPopup(reward.ItemKey, reward.Count);   // 지급은 이미 끝났으니 표시만 합니다
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `category` | 수령 대상 분류. `null`이면 미수령 보상이 있는 전체 분류 (기본값: `null`) |

**반환**

`.Data`는 지급된 모든 보상의 `ClaimResult` 목록입니다. 우편 순서, 우편 안 아이템 순서대로 담깁니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.Unknown` | 대상 우편 중 하나라도 [아이템 핸들러](/guide/mailbox/item-handler)가 등록되지 않은 key가 있어 수령 전에 멈췄습니다. `ErrorCode`는 `mail_item_handler_missing:<key>`입니다 |
