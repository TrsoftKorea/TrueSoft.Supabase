# 서비스 연결값

[운영 콘솔에서 만든 프로젝트](/guide/start/project)는 DB와 서버 함수가 미리 설치돼 있어, 게임이 쓰는 스토어·로그인의 키만 넣으면 됩니다.

## 연결값 입력 {#secrets}

스토어·애플 키는 운영 콘솔 **설정 › 서비스 연결**에서 넣습니다. Supabase 대시보드에 들어가지 않아도 되고, 저장하면 1분 안에 적용됩니다. 각 항목의 **가져오는 방법**을 누르면 발급처 화면에서 값을 찾는 단계가 나옵니다.

묶음마다 **사용** 스위치가 있습니다. 쓰는 스토어·로그인만 켜면 반드시 넣어야 할 값에 빨간 `*`이 붙고, 쓰지 않는 것은 접혀서 보이지 않습니다. 스위치는 화면 표시만 바꿉니다 — 꺼도 넣어 둔 값은 지워지지 않고 서버도 그대로 그 값을 씁니다.

| 콘솔&nbsp;위치 | 키&nbsp;이름 | 필수 | 용도 |
|------|------|----|------|
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;번들&nbsp;ID | `APPLE_BUNDLE_ID` | iOS&nbsp;IAP·Apple&nbsp;로그인&nbsp;사용&nbsp;시 | iOS 앱의 번들 ID. 여럿이면 쉼표로 구분. 없으면 iOS 결제 검증이 전부 거부됩니다. 다른 앱의 영수증을 걸러 내는 기준이라 비워 둘 수 없습니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;공유&nbsp;암호 | `APPLE_SHARED_SECRET` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시 | `purchase-verify-apple-legacy` 함수에서 사용. iOS 14 이하 지원이나 플레이나누 병행처럼 SK1을 쓰면 필수이며, 없으면 결제 후 검증이 `server_config_error`로 실패합니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;내&nbsp;구입&nbsp;키&nbsp;›&nbsp;Issuer&nbsp;ID | `APPLE_IAP_ISSUER_ID` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시&nbsp;권장 | SK1 영수증에는 가격이 없어, 앱 내 구입 키 셋이 있어야 애플에 실제 결제 금액을 물어 매출에 넣습니다. 없으면 SK1 결제는 금액 없이 기록됩니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;내&nbsp;구입&nbsp;키&nbsp;›&nbsp;Key&nbsp;ID | `APPLE_IAP_KEY_ID` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시&nbsp;권장 | 앱 내 구입 키의 키 ID. 10자. Sign in with Apple 키와 다른 키입니다 |
| 스토어&nbsp;›&nbsp;App&nbsp;Store&nbsp;›&nbsp;앱&nbsp;내&nbsp;구입&nbsp;키&nbsp;›&nbsp;Private&nbsp;key | `APPLE_IAP_PRIVATE_KEY` | iOS&nbsp;IAP·SK1&nbsp;사용&nbsp;시&nbsp;권장 | 그 키의 `.p8` 파일 내용 전체. `-----BEGIN PRIVATE KEY-----` 줄까지 포함 |
| 스토어&nbsp;›&nbsp;Google&nbsp;Play&nbsp;›&nbsp;패키지&nbsp;이름 | `GOOGLE_PACKAGE_NAME` | Android&nbsp;IAP&nbsp;사용&nbsp;시 | Android 앱의 패키지 이름. 여럿이면 쉼표로 구분. 여기 없는 앱의 결제는 `package_name_mismatch`로 거부되고 기록되지 않습니다. 비워 두면 Android 결제 검증이 전부 `server_config_error`로 실패합니다. 시험용 앱의 결제가 매출에 섞이지 않게 거르는 기준입니다 |
| 스토어&nbsp;›&nbsp;Google&nbsp;Play&nbsp;›&nbsp;서비스&nbsp;계정&nbsp;키 | `GOOGLE_SERVICE_ACCOUNT_JSON` | Android&nbsp;IAP&nbsp;사용&nbsp;시 | Google Play 결제 영수증 서버 검증에 사용하는 서비스 계정 키. 내려받은 JSON 파일 내용 전체 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Team&nbsp;ID | `APPLE_TEAM_ID` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | Apple Developer 팀 ID. 10자 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Key&nbsp;ID | `APPLE_KEY_ID` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | Sign in with Apple 키의 Key ID. 10자 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Private&nbsp;key | `APPLE_PRIVATE_KEY` | Apple&nbsp;로그인&nbsp;사용&nbsp;시 | 그 키의 `.p8` 파일 내용 전체. `-----BEGIN PRIVATE KEY-----` 줄까지 포함 |
| 로그인&nbsp;›&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple&nbsp;›&nbsp;Services&nbsp;ID | `APPLE_SERVICES_ID` | 선택 | 게임 애플 로그인에 쓰는 Services ID. 넣으면 [게임 밖 고객센터](/guide/customer-center/#web)에 애플 로그인 단추가 보입니다 |
| 로그인&nbsp;›&nbsp;Google&nbsp;로그인&nbsp;›&nbsp;클라이언트&nbsp;ID | `GOOGLE_WEB_CLIENT_ID` | 선택 | 게임 구글 로그인에 쓰는 웹 애플리케이션 클라이언트 ID. 넣으면 게임 밖 고객센터에 구글 로그인 단추가 보입니다 |

Google 서비스 계정 키는 [Google 서비스 계정 JSON 발급](/guide/google-service-account/issue)도 참고하세요.

::: info 탈퇴 취소용 비밀값은 넣지 않습니다
탈퇴 취소 토큰에 서명하는 비밀값은 프로젝트를 준비할 때 DB 안에 무작위로 만들어집니다. 따로 등록할 것이 없습니다.
:::

::: details 예전에 Supabase Secrets에 넣은 값
콘솔에 값이 없으면 서버 함수는 Supabase 대시보드 **Edge Functions > Secrets**에서 위 표의 키 이름과 같은 값을 씁니다. 콘솔 화면에는 "콘솔 밖에서 넣은 값 사용 중"으로 보입니다. 콘솔에 넣으면 그 값이 먼저 쓰이고, 콘솔에서 지우면 다시 Secrets 값을 씁니다.

Secrets에 `CANCEL_TOKEN_SECRET`이 있으면 DB가 만든 비밀값보다 그 값이 먼저 쓰입니다. 지우면 이미 발급된 탈퇴 취소 토큰이 무효가 되니 그대로 두세요.
:::
