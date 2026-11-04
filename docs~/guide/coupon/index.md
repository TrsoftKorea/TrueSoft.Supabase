# 쿠폰

```csharp
Task<SupabaseResult> Supabase.RedeemCouponAsync(string code)
```

플레이어가 입력한 쿠폰 코드를 사용합니다. 성공하면 서버가 보상 우편을 만듭니다. 쿠폰 생성·수정·삭제와 코드 발급은 운영 콘솔에서만 하므로 SDK에 없습니다.

```csharp
var result = await Supabase.RedeemCouponAsync(input.text);
if (result.IsSuccess)
{
    ShowToast("쿠폰을 사용했습니다. 우편함을 확인하세요.");
    await RefreshMailboxAsync();
    return;
}

ShowToast(result.Reason switch
{
    SupabaseReason.CouponNotFound    => "존재하지 않는 코드입니다.",
    SupabaseReason.CouponInactive    => "지금은 사용할 수 없는 쿠폰입니다.",
    SupabaseReason.CouponExpired     => "사용 기한이 지났습니다.",
    SupabaseReason.CouponAlreadyUsed => "이미 사용한 쿠폰입니다.",
    SupabaseReason.CouponExhausted   => "쿠폰이 모두 소진되었습니다.",
    _                                => "쿠폰 사용에 실패했습니다.",
});
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `code` | 플레이어가 입력한 코드. 대소문자와 앞뒤 공백은 서버가 정규화하므로 게임에서 다듬을 필요가 없습니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CouponNotFound` | 존재하지 않는 코드입니다 |
| `SupabaseReason.CouponInactive` | 운영이 사용을 중지한 쿠폰입니다 |
| `SupabaseReason.CouponExpired` | 사용 기한이 지났습니다 |
| `SupabaseReason.CouponAlreadyUsed` | 일반 쿠폰은 그 코드가 이미 쓰였고, 키워드 쿠폰은 본인이 이미 썼습니다 |
| `SupabaseReason.CouponExhausted` | 키워드 쿠폰의 최대 사용 횟수가 소진되었습니다 |

::: info 보상은 우편으로 옵니다
쿠폰 사용 응답에는 보상이 들어 있지 않습니다. 서버가 [우편](/guide/mailbox/)을 만들어 넣으므로, 게임은 사용에 성공한 뒤 우편함을 새로 조회하면 됩니다. 실제 지급은 기존 [아이템 핸들러](/guide/mailbox/item-handler)를 그대로 탑니다.
:::

::: info 두 가지 쿠폰
| 종류 | 코드 | 사용 제한 |
|------|------|-----------|
| 일반 | 발급&nbsp;수량만큼&nbsp;랜덤&nbsp;생성. `CODEDRH`·`CODEKFT` | 코드 1개당 1회 |
| 키워드 | 운영자가&nbsp;직접&nbsp;지정. `THANKYOU` | 최대 횟수까지 여러 명, 한 플레이어는 1회 |

일반 쿠폰은 코드 하나가 곧 선물 하나입니다. 같은 캠페인의 다른 코드를 여러 장 받았다면 각각 쓸 수 있고, 누가 어떤 코드를 썼는지 코드 단위로 남습니다. 키워드 쿠폰은 하나의 코드를 여러 사람이 씁니다. 최대 횟수를 비우면 무제한입니다.
:::

::: info 유효기간은 두 가지입니다
쿠폰의 만료 시각이 지나면 코드를 더 쓸 수 없습니다. 지급된 우편에는 그와 별개로 수령 기한이 있어, 쿠폰이 만료돼도 이미 받은 우편은 그 기한까지 수령할 수 있습니다.
:::
