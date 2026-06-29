# 대시보드·빌드 설정

Apple 로그인([신규 로그인](./signin))을 쓰기 전 준비입니다. 커스텀 ID 토큰 방식만 쓴다면 필요 없습니다.

## Apple Developer

- App ID에 **Sign in with Apple**을 활성화합니다. 이 App ID의 Bundle ID가 iOS 토큰의 `aud`입니다.

Android를 지원하면 추가로:

- **Identifiers > Services IDs**에서 Services ID를 만들고 Sign in with Apple을 구성합니다. 식별자는 `번들ID.Services` 형식으로 통일합니다(예: `com.company.mygame.Services`). 이 Services ID가 Android 토큰의 `aud`입니다.
- 구성 화면(**Web Authentication Configuration**)에서 Primary App ID를 선택하고 아래를 등록합니다.
  - **Domains and Subdomains**: `<프로젝트-ref>.supabase.co`
  - **Return URLs**: `https://<프로젝트-ref>.supabase.co/auth/v1/callback`
- **Sign in with Apple Key(.p8)**를 발급합니다([client_secret 생성 가이드](https://supabase.com/docs/guides/auth/social-login/auth-apple#generate-a-client_secret) 참고).

::: warning Return URLs ≠ 앱 딥링크
Apple Services ID의 **Return URLs**는 Supabase 콜백(`https://<프로젝트-ref>.supabase.co/auth/v1/callback`)입니다. 앱 딥링크(`{패키지이름}://login-callback`)는 Supabase **Redirect URLs**에 넣는 별개 값입니다.
:::

## Supabase · Authentication > Providers > Apple

| 필드 | 입력 |
|------|------|
| **Enable&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple** | ON |
| **Client&nbsp;IDs** | 쉼표로 구분한 목록. **Services ID(`번들ID.Services`)를 맨 앞**에 두고, iOS용 앱 **Bundle ID**를 뒤에 둡니다. 예: `com.company.mygame.Services, com.company.mygame` |
| **Secret&nbsp;Key&nbsp;(for&nbsp;OAuth)** | Android에만 필요. Apple `.p8` 키로 [생성한 client_secret](https://supabase.com/docs/guides/auth/social-login/auth-apple#generate-a-client_secret). iOS만 지원하면 비워둡니다 |
| **Callback&nbsp;URL&nbsp;(for&nbsp;OAuth)** | Supabase가 표시하는 값. Apple Services ID의 Return URLs에 등록할 때 이 값을 복사합니다 |
| **Allow&nbsp;users&nbsp;without&nbsp;an&nbsp;email** | Apple '이메일 숨김' 사용자를 받으려면 ON(선택) |

::: warning Client IDs 순서가 중요합니다
Supabase는 Android(웹) OAuth의 client_id로 **목록의 첫 값**을 사용합니다. 그래서 **Services ID가 맨 앞**이어야 합니다. 번들 ID가 첫 값이면 Apple이 `invalid_request: Invalid client id or web redirect url`로 거부합니다. 한편 iOS 네이티브는 토큰의 `aud`(=번들 ID)가 **목록에 포함**되어 있으면 되므로, 번들 ID는 뒤에 두면 됩니다.
:::

::: warning Secret Key 6개월 만료
Apple OAuth secret key는 6개월마다 만료됩니다. 만료 전에 새로 생성해 교체하지 않으면 Android 로그인이 실패합니다.
:::

## Supabase · 그 외 설정

- **Authentication > URL Configuration > Redirect URLs**에 `{패키지이름}://login-callback`을 추가합니다(Android). 패키지 이름은 Unity **Player Settings > Other Settings > Package Name** 값입니다.
- 게스트 연동을 쓰면 **Authentication > Sign In / Providers**에서 **Manual Linking**을 활성화합니다.

::: tip
iOS만 지원하면 **Enable**과 **Client IDs**(Bundle ID)만 채우면 됩니다. Secret Key·Services ID·Callback URL·Redirect URL은 Android용입니다.
:::

## Unity 빌드

빌드 관련 설정은 SDK가 자동으로 처리하므로 iOS·Android 모두 추가 작업이 없습니다.

::: info
[`TrySignInWithAppleAsync`](./signin) 하나로 iOS·Android 모두 동작합니다. 에디터에서는 동작하지 않으니 실기기 빌드에서 테스트하세요.
:::
