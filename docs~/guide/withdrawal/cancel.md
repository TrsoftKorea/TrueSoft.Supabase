# 탈퇴 취소

```csharp
Task<SupabaseResult> Supabase.RedeemWithdrawalCancelAsync(string cancelToken = null)
```

탈퇴 예약을 취소합니다. 예약된 계정으로 로그인하면 `WithdrawalGateBlocked`로 막히고, 그 결과에 실려온 취소 토큰으로 비로그인 상태에서 취소합니다. 취소 시점에 따라 토큰을 넘기는 방식이 갈립니다.

**로그인 흐름 안에서 바로 취소** — 로그인 결과(`login`)를 쥐고 있으므로 그 `WithdrawalCancelToken`을 직접 넘깁니다. 저장 상태에 의존하지 않아 더 견고합니다.

```csharp
var login = await Supabase.TriggerAutoLoginAsync();
if (login.Reason == SupabaseReason.WithdrawalGateBlocked)
{
    // 남은 유예 시간 등을 보여주고, 사용자가 취소를 선택하면
    var cancel = await Supabase.RedeemWithdrawalCancelAsync(login.WithdrawalCancelToken);
    if (cancel.IsSuccess)
        ShowMessage("탈퇴가 취소되었습니다. 다시 로그인해 주세요.");
    else
        ShowError(cancel.Reason);
}
```

**나중에·다른 화면에서 취소** — 로그인 결과 객체가 이미 없을 때는 인자 없이 호출합니다. SDK가 게이트에서 저장해둔 토큰을 자동으로 사용합니다.

```csharp
var cancel = await Supabase.RedeemWithdrawalCancelAsync();   // 저장된 토큰 사용
```

::: info 어느 쪽이든 같은 토큰
같은 기기·같은 예약이면 두 방식 모두 게이트가 발급·저장한 동일한 토큰을 씁니다. 인자 없는 호출은 "결과 객체가 없을 때의 폴백"입니다.
:::

::: tip 교차 기기 취소
다른 기기에서 받은 토큰으로 취소하려면 그 토큰 문자열을 직접 넘깁니다. 저장된 토큰은 발급된 기기에만 있습니다.
:::

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `cancelToken` | 취소 토큰. 로그인 결과의 `.WithdrawalCancelToken`을 넘기거나, 비우면 게이트가 저장한 토큰을 사용 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.WithdrawalCancelTokenEmpty` | 저장된 취소 토큰이 없습니다 |
| `SupabaseReason.WithdrawalCancelJwtVerifyMustBeOff` | 취소 Edge Function의 `verify_jwt`를 꺼야 합니다 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
