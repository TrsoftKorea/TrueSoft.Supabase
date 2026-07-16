# 계정 API

## 닉네임

| 메서드 | 설명 |
|--------|------|
| [`IsDisplayNameAvailableAsync`](/guide/display-name/nickname/check) | 닉네임 사용 가능 여부 확인 |
| [`SetMyDisplayNameAsync`](/guide/display-name/nickname/set) | 내 닉네임 설정 |
| [`GetPublicDisplayNameAsync`](/guide/display-name/nickname/get-other) | 다른 플레이어 닉네임 조회 |

## 프로필

| 멤버 | 설명 |
|------|------|
| [내 프로필](/guide/display-name/profile) | 로그인 결과 `SupabaseSignInResult.Profile`로 획득 |
| [`GetPublicProfileAsync`](/guide/display-name/profile) | 다른 플레이어 공개 프로필 조회 |

## 탈퇴

| 메서드 | 설명 |
|--------|------|
| [`RequestMyWithdrawalAsync`](/guide/withdrawal/request/submit) | 탈퇴 예약 (유예 기간) |
| [`GetMyWithdrawalStatusAsync`](/guide/withdrawal/request/status) | 탈퇴 예약 상태 조회 |
| [`ClearMyWithdrawalAsync`](/guide/withdrawal/request/cancel) | 탈퇴 예약 취소 |
| [`RequestWithdrawalCancelTokenAsync`](/guide/withdrawal/token/issue) | 탈퇴 취소 토큰 발급 |
| [`RedeemWithdrawalCancelAsync`](/guide/withdrawal/token/redeem) | 토큰으로 탈퇴 취소 |

## 서버 이주

| 메서드 | 설명 |
|--------|------|
| [`TransferMyServerAsync`](/guide/withdrawal/server-transfer) | 다른 서버로 이주 |
| [`GetMyServerInfoAsync`](/guide/withdrawal/server-transfer) | 내 서버 정보 조회 |
