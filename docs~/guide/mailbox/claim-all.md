# 보상 전체 수령

```csharp
Task<SupabaseResult<IReadOnlyList<ClaimResult>>> Supabase.ClaimAllMailItemsAsync(string category = null)
```

미수령 보상이 있는 모든 우편의 보상을 한 번에 수령합니다. 지급 방식은 [보상 수령](/guide/mailbox/claim)과 같고 이 호출 안에서 끝나므로, 결과는 보상 표시에만 씁니다.

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
| `SupabaseReason.MailItemHandlerMissing` | 대상 우편 중 하나라도 핸들러도 숫자 데이터 필드도 없는 key가 있어 어느 우편도 수령되지 않았습니다. `ErrorCode`는 `mail_item_handler_missing:<key>`입니다 |

**실사용 예시**

우편함의 모두 받기 버튼에서 지금 탭의 보상을 한 번에 받고 한 창에 모아 보여 줍니다.

```csharp
public async void OnClickClaimAll()
{
    claimAllButton.interactable = false;
    var result = await Supabase.ClaimAllMailItemsAsync(currentTab);
    claimAllButton.interactable = true;

    if (result.Reason == SupabaseReason.MailItemHandlerMissing)
    {
        ShowToast("받을 수 없는 보상이 있어 받지 못했습니다");   // 핸들러 등록을 빠뜨렸습니다
        return;
    }
    if (!result.IsSuccess) { ShowToast("보상을 받지 못했습니다"); return; }

    ShowRewardSummary(result.Data);   // 지급은 끝났고 표시만 합니다
    RefreshMailbox();
}
```
