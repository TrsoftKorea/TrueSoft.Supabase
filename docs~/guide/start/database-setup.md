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
| 18~21 | 채팅 · 매치 결과 · 친구 · 매치 로비 |
| 22 | 클라이언트 권한 최소화 |

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
| `withdrawal-sweep` | 공개 프로필 · 탈퇴 유예가 지난 계정을 자정 직후 삭제 |
| `withdrawal-now` | 공개 프로필 · 즉시 탈퇴 |
| `purchase-verify-google` | 인앱 결제 · Android |
| `purchase-verify-apple` | 인앱 결제 · iOS · SK2 |
| `purchase-verify-apple-legacy` | 인앱 결제 · iOS · SK1 · forceStoreKit1 |
| `purchase-notify-apple` | 인앱 결제 · iOS · 환불 기록 |
| `purchase-refund-sync-google` | 인앱 결제 · Android · 환불 기록 |
| `purchase-price-sync` | 인앱 결제 · 원화 환산용 환율표 저장 · 빈 결제 금액 채우기 |
| `apple-token` | Apple 로그인 · 탈퇴 시 Apple 연결 해제 |
| `get-ban-info` | 인증 · 차단된 계정 정보 조회 |

::: warning 네 함수는 Verify JWT를 끕니다
`purchase-notify-apple`은 애플 서버가, `purchase-refund-sync-google`·`purchase-price-sync`·`withdrawal-sweep`은 DB 정기 작업이 로그인 없이 부릅니다. 네 함수는 배포 후 함수 설정에서 **Verify JWT**를 끄세요. 켜 두면 환불이 기록되지 않고, 비어 있는 원화 금액과 옛 방식 iOS 결제 금액이 채워지지 않으며, 탈퇴 유예가 지난 계정이 지워지지 않습니다.
:::

## 4. 서비스 연결값 입력 {#secrets}

스토어·애플 키는 운영 콘솔 **설정 › 서비스 연결**에서 넣습니다. Supabase 대시보드에 들어가지 않아도 되고, 저장하면 1분 안에 적용됩니다. 각 항목의 **가져오는 방법**을 누르면 발급처 화면에서 값을 찾는 단계가 나옵니다.

묶음마다 **사용** 스위치가 있습니다. 쓰는 스토어·로그인만 켜면 반드시 넣어야 할 값에 빨간 `*`이 붙고, 쓰지 않는 것은 접혀서 보이지 않습니다. 스위치는 화면 표시만 바꿉니다 — 꺼도 넣어 둔 값은 지워지지 않고 서버도 그대로 그 값을 씁니다.

