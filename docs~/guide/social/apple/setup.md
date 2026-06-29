# 대시보드·빌드 설정

Apple 로그인([신규 로그인](./signin))을 쓰기 전 준비입니다. 커스텀 ID 토큰 방식만 쓴다면 필요 없습니다.

## Apple Developer

- App ID에 **Sign in with Apple**을 활성화합니다(iOS 네이티브용). 이 App ID의 Bundle ID가 iOS 네이티브 토큰의 `aud`입니다.

브라우저 로그인(Android)을 쓰면 추가로:

- **Identifiers > Services IDs**에서 Services ID(역도메인, 예: `com.company.mygame.signin`)를 만들어 위 App ID에 연결하고 Sign in with Apple을 구성합니다. 이 Services ID가 브라우저 토큰의 `aud`이자 OAuth client_id입니다.
- Services ID의 **Return URL**을 Supabase 콜백으로 지정합니다: `https://<프로젝트-ref>.supabase.co/auth/v1/callback`.
- **Sign in with Apple Key(.p8)**를 발급합니다. Key ID·Team ID와 함께 Supabase Secret 생성에 사용합니다.

::: warning Return URL ≠ 앱 딥링크
Apple Services ID의 **Return URL**은 Supabase 콜백(`.../auth/v1/callback`)입니다. 앱 딥링크(`{패키지이름}://login-callback`)는 Supabase **Redirect URLs**에 넣는 별개 값입니다.
:::

## Supabase 대시보드

1. **Authentication > Providers > Apple**을 활성화합니다.
2. **Client IDs**에 토큰의 audience를 등록합니다 — iOS 네이티브는 앱 Bundle ID, 브라우저·PlayNANOO는 Services ID.
3. 브라우저 로그인을 쓰면 Apple Developer에서 발급한 **Services ID·Secret Key**를 등록합니다.
4. **Authentication > URL Configuration > Redirect URLs**에 `{패키지이름}://login-callback`을 추가합니다. 패키지 이름은 Unity **Player Settings > Other Settings > Package Name** 값입니다. 예: `com.company.mygame` → `com.company.mygame://login-callback`.
5. 게스트 연동을 쓰면 **Authentication > Settings > Manual linking**을 ON으로 둡니다.

::: tip
3·4번은 Android 브라우저 로그인에만 필요합니다. iOS만 지원하면 생략합니다.
:::

## Unity 빌드

빌드 시 SDK가 자동으로 처리하므로 별도 작업이 없습니다.

- **iOS**: Xcode에 **Sign in with Apple** Capability를 자동 추가합니다.
- **Android**: 딥링크와 AndroidManifest를 자동 처리합니다.

::: info 플랫폼별 동작
[`TrySignInWithAppleAsync`](./signin)가 iOS는 네이티브, Android는 브라우저로 자동 분기합니다. 에디터에서는 동작하지 않으니 실기기 빌드에서 테스트하세요.
:::
