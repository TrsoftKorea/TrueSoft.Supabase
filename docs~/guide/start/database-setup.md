# Database Setup

DB 스키마와 Edge Function은 **Database Setup** 샘플에 포함된 파일로 설정합니다.

## 1. 샘플 임포트

Package Manager > **Truesoft Supabase SDK** > **Samples** 탭에서 **Database Setup**을 Import합니다.  
`Assets/Samples/.../DatabaseSetup/` 폴더에 SQL 파일과 Edge Function 소스가 생성됩니다.

## 2. DB 스키마 실행

`SQL/player/` 폴더의 파일을 **번호 순서대로** Supabase SQL Editor에서 실행합니다.

::: tip
대시보드 어느 화면에서나 **SQL Editor** 버튼 또는 `Ctrl+E`로 열 수 있습니다.
:::

| 순서 | 파일 | 내용 |
|------|------|------|
| 1 | `01_servers.sql` | 게임 서버 목록 |
| 2 | `02_profiles.sql` | 플레이어 프로필·닉네임 |
| 3 | `03_anonymous_recovery.sql` | 익명 계정 복구 |
| 4 | `04_user_data.sql` | 유저 데이터 |
| 5 | `05_account_management.sql` | 서버 이주·탈퇴 |
| 6 | `06_mails.sql` | 우편함 |
| 7 | `07_purchases.sql` | 인앱 결제 (IAP 사용 시) |
| 8 | `08_remote_config.sql` | Remote Config |
| 9 | `09_cron_jobs.sql` | 자동화 크론 잡 |
| 10 | `10_bans.sql` | 계정 차단 메시지 |
| 11 | `11_user_data_logs.sql` | 유저 데이터 변경 로그 |

::: tip
`99_verify.sql`을 마지막에 실행하면 스키마 설치 여부를 확인할 수 있습니다.
:::

## 3. 엣지 함수 배포 {#edge-function-deploy}

아래 과정을 각 함수마다 반복합니다.

1. Supabase 대시보드 > **Edge Functions** > **Deploy a new function** > **Via Editor** 클릭
2. 함수 이름을 정확히 입력하고 생성
3. Unity Project 창에서 `Assets/Samples/.../DatabaseSetup/EdgeFunctions/<함수명>/index.ts`를 열어 전체 내용 복사
4. 에디터에 붙여넣고 **Deploy** 클릭

| 함수 이름 | 필요 기능 |
|-----------|----------|
| `displayname-get` | 공개 프로필 — 닉네임 조회 |
| `displayname-set` | 공개 프로필 — 닉네임 설정 |
| `withdrawal-cancel-issue` | 공개 프로필 — 탈퇴 취소 토큰 발급 |
| `withdrawal-cancel-redeem` | 공개 프로필 — 탈퇴 취소 토큰 사용 |
| `withdrawal-guard` | 공개 프로필 — 탈퇴 계정 자동 처리 |
| `purchase-verify-google` | 인앱 결제 — Android |
| `purchase-verify-apple` | 인앱 결제 — iOS (SK2 / Unity IAP v5) |
| `purchase-verify-apple-legacy` | 인앱 결제 — iOS (SK1 / Unity IAP v4, 또는 SK1 강제 모드) |
| `get-ban-info` | 인증 — 차단된 계정 정보 조회 |

## 4. 시크릿 설정

대시보드 **Edge Functions > Secrets**에 등록합니다.

| 시크릿 키 | 필수 | 용도 |
|----------|----|------|
| `CANCEL_TOKEN_SECRET` | 탈퇴 취소 사용 시 | 탈퇴 취소 토큰 서명·검증에 사용하는 비밀 키. 랜덤 문자열 32자 이상 |
| `GOOGLE_SERVICE_ACCOUNT_JSON` | Android IAP 사용 시 | Google Play 결제 영수증 서버 검증에 사용하는 서비스 계정 키 |
| `APPLE_SHARED_SECRET` | iOS IAP 사용 시 (SK1) | App Store Connect > 앱 정보 > 공유 암호. `purchase-verify-apple-legacy` 함수에서 사용 |

발급 절차는 [Google 서비스 계정 JSON 발급](/guide/google-service-account/issue)을 참고하세요.

---
