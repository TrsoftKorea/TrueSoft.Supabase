# Apple

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

먼저 [대시보드 설정](./setup)을 완료하세요. [신규 로그인](./signin)(`TrySignInWithAppleAsync`)이 **iOS·Android를 자동으로 처리**합니다. 이미 가진 Apple ID 토큰을 직접 쓸 땐 커스텀 메서드를 사용합니다.

| 상황 | 기본 | 커스텀 |
|------|------|--------|
| 신규&nbsp;로그인 | [신규 로그인](./signin) | [ID 토큰 로그인](./signin-token) |
| 게스트(익명)&nbsp;→&nbsp;연동 | [게스트 연동](./link) | [ID 토큰 연동](./link-token) |
| 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | [추가 연동](./add) | [ID 토큰 추가 연동](./add-token) |

::: info Android 게스트·추가 연동
연동은 iOS에서 동작합니다. Android에서 연동하려면 이미 가진 Apple ID 토큰을 커스텀 메서드([게스트 연동 · 커스텀](./link-token)·[추가 연동 · 커스텀](./add-token))에 전달하세요. 신규 로그인은 [신규 로그인](./signin)이 Android도 자동 처리합니다.
:::

플랫폼 구분 없이 [Apple 연동 해제](./unlink)로 현재 계정에서 Apple 연동을 제거할 수 있습니다.
