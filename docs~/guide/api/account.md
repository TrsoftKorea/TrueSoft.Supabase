# 계정 API

## 닉네임

| 메서드 | 설명 |
|--------|------|
| [`IsNameAvailableAsync`](/guide/display-name/nickname/check) | 닉네임 사용 가능 여부 확인 |
| [`SetNameAsync`](/guide/display-name/nickname/set) | 내 닉네임 설정 |
| [`GetPublicNameAsync`](/guide/display-name/nickname/get-other) | 다른 플레이어 닉네임 조회 |

## 프로필

| 멤버 | 설명 |
|------|------|
| [내 프로필](/guide/display-name/profile) | 로그인 결과 `SupabaseSignInResult.Profile`로 획득 |
| [`GetPublicProfileAsync`](/guide/display-name/profile) | 다른 플레이어 공개 프로필 조회 |

## 탈퇴

| 메서드 | 설명 |
|--------|------|
| [`RequestWithdrawalAsync`](/guide/withdrawal/submit) | 탈퇴 예약 (유예 기간) |
| [`RedeemWithdrawalCancelAsync`](/guide/withdrawal/cancel) | 탈퇴 취소 |

## 서버 정보

| 메서드 | 설명 |
|--------|------|
| [`GetServerInfoAsync`](/guide/withdrawal/server-info) | 내 서버 정보 조회 |
