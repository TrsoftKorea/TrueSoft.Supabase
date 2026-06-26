# 대시보드·빌드 설정

iOS 네이티브 Apple 로그인을 쓰기 전 준비입니다. 커스텀 ID 토큰 방식만 쓴다면 이 단계는 필요 없습니다.

## Apple Developer · Supabase 설정

- Apple Developer의 App ID에 **Sign in with Apple** 기능을 활성화합니다.
- Supabase 대시보드 **Authentication > Providers > Apple** 을 활성화하고 Service ID·키를 등록합니다.
- 게스트 연동을 쓰면 **Authentication > Settings > Manual linking** 을 ON으로 둡니다.

## Unity 빌드

- iOS 빌드 시 SDK가 Xcode 프로젝트에 **Sign in with Apple** Capability(entitlement)를 자동으로 추가합니다(에디터 빌드 후처리).
- 네이티브 코드는 SDK에 포함되어 Xcode에서 함께 컴파일되므로 별도 패키지·플러그인 임포트가 필요 없습니다.

::: warning
네이티브 로그인은 iOS에서만 동작합니다. 에디터·Android에서는 `apple_login_ios_only`가 반환됩니다. Android에서 Apple 로그인이 필요하면 웹 OAuth로 받은 토큰을 [신규 로그인 · 커스텀](./signin-token)에 전달하세요.
:::
