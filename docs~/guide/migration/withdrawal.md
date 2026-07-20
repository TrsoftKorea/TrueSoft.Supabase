# 탈퇴 예약 · 취소

탈퇴 예약과 취소 흐름은 SDK 단독 사용과 동일합니다. 유예 기간 중의 **예약 취소**이며, 유예가 지나 탈퇴가 완료되면 계정이 삭제되어 되돌릴 수 없습니다.

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다. 플레이어가 예약 취소를 선택하면 `CancelWithdrawal(withdrawalKey)`를 호출합니다.

| 로그인 유형 | 취소 후 동작 |
|-------------|------------|
| 게스트 | `SignInAnonymouslyAsync()` 자동 재호출 → 성공 시 `ClearWithdrawalAsync()` 자동 호출 |
| Google / Apple | `OnWithdrawalCancelled` 이벤트 발행 → 개발자가 재인증 UI 표시 후 `ClearWithdrawalAsync()` 직접 호출 |
