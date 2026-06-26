# 계정 API

## 닉네임

| 메서드 | 설명 |
|--------|------|
| [`TryIsDisplayNameAvailableAsync`](/guide/display-name/nickname#check) | 닉네임 사용 가능 여부 확인 |
| [`TrySetMyDisplayNameAsync`](/guide/display-name/nickname#set) | 내 닉네임 설정 |
| [`TryGetPublicDisplayNameAsync`](/guide/display-name/nickname#get-other) | 다른 플레이어 닉네임 조회 |

## 프로필

| 멤버 | 설명 |
|------|------|
| [`Supabase.MyProfile`](/guide/auth/auto-login#after-login-values) | 로그인 시 캐시된 내 프로필 |
| [`TryGetPublicProfileAsync`](/guide/display-name/profile) | 다른 플레이어 공개 프로필 조회 |

## 탈퇴

| 메서드 | 설명 |
|--------|------|
| [`TryRequestMyWithdrawalAsync`](/guide/withdrawal/request#request) | 탈퇴 예약 (유예 기간) |
| [`TryGetMyWithdrawalStatusAsync`](/guide/withdrawal/request#status) | 탈퇴 예약 상태 조회 |
| [`TryClearMyWithdrawalAsync`](/guide/withdrawal/request#clear) | 탈퇴 예약 취소 |
| [`TryRequestWithdrawalCancelTokenAsync`](/guide/withdrawal/token#issue) | 탈퇴 취소 토큰 발급 |
| [`TryRedeemWithdrawalCancelAsync`](/guide/withdrawal/token#redeem) | 토큰으로 탈퇴 취소 |

## 서버 이주

| 메서드 | 설명 |
|--------|------|
| [`TryTransferMyServerAsync`](/guide/withdrawal/server-transfer) | 다른 서버로 이주 |
| [`TryGetMyServerInfoAsync`](/guide/withdrawal/server-transfer) | 내 서버 정보 조회 |
