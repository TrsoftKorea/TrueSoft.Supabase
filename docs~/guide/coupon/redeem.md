# 쿠폰 사용

```csharp
Task<SupabaseResult> Supabase.RedeemCouponAsync(string code)
```

유저가 입력한 쿠폰 코드를 사용합니다. 성공하면 서버가 보상 우편을 만듭니다.

```csharp
var result = await Supabase.RedeemCouponAsync(input.text);
if (result.IsSuccess)
{
    ShowToast("쿠폰을 사용했습니다. 우편함을 확인하세요.");
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
| `code` | 유저가 입력한 코드. 대소문자와 앞뒤 공백은 서버가 정규화하므로 게임에서 다듬을 필요가 없습니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CouponNotFound` | 존재하지 않는 코드입니다 |
| `SupabaseReason.CouponInactive` | 운영이 사용을 중지한 쿠폰입니다 |
| `SupabaseReason.CouponExpired` | 사용 기한이 지났습니다 |
| `SupabaseReason.CouponAlreadyUsed` | 일반 쿠폰은 그 코드가 이미 쓰였고, 키워드 쿠폰은 본인이 이미 썼습니다 |
| `SupabaseReason.CouponExhausted` | 키워드 쿠폰의 최대 사용 횟수가 소진되었습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: info 보상은 응답에 없습니다
지급 내역은 [우편](/guide/mailbox/)으로 오므로, 성공 후 우편함을 새로 조회하세요. 자세한 이유는 [보상은 우편으로 옵니다](/guide/coupon/#reward-by-mail)를 참고하세요.
:::
