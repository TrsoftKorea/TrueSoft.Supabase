# Google

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

## 메서드 고르기

먼저 [대시보드 설정](./setup)을 완료하세요. [신규 로그인](./signin)(`SignInWithGoogleAsync`)이 **Android·iOS를 모두 처리**합니다. Android는 Play Services 계정 선택 창을, iOS는 기본 로그인 창을 띄웁니다. 다른 SDK로 이미 받은 Google ID 토큰을 쓸 땐 커스텀 메서드를 사용합니다.

| 상황 | 기본 | 커스텀 |
|------|------|--------|
| 신규&nbsp;로그인 | [신규 로그인](./signin) | [ID 토큰 로그인](./signin-token) |
| 게스트(익명)&nbsp;→&nbsp;연동 | [게스트 연동](./link) | [ID 토큰 연동](./link-token) |
| 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | [추가 연동](./add) | [ID 토큰 추가 연동](./add-token) |

플랫폼 구분 없이 [Google 연동 해제](./unlink)로 현재 계정에서 Google 연동을 제거할 수 있습니다.
