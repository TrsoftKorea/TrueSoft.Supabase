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
| `purchase-verify-apple` | 인앱 결제 · iOS · SK2 |
| `purchase-verify-apple-legacy` | 인앱 결제 · iOS · SK1 · forceStoreKit1 |
| `apple-token` | Apple 로그인 · 탈퇴 시 Apple 연결 해제 |
| `get-ban-info` | 인증 · 차단된 계정 정보 조회 |

## 4. 시크릿 설정 {#secrets}

대시보드 **Edge Functions > Secrets**에 등록합니다. 여러 개는 [한 번에 붙여넣기](#secrets-paste)로 몰아서 넣을 수 있습니다.

| 시크릿 키 | 필수 | 용도 |
|----------|----|------|
| `CANCEL_TOKEN_SECRET` | 탈퇴&nbsp;취소&nbsp;사용&nbsp;시 | 탈퇴 취소 토큰 서명·검증에 사용하는 비밀 키. 랜덤 문자열 32자 이상 |
| `GOOGLE_SERVICE_ACCOUNT_JSON` | Android&nbsp;IAP&nbsp;사용&nbsp;시 | Google Play 결제 영수증 서버 검증에 사용하는 서비스 계정 키 |
| `APPLE_BUNDLE_ID` | iOS&nbsp;IAP·Apple&nbsp;로그인&nbsp;사용&nbsp;시 | iOS 앱의 번들 ID. 여럿이면 쉼표로 구분. 없으면 iOS 결제 검증이 전부 거부됩니다 — 다른 앱의 영수증을 걸러 내는 기준이라 비워 둘 수 없습니다 |
| `APPLE_SHARED_SECRET` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시 | App Store Connect > 앱 정보 > 앱 전용 공유 암호. `purchase-verify-apple-legacy` 함수에서 사용. iOS 14 이하 지원이나 플레이나누 병행처럼 SK1을 쓰면 필수이며, 없으면 결제 후 검증이 `server_config_error`로 실패합니다 |
| `APPLE_TEAM_ID` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | Apple Developer 팀 ID. 10자 |
| `APPLE_KEY_ID` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | Sign in with Apple 키의 Key ID. 10자 |
| `APPLE_PRIVATE_KEY` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | 그 키의 `.p8` 파일 내용 전체. `-----BEGIN PRIVATE KEY-----` 줄까지 포함 |

발급 절차는 [Google 서비스 계정 JSON 발급](/guide/google-service-account/issue)을 참고하세요.

### 한 번에 붙여넣기 {#secrets-paste}

대시보드 **Edge Functions > Secrets** 화면에는 여러 개를 한꺼번에 붙여넣을 수 있습니다. 아래 목록을 복사해 쓰는 기능의 값을 채운 뒤, 이름 칸에 통째로 붙여넣고 저장합니다. 저장하면 함수를 다시 배포하지 않아도 바로 적용됩니다.

```ini
CANCEL_TOKEN_SECRET=
APPLE_BUNDLE_ID=
APPLE_SHARED_SECRET=
APPLE_TEAM_ID=
APPLE_KEY_ID=
```

붙여넣은 뒤 목록에 이름이 모두 나타났는지 확인하세요. 값은 저장 후 다시 보이지 않습니다.

::: warning 쓰지 않는 줄은 지우세요
값을 비워 둔 줄도 그 이름으로 등록돼, 이미 넣어 둔 값을 빈 값으로 덮을 수 있습니다. 채우지 않은 줄은 지우고 붙여넣으세요.
:::

::: info 여러 줄짜리 값은 따로 넣습니다
`APPLE_PRIVATE_KEY`의 `.p8` 내용과 `GOOGLE_SERVICE_ACCOUNT_JSON`은 여러 줄이라 목록에 섞으면 줄마다 다른 이름으로 잘릴 수 있습니다. 이 둘은 목록에서 빼고 하나씩 등록하세요 — 이름 칸에 이름을, 값 칸에 파일 내용을 그대로 붙여넣으면 됩니다.
:::
