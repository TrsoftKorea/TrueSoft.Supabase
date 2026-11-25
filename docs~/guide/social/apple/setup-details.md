# Apple 설정 상세

[대시보드 설정](/guide/social/apple/setup)의 각 값이 왜 그런지와 자주 나는 오류입니다. 처음엔 설정을 그대로 적용하고, 동작이 궁금할 때 읽으세요.

## Client IDs 순서 {#client-ids}

Supabase는 Android 웹 OAuth의 client_id로 로그인 설정 **Client IDs 목록의 첫 값**을 사용합니다. 운영 콘솔이 Services ID를 맨 앞에, Bundle ID를 그 뒤에 넣습니다.

- 번들 ID가 첫 값이면 Apple이 `invalid_request: Invalid client id or web redirect url`로 거부합니다. 번들 ID에는 웹 redirect 설정이 없기 때문입니다.
- iOS 네이티브는 토큰의 `aud`, 즉 번들 ID가 **목록에 포함**되어 있으면 검증을 통과하므로, 번들 ID는 뒤에 둬도 됩니다.

## Return URLs와 돌아올 주소 {#redirects}

이름이 비슷한 리다이렉트가 두 군데라 헷갈리기 쉽습니다. 서로 다른 값입니다.

| 설정 위치 | 값 | 흐름 |
|----------|-----|------|
| Apple&nbsp;Services&nbsp;ID&nbsp;**Return&nbsp;URLs** | `https://<프로젝트-ref>.supabase.co/auth/v1/callback` | Apple → Supabase |
| 운영&nbsp;콘솔이&nbsp;넣는&nbsp;돌아올&nbsp;주소 | `{패키지이름}://login-callback` | Supabase → 앱 |

## aud와 Client IDs {#aud}

`aud`는 audience의 약자로, 토큰이 발급된 client_id입니다. 로그인 설정의 **Client IDs**는 허용할 `aud` 목록입니다. iOS 네이티브 토큰의 `aud`는 앱 **Bundle ID**, Android 웹 토큰의 `aud`는 **Services ID**입니다.

## Secret Key 갱신 {#secret}

Apple OAuth secret key는 6개월마다 만료됩니다. 운영 콘솔에 Sign in with Apple 키를 넣어 두면 서버가 매주 새로 만들어 넣으므로 따로 바꿀 일이 없습니다. 키를 지우거나 Apple에서 폐기하면 다음 만료 때 Android 로그인이 실패합니다.
