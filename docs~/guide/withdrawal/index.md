# 탈퇴 처리

계정 탈퇴는 즉시 삭제가 아니라 **유예 기간을 둔 예약제**입니다. 유예 중에는 취소할 수 있고, 취소 없이 유예가 지나면 다음 로그인 시 계정이 실제로 삭제됩니다. 유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

## 처리 흐름

1. **탈퇴 신청** — `RequestWithdrawalAsync()`로 유예를 예약합니다. 성공하면 **즉시 로그아웃**되어 본편에 들어갈 수 없습니다.
2. **유예 중 재로그인** — 예약된 계정으로 다시 로그인하면 SDK가 **게이트**에서 세션을 정리한 뒤, 로그인을 `WithdrawalGateBlocked` 실패로 반환합니다. 이때 로그인 결과에 삭제 예정 시각 `WithdrawnAt`과 취소 토큰 `WithdrawalCancelToken`이 함께 실려 옵니다.
3. **탈퇴 취소** — 그 값으로 남은 시간을 보여주고 `RedeemWithdrawalCancelAsync`로 예약을 해제합니다.
4. **유예 만료** — 취소하지 않고 유예가 지나면 다음 로그인 시 계정이 실제로 삭제됩니다. 완료된 탈퇴는 되돌릴 수 없으며, 재로그인 시 새 계정이 생성됩니다.

::: info
취소 토큰은 게이트가 내부적으로 발급·저장하므로 게임이 직접 발급할 필요가 없습니다. 취소는 `RedeemWithdrawalCancelAsync()` 한 번으로 처리됩니다. [Edge Function 배포](/guide/start/database-setup#edge-function-deploy)가 선행되어야 합니다.
:::

## 메서드

| 기능 | 메서드 |
|------|--------|
| 탈퇴 신청 | [탈퇴 신청](./submit) |
| 예약 상태 확인 | [예약 상태 확인](./status) |
| 탈퇴 취소 | [탈퇴 취소](./cancel) |
