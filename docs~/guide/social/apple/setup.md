# 대시보드 설정

Apple 로그인([신규 로그인](./signin))을 쓰기 전 준비입니다. 각 설정의 이유·자주 나는 오류는 [Apple 설정](./setup-details)을 참고하세요.

## Apple Developer

- App ID에 **Sign in with Apple**을 활성화합니다.

## Supabase

**Authentication > Providers > Apple** 패널에서 설정합니다.

| 필드 | 입력 |
|------|------|
| **Enable&nbsp;Sign&nbsp;in&nbsp;with&nbsp;Apple** | ON |
| **Client&nbsp;IDs** | 앱 **Bundle ID** |
| **Allow&nbsp;users&nbsp;without&nbsp;an&nbsp;email** | Apple이 이메일 주소를 반환하지 않아도 로그인을 허용합니다(선택) |

게스트 연동을 쓰면 **Authentication > Sign In / Providers**에서 **Manual Linking**을 활성화합니다.

## Android 추가

Android에서 Apple 로그인을 쓰면 위 설정에 더해 아래를 추가합니다.

**Apple Developer**

- **Identifiers > Services IDs**에서 Services ID를 만들고 Sign in with Apple을 구성합니다.
  - **Identifier**: `번들ID.Services` 형식 (예: `com.company.mygame.Services`)
- 구성 화면(**Web Authentication Configuration**)에서 Primary App ID를 선택하고 등록합니다.
  - **Domains and Subdomains**: `<프로젝트-ref>.supabase.co`
  - **Return URLs**: `https://<프로젝트-ref>.supabase.co/auth/v1/callback`
- **Sign in with Apple Key(.p8)**를 발급해 둡니다.

**Supabase · Providers > Apple**

- **Client IDs**: 위 Bundle ID 앞에 **Services ID를 추가**합니다(Services ID가 맨 앞). 예: `com.company.mygame.Services, com.company.mygame` ([순서 이유](./setup-details#client-ids))
- **Secret Key (for OAuth)**: [client_secret(JWT)을 생성](https://supabase.com/docs/guides/auth/social-login/auth-apple#generate-a-client_secret)해 넣습니다.

**Supabase · URL Configuration**

- **Redirect URLs**에 `{패키지이름}://login-callback`을 추가합니다. 패키지 이름은 Unity **Player Settings > Other Settings > Package Name** 값입니다.
