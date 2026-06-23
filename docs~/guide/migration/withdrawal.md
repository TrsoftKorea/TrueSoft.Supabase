# 탈퇴 / 복구

탈퇴 신청과 복구 흐름은 SDK 단독 사용과 동일합니다.

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다. 플레이어가 복구를 선택하면 `RestoreWithdrawal(withdrawalKey)`를 호출합니다.

| 로그인 유형 | 복구 후 동작 |
|-------------|------------|
| 게스트 | `TrySignInAnonymouslyAsync()` 자동 재호출 → 성공 시 `TryClearMyWithdrawalAsync()` 자동 호출 |
| Google / Apple | `OnWithdrawalRestored` 이벤트 발행 → 개발자가 재인증 UI 표시 후 `TryClearMyWithdrawalAsync()` 직접 호출 |

---
