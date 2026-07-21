# 탈퇴 취소

탈퇴 예약(`RequestWithdrawalAsync`)은 SDK 단독 사용과 동일해 이관에서 따로 할 일이 없습니다. 이관에서 다른 것은 **취소 흐름**입니다 — SDK 단독의 `RedeemWithdrawalCancelAsync()` 대신 `PlayNanooRuntime`의 이벤트와 `CancelWithdrawal`을 씁니다.

## 취소 흐름

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending`이 `withdrawalKey`와 함께 발행됩니다. 플레이어가 취소를 선택하면 `CancelWithdrawal(withdrawalKey)`를 호출하고, 결과는 이벤트로 받습니다.

```csharp
playNanooRuntime.OnWithdrawalPending += withdrawalKey =>
{
    // 남은 유예 시간 등을 안내하고, 플레이어가 취소를 선택하면
    playNanooRuntime.CancelWithdrawal(withdrawalKey);
};

playNanooRuntime.OnWithdrawalCancelled         += () => ShowMessage("탈퇴가 취소되었습니다. 다시 로그인해 주세요.");
playNanooRuntime.OnWithdrawalCancelLoginFailed += () => ShowError("탈퇴 취소에 실패했습니다.");
```

`CancelWithdrawal`은 로그인 유형(게스트·Google·Apple)과 무관하게, 게이트가 저장해둔 취소 토큰으로 예약을 철회합니다.

::: warning 유예 기간
유예 기간 중의 예약 취소입니다. 유예가 지나 탈퇴가 완료되면 계정이 삭제되어 되돌릴 수 없습니다.
:::
