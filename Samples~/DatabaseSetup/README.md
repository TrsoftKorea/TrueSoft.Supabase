# Database Setup

초기 Supabase 프로젝트 설정에 필요한 SQL 스크립트와 Edge Function 소스 파일입니다.  
설정 완료 후 이 폴더를 삭제해도 됩니다.

---

## SQL 실행

Supabase SQL Editor에 `SQL/player/install.sql` 전체를 붙여넣고 한 번 실행합니다. 게임 서버·프로필·유저 데이터·우편함·인앱 결제·원격 설정·리더보드·쿠폰과 운영 도구용 테이블이 모두 만들어집니다.

`verify.sql`은 설치 완료 후 정상 여부를 확인하는 쿼리입니다.

파일 안의 절 순서를 바꾸거나 일부만 실행하지 마세요. 마지막 절이 모든 테이블·함수 권한을 회수한 뒤 필요한 것만 되돌려 주는데, 앞 절에서 만든 함수를 이름으로 지정하기 때문입니다. 모든 구문이 멱등이라 다시 실행해도 안전합니다.

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
