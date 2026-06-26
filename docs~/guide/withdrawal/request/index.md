# 탈퇴 처리

즉시 삭제하지 않고 일정 유예 기간 후에 처리됩니다. 유예 기간 동안 플레이어가 탈퇴를 취소할 수 있습니다. 유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

::: info
유예 기간이 만료된 계정은 로그인 시 자동으로 처리됩니다.  
[Edge Function 배포](/guide/start/database-setup#edge-function-deploy)가 완료되어 있어야 합니다.
:::

| 기능 | 메서드 |
|------|--------|
| 탈퇴 신청 | [탈퇴 신청](./submit) |
| 예약 상태 확인 | [예약 상태 확인](./status) |
| 예약 취소 | [예약 취소](./cancel) |
