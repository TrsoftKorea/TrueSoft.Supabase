# 대시보드·빌드 설정

Apple 로그인([신규 로그인](./signin))을 쓰기 전 준비입니다. 커스텀 ID 토큰 방식만 쓴다면 이 단계는 필요 없습니다.

## Supabase 대시보드

한 번의 대시보드 방문에서 아래를 함께 처리합니다.

1. Apple Developer의 App ID(Android도 쓰면 Services ID)에 **Sign in with Apple**을 활성화합니다.
2. **Authentication > Providers > Apple**을 활성화하고 Service ID·키를 등록합니다.
3. **Authentication > URL Configuration > Redirect URLs**에 `{패키지이름}://login-callback`을 추가합니다. 패키지 이름은 Unity **Player Settings > Other Settings > Package Name** 값입니다. 예: `com.company.mygame` → `com.company.mygame://login-callback`.
4. 게스트 연동을 쓰면 **Authentication > Settings > Manual linking**을 ON으로 둡니다.

::: tip
3번 Redirect URL은 **Android 브라우저 로그인에만** 필요합니다. iOS만 지원한다면 생략해도 됩니다. 이미 Apple 설정을 위해 대시보드에 들어와 있으니, 이때 같이 추가해 두면 따로 다시 들를 필요가 없습니다.
:::

## Unity 빌드

빌드 시 SDK가 자동으로 처리하므로 별도 작업이 없습니다.

- **iOS**: Xcode 프로젝트에 **Sign in with Apple** Capability를 자동 추가합니다.
- **Android**: 딥링크와 AndroidManifest 설정을 자동 처리합니다.

::: info 플랫폼별 동작
[`TrySignInWithAppleAsync`](./signin)가 iOS는 네이티브, Android는 브라우저로 자동 분기합니다. 에디터에서는 동작하지 않으니 실기기 빌드에서 테스트하세요.
:::
