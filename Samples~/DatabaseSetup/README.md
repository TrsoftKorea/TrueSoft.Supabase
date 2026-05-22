# Database Setup

초기 Supabase 프로젝트 설정에 필요한 SQL 스크립트와 Edge Function 소스 파일입니다.  
설정 완료 후 이 폴더를 삭제해도 됩니다.

---

## SQL 실행 순서

Supabase SQL Editor에서 `SQL/player/` 폴더의 파일을 **번호 순서대로** 실행합니다.

| 파일 | 내용 |
|------|------|
| 01_servers.sql | 게임 서버 마스터 |
| 02_profiles.sql | 공개 프로필 |
| 03_anonymous_recovery.sql | 익명 계정 복구 |
| 04_user_data.sql | 유저 데이터 |
| 05_account_management.sql | 계정 관리 |
| 06_mails.sql | 우편함 |
| 07_purchases.sql | 인앱 결제 |
| 08_remote_config.sql | Remote Config |
| 09_cron_jobs.sql | 자동화 작업 |
| 10_analytics.sql | 애널리틱스 세션·이벤트 (선택) |
| 11_product_catalog.sql | 인앱 상품 카탈로그 (Retool 관리용, 선택) |

`99_verify.sql`은 설치 완료 후 정상 여부를 확인하는 쿼리입니다.

---

## Edge Function 배포

아래 과정을 각 함수마다 반복합니다.

1. Supabase 대시보드 > **Edge Functions** > **Deploy a new function** 클릭
2. 함수 이름을 정확히 입력하고 생성
3. `EdgeFunctions/<함수명>/index.ts` 파일을 열어 전체 내용 복사
4. 에디터에 붙여넣고 **Deploy** 클릭

| 함수 이름 | 관련 기능 |
|-----------|----------|
| `displayname-get` | 공개 프로필 — 닉네임 조회 |
| `displayname-set` | 공개 프로필 — 닉네임 설정 |
| `withdrawal-cancel-issue` | 공개 프로필 — 탈퇴 취소 토큰 발급 |
| `withdrawal-cancel-redeem` | 공개 프로필 — 탈퇴 취소 토큰 사용 |
| `withdrawal-guard` | 공개 프로필 — 탈퇴 완료 계정 정리 |
| `purchase-verify-google` | 인앱 결제 — Google Play 영수증 검증 |
| `purchase-verify-apple` | 인앱 결제 — App Store 영수증 검증 |