| 콘솔&nbsp;위치 | 키&nbsp;이름 | 필수 | 용도 |
|------|------|----|------|
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;번들&nbsp;ID | `APPLE_BUNDLE_ID` | iOS&nbsp;IAP·Apple&nbsp;로그인&nbsp;사용&nbsp;시 | iOS 앱의 번들 ID. 여럿이면 쉼표로 구분. 없으면 iOS 결제 검증이 전부 거부됩니다 — 다른 앱의 영수증을 걸러 내는 기준이라 비워 둘 수 없습니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;공유&nbsp;암호 | `APPLE_SHARED_SECRET` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시 | `purchase-verify-apple-legacy` 함수에서 사용. iOS 14 이하 지원이나 플레이나누 병행처럼 SK1을 쓰면 필수이며, 없으면 결제 후 검증이 `server_config_error`로 실패합니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;내&nbsp;구입&nbsp;키&nbsp;›&nbsp;Issuer&nbsp;ID | `APPLE_IAP_ISSUER_ID` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시&nbsp;권장 | SK1 영수증에는 가격이 없어, 앱 내 구입 키 셋이 있어야 애플에 실제 결제 금액을 물어 매출에 넣습니다. 없으면 SK1 결제는 금액 없이 기록됩니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;내&nbsp;구입&nbsp;키&nbsp;›&nbsp;Key&nbsp;ID | `APPLE_IAP_KEY_ID` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시&nbsp;권장 | 앱 내 구입 키의 키 ID. 10자. Sign in with Apple 키와 다른 키입니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;내&nbsp;구입&nbsp;키&nbsp;›&nbsp;Private&nbsp;key | `APPLE_IAP_PRIVATE_KEY` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시&nbsp;권장 | 그 키의 `.p8` 파일 내용 전체. `-----BEGIN PRIVATE KEY-----` 줄까지 포함 |
| 스토어&nbsp;›&nbsp;Google&nbsp;Play&nbsp;›&nbsp;패키지&nbsp;이름 | `GOOGLE_PACKAGE_NAME` | Android&nbsp;IAP&nbsp;사용&nbsp;시 | Android 앱의 패키지 이름. 여럿이면 쉼표로 구분. 여기 없는 앱의 결제는 `package_name_mismatch`로 거부되고 기록되지 않습니다. 비워 두면 Android 결제 검증이 전부 `server_config_error`로 실패합니다 — 시험용 앱의 결제가 매출에 섞이지 않게 거르는 기준입니다 |
| 스토어&nbsp;›&nbsp;Google&nbsp;Play&nbsp;›&nbsp;서비스&nbsp;계정&nbsp;키 | `GOOGLE_SERVICE_ACCOUNT_JSON` | Android&nbsp;IAP&nbsp;사용&nbsp;시 | Google Play 결제 영수증 서버 검증에 사용하는 서비스 계정 키. 내려받은 JSON 파일 내용 전체 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Team&nbsp;ID | `APPLE_TEAM_ID` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | Apple Developer 팀 ID. 10자 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Key&nbsp;ID | `APPLE_KEY_ID` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | Sign in with Apple 키의 Key ID. 10자 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Private&nbsp;key | `APPLE_PRIVATE_KEY` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | 그 키의 `.p8` 파일 내용 전체. `-----BEGIN PRIVATE KEY-----` 줄까지 포함 |

Google 서비스 계정 키는 [Google 서비스 계정 JSON 발급](/guide/google-service-account/issue)도 참고하세요.

::: info 탈퇴 취소용 비밀값은 넣지 않습니다
탈퇴 취소 토큰에 서명하는 비밀값은 `install.sql`을 실행할 때 DB 안에 무작위로 만들어집니다. 따로 등록할 것이 없습니다.
:::

::: details 예전에 Supabase Secrets에 넣은 값
콘솔에 값이 없으면 서버 함수는 Supabase 대시보드 **Edge Functions > Secrets**에서 위 표의 키 이름과 같은 값을 씁니다. 콘솔 화면에는 "콘솔 밖에서 넣은 값 사용 중"으로 보입니다. 콘솔에 넣으면 그 값이 먼저 쓰이고, 콘솔에서 지우면 다시 Secrets 값을 씁니다.

Secrets에 `CANCEL_TOKEN_SECRET`이 있으면 DB가 만든 비밀값보다 그 값이 먼저 쓰입니다. 지우면 이미 발급된 탈퇴 취소 토큰이 무효가 되니 그대로 두세요.
:::

## 5. 프로젝트 주소 등록 {#project-url}

DB가 엣지 함수를 직접 부르는 자동 작업이 셋 있습니다 — 탈퇴 유예가 지난 계정 삭제(`withdrawal-sweep`), 구글 환불 확인(`purchase-refund-sync-google`), 환율표 저장과 빈 결제 금액 채우기(`purchase-price-sync`). 그러려면 DB가 이 프로젝트의 주소를 알아야 합니다. 구글 결제를 쓰지 않아도 탈퇴 계정 삭제 때문에 **반드시** 등록합니다. SQL Editor에서 한 번 실행하세요.

```sql
select vault.create_secret('https://<project-ref>.supabase.co', 'project_url');
```

`<project-ref>`는 대시보드 주소창의 `/project/` 뒤에 오는 문자열입니다. 등록하지 않으면 탈퇴 유예가 지난 계정이 지워지지 않고, 운영 콘솔 **자동 작업** 화면과 구매 내역에 실패로 표시됩니다.
