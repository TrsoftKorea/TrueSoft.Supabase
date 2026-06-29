# 대시보드·빌드 설정

Apple 로그인([신규 로그인](./signin))을 쓰기 전 준비입니다. 커스텀 ID 토큰 방식만 쓴다면 필요 없습니다. 각 설정의 이유·자주 나는 오류는 [Apple 설정 자세히](./setup-details)에 있습니다.

## Apple Developer

- App ID에 **Sign in with Apple**을 활성화합니다.

Android를 지원하면 추가로:

- **Identifiers > Services IDs**에서 Services ID(`번들ID.Services`, 예: `com.company.mygame.Services`)를 만들고 Sign in with Apple을 구성합니다.
- 구성 화면(**Web Authentication Configuration**)에서 Primary App ID를 선택하고 등록합니다.
  - **Domains and Subdomains**: `<프로젝트-ref>.supabase.co`
  - **Return URLs**: `https://<프로젝트-ref>.supabase.co/auth/v1/callback`
- **Sign in with Apple Key(.p8)**를 발급해 [client_secret을 생성](https://supabase.com/docs/guides/auth/social-login/auth-apple#generate-a-client_secret)합니다.

## Supabase · Apple 설정

**Authentication > Providers > Apple** 패널에서 설정합니다.

| 필드 | 입력 |
|------|------|
| **Enable&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple** | ON |
| **Client&nbsp;IDs** | **Services ID(`번들ID.Services`)를 맨 앞**, iOS용 **Bundle ID**를 뒤에. 예: `com.company.mygame.Services, com.company.mygame` ([순서 이유](./setup-details#client-ids)) |
| **Secret&nbsp;Key&nbsp;(for&nbsp;OAuth)** | Android에만 필요. `.p8` 키로 생성한 client_secret. iOS만 지원하면 비워둡니다 |
| **Callback&nbsp;URL&nbsp;(for&nbsp;OAuth)** | 표시된 값을 Apple Services ID의 Return URLs에 복사합니다 |
| **Allow&nbsp;users&nbsp;without&nbsp;an&nbsp;email** | Apple이 이메일 주소를 반환하지 않아도 로그인을 허용합니다(선택) |

## Supabase · 추가 설정

- **Authentication > URL Configuration > Redirect URLs**에 `{패키지이름}://login-callback`을 추가합니다(Android). 패키지 이름은 Unity **Player Settings > Other Settings > Package Name** 값입니다.
- 게스트 연동을 쓰면 **Authentication > Sign In / Providers**에서 **Manual Linking**을 활성화합니다.

::: tip
iOS만 지원하면 **Enable**과 **Client IDs**(Bundle ID)만 채우면 됩니다.
:::

## Unity 빌드

빌드 관련 설정은 SDK가 자동으로 처리하므로 iOS·Android 모두 추가 작업이 없습니다.

::: info
[`TrySignInWithAppleAsync`](./signin) 하나로 iOS·Android 모두 동작합니다. 에디터에서는 동작하지 않으니 실기기 빌드에서 테스트하세요.
:::
