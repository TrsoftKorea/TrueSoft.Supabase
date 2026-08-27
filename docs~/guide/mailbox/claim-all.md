# 보상 일괄 수령

```csharp
Task<SupabaseResult<IReadOnlyList<ClaimResult>>> Supabase.ClaimAllMailItemsAsync(string category = null)
```

미수령 보상이 있는 모든 우편의 보상을 한 번에 수령합니다. 각 우편은 수령과 동시에 읽음 처리됩니다.

```csharp
var result = await Supabase.ClaimAllMailItemsAsync();
if (result.IsSuccess)
{
    foreach (var reward in result.Data)   // 지급된 보상 목록
        GrantItem(reward.ItemKey, reward.Count);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `category` | 수령 대상 분류. `null`이면 미수령 보상이 있는 전체 분류 (기본값: `null`) |

**반환**

`.Data`에 `ClaimResult`로 지급된 모든 보상이 우편·순서대로 담깁니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `mail_item_handler_missing:<key>` | 대상 우편 중 하나라도 [아이템 핸들러](/guide/mailbox/item-handler) 미등록 key가 있으면 수령 전 실패합니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
