# Google

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

먼저 [대시보드 설정](./setup)을 완료한 뒤, 상황과 플랫폼에 맞는 메서드를 사용하세요. Android는 Play Services 네이티브 로그인을, iOS는 외부 SDK(커스텀 OAuth 포함)로 발급받은 ID 토큰을 직접 전달하는 방식을 씁니다.

| 상황 | Android | iOS |
|------|---------|-----|
| 신규&nbsp;로그인 | [네이티브 로그인](./signin-android) | [ID 토큰 로그인](./signin-ios) |
| 게스트(익명)&nbsp;→&nbsp;연동 | [네이티브 연동](./link-android) | [ID 토큰 연동](./link-ios) |
| 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | [네이티브 추가 연동](./add-android) | [ID 토큰 추가 연동](./add-ios) |

플랫폼 구분 없이 [Google 연동 해제](./unlink)로 현재 계정에서 Google 연동을 제거할 수 있습니다.
