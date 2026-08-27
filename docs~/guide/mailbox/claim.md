# 우편 보상 수령

```csharp
Task<SupabaseResult<IReadOnlyList<ClaimResult>>> Supabase.ClaimMailItemsAsync(string mailId)
```

우편 1건의 첨부 보상을 수령합니다. 수령과 동시에 읽음 처리됩니다.

```csharp
var result = await Supabase.ClaimMailItemsAsync(mailId);
if (result.IsSuccess)
{
    foreach (var reward in result.Data)   // 지급된 보상 목록
        GrantItem(reward.ItemKey, reward.Count);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 수령할 우편 UUID |

**반환**

`.Data`에 `ClaimResult` 목록으로 지급된 보상이 담깁니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.ItemKey` | `string` | 아이템 키 |
| `.Count` | `int` | 지급 수량 |
| `.ItemIndex` | `int` | 배열 내 순서, 0부터 시작 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `mail_item_handler_missing:<key>` | 해당 key의 [아이템 핸들러](/guide/mailbox/item-handler)가 등록되지 않았습니다. 수령 처리 전 검증되어 서버 상태는 바뀌지 않습니다 |
| `mail_expired` | 만료된 우편입니다 |
| `mail_not_found` | 본인 소유가 아니거나 존재하지 않는 우편입니다 |
| `SupabaseReason.AlreadyClaimed` | 이미 보상을 수령한 우편입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: warning
수령 RPC를 호출하기 **전에** 대상 우편의 모든 아이템 key에 핸들러가 등록돼 있는지 검증합니다. 하나라도 없으면 즉시 실패하고 서버 상태는 바뀌지 않습니다 — 핸들러 누락으로 수령 처리만 되고 아이템이 유실되는 상황을 막기 위한 설계입니다.
:::
