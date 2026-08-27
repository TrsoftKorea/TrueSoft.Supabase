# 데이터베이스 설정

DB 스키마와 Edge Function은 **Database Setup** 샘플에 포함된 파일로 설정합니다.

## 1. 샘플 임포트

Package Manager에서 **TrueBase** 패키지를 선택하고 **Samples** 탭에서 **Database Setup**을 Import합니다.  
`Assets/Samples/.../DatabaseSetup/` 폴더에 SQL 파일과 Edge Function 소스가 생성됩니다.

## 2. DB 스키마 실행

`SQL/player/install.sql` 전체를 Supabase SQL Editor에 붙여넣고 한 번 실행합니다. 스키마가 모두 설치됩니다.

::: tip
대시보드 어느 화면에서나 **SQL Editor** 버튼 또는 `Ctrl+E`로 열 수 있습니다.
:::

설치되는 내용은 다음과 같습니다.

| 절 | 내용 |
|----|------|
| 1~3 | 게임 서버 · 플레이어 프로필 · 익명 계정 복구 |
| 4~5 | 유저 데이터 저장 · 서버 이주 · 탈퇴 |
| 6 | 우편함 |
| 7~8 | 인앱 결제 영수증 · 원격 설정 |
| 9~11 | 크론 잡 · 계정 차단 · 유저 데이터 변경 로그 |
| 12~14 | 어드민 우편 발송 · 예약 · 분류 |
| 15~17 | 리더보드 · 운영자 스키마 버전관리 · 쿠폰 |
| 18 | 클라이언트 권한 최소화 |

설치 후 `verify.sql`을 실행하면 빠진 것이 없는지 확인할 수 있습니다.

::: warning 절 순서를 바꾸지 마세요
마지막 절은 모든 테이블·함수 권한을 회수한 뒤 필요한 것만 되돌려 줍니다. 앞 절에서 만든 함수를 이름으로 지정하므로, 순서를 바꾸거나 일부만 실행하면 함수를 찾지 못해 실패합니다.
:::

::: tip 다시 실행해도 안전합니다
모든 구문이 멱등이라 이미 설치된 프로젝트에 다시 실행해도 데이터가 사라지지 않습니다.
:::

## 3. 엣지 함수 배포 {#edge-function-deploy}

아래 과정을 각 함수마다 반복합니다.

1. Supabase 대시보드 > **Edge Functions** > **Deploy a new function** > **Via Editor** 클릭
2. 함수 이름을 정확히 입력하고 생성
3. Unity Project 창에서 `Assets/Samples/.../DatabaseSetup/EdgeFunctions/<함수명>/index.ts`를 열어 전체 내용 복사
4. 에디터에 붙여넣고 **Deploy** 클릭

| 함수 이름 | 필요 기능 |
|-----------|----------|
| `displayname-get` | 공개 프로필 · 닉네임 조회 |
| `displayname-set` | 공개 프로필 · 닉네임 설정 |
| `admin-displayname-set` | 어드민 · 닉네임 강제 변경. Retool 사용 시 |
| `withdrawal-cancel-issue` | 공개 프로필 · 탈퇴 취소 토큰 발급 |
| `withdrawal-cancel-redeem` | 공개 프로필 · 탈퇴 취소 토큰 사용 |
| `withdrawal-guard` | 공개 프로필 · 탈퇴 계정 자동 처리 |
| `purchase-verify-google` | 인앱 결제 · Android |
| `purchase-verify-apple` | 인앱 결제 · iOS · SK2 · Unity IAP v5 |
| `purchase-verify-apple-legacy` | 인앱 결제 · iOS · SK1 · Unity IAP v4 또는 forceStoreKit1 |
| `get-ban-info` | 인증 · 차단된 계정 정보 조회 |

## 4. 시크릿 설정

대시보드 **Edge Functions > Secrets**에 등록합니다.

| 시크릿 키 | 필수 | 용도 |
|----------|----|------|
| `CANCEL_TOKEN_SECRET` | 탈퇴&nbsp;취소&nbsp;사용&nbsp;시 | 탈퇴 취소 토큰 서명·검증에 사용하는 비밀 키. 랜덤 문자열 32자 이상 |
| `GOOGLE_SERVICE_ACCOUNT_JSON` | Android&nbsp;IAP&nbsp;사용&nbsp;시 | Google Play 결제 영수증 서버 검증에 사용하는 서비스 계정 키 |
| `APPLE_SHARED_SECRET` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시 | App Store Connect > 앱 정보 > 공유 암호. `purchase-verify-apple-legacy` 함수에서 사용 |

발급 절차는 [Google 서비스 계정 JSON 발급](/guide/google-service-account/issue)을 참고하세요.
