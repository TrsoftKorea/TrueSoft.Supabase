# 탈퇴 예약 · 취소

탈퇴 예약과 취소 흐름은 SDK 단독 사용과 동일합니다. 유예 기간 중의 **예약 취소**이며, 유예가 지나 탈퇴가 완료되면 계정이 삭제되어 되돌릴 수 없습니다.

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다. 플레이어가 예약 취소를 선택하면 `CancelWithdrawal(withdrawalKey)`를 호출합니다.

`CancelWithdrawal`은 로그인 유형(게스트·Google·Apple)과 무관하게, 게이트가 저장해둔 취소 토큰으로 예약을 철회합니다. 성공 시 `OnWithdrawalCancelled`, 실패 시 `OnWithdrawalCancelLoginFailed` 이벤트가 발행됩니다.
