# 탈퇴 처리

계정 탈퇴를 예약·취소하거나 바로 처리하는 기능입니다.

## 예약 탈퇴와 즉시 탈퇴 {#kinds}

탈퇴는 두 가지입니다. 기본은 **유예 기간을 둔 예약 탈퇴**로, 유예 중에는 취소할 수 있고 취소 없이 유예가 지나면 계정이 삭제됩니다. 바로 지워야 하면 [즉시 탈퇴](/guide/withdrawal/delete-now)를 씁니다.

유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 정하고 최소 1일입니다. 삭제 시각은 유예가 끝난 **다음 자정**입니다. 예를 들어 10월 27일 오후 3시에 7일 유예로 신청하면 11월 4일 0시에 삭제됩니다. 자정을 어느 나라 시간으로 셀지는 운영 콘솔 **설정 › 시간대**에서 프로젝트마다 정합니다.

## 처리 흐름

1. **탈퇴 신청** — `RequestWithdrawalAsync()`로 유예를 예약합니다. 성공하면 **즉시 로그아웃**되어 본편에 들어갈 수 없습니다. Apple로 로그인한 계정은 이때 [Apple 연결도 끊깁니다](/guide/social/apple/#revoke).
2. **유예 중 재로그인** — 예약된 계정으로 다시 로그인하면 SDK가 **게이트**에서 세션을 정리한 뒤, 로그인을 `WithdrawalGateBlocked` 실패로 반환합니다. 이때 로그인 결과에 삭제 예정 시각 `WithdrawnAt`과 취소 토큰 `WithdrawalCancelToken`이 함께 실려 옵니다.
3. **탈퇴 취소** — 그 값으로 남은 시간을 보여주고 `RedeemWithdrawalCancelAsync`로 예약을 해제합니다. 삭제 시각인 자정 전까지 취소할 수 있습니다.
4. **유예 만료** — 서버가 자정 직후에 계정을 삭제합니다. 완료된 탈퇴는 되돌릴 수 없고, 같은 소셜 계정으로 다시 로그인하면 새 계정이 만들어집니다.

::: info
취소 토큰은 게이트가 내부적으로 발급·저장하므로 게임이 직접 발급할 필요가 없습니다. 취소는 `RedeemWithdrawalCancelAsync()` 한 번으로 처리됩니다. [Edge Function 배포](/guide/start/database-setup#edge-function-deploy)와 [프로젝트 주소 등록](/guide/start/database-setup#project-url)이 선행되어야 합니다.
:::

::: details 자정과 삭제 사이에 로그인하면
서버가 삭제를 끝내기 전 몇 초 사이, 또는 서버 정리가 실패했을 때 로그인하면 그 로그인 안에서 계정을 지우고 새 계정을 만듭니다. 구글·애플은 방금 받은 로그인 토큰을 다시 써서 로그인 창이 한 번만 뜹니다. 구글은 서버가 그 토큰을 다시 받지 않으면 로그인 창을 한 번 더 띄웁니다.
:::

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`RequestWithdrawalAsync`](/guide/withdrawal/submit) | 탈퇴 신청 |
| [`RedeemWithdrawalCancelAsync`](/guide/withdrawal/cancel) | 탈퇴 취소 |
| [`DeleteAccountNowAsync`](/guide/withdrawal/delete-now) | 즉시 탈퇴 |

예약 상태는 메서드 없이 로그인 결과로 받습니다. [예약 상태 확인](/guide/withdrawal/status)을 참고하세요.
