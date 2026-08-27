# 탈퇴 취소

`RequestWithdrawalAsync`로 하는 탈퇴 예약은 SDK 단독 사용과 동일해 이관에서 따로 할 일이 없습니다. **취소도 표준 SDK API 그대로** `Supabase.RedeemWithdrawalCancelAsync()`를 호출하면 됩니다 — `PlayNanooRuntime`이 인터셉터로 나누 탈퇴 복구까지 함께 처리합니다. 그래서 플레이나누를 제거해도 이 취소 코드는 그대로 동작합니다.

## 취소 흐름

로그인 결과의 `Reason`이 `SupabaseReason.WithdrawalGateBlocked`이면 탈퇴 예약 상태이므로 취소 UI를 띄우고, 플레이어가 취소를 선택하면 `Supabase.RedeemWithdrawalCancelAsync()`를 호출합니다.

```csharp
var login = await Supabase.SignInAnonymouslyAsync();
if (login.Reason == SupabaseReason.WithdrawalGateBlocked)
{
    // 남은 유예 시간 등을 안내하고, 플레이어가 취소를 선택하면
    var cancelled = await Supabase.RedeemWithdrawalCancelAsync();
    if (cancelled)
        ShowMessage("탈퇴가 취소되었습니다. 다시 로그인해 주세요.");
    else
        ShowError("탈퇴 취소에 실패했습니다.");
}
```

인터셉터가 나누 복구를 먼저 수행하고, 성공하면 Supabase 예약을 철회합니다. 나누 복구가 실패하면 Supabase 예약은 그대로 두고 실패를 반환해 양쪽 상태를 일치시킵니다.

::: tip 감지 시점 훅
로그인 호출부마다 결과를 검사하기 번거로우면 `playNanooRuntime.OnWithdrawalPending` 이벤트로 감지 시점에 취소 UI를 띄울 수 있습니다. 취소 실행은 동일하게 `Supabase.RedeemWithdrawalCancelAsync()`입니다.
:::

::: warning 유예 기간
유예 기간 중의 예약 취소입니다. 유예가 지나 탈퇴가 완료되면 계정이 삭제되어 되돌릴 수 없습니다.
:::
