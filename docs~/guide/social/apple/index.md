# Apple

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

## 메서드 고르기

먼저 [대시보드 설정](./setup)을 완료하세요. [신규 로그인](./signin)(`SignInWithAppleAsync`)이 **iOS·Android를 자동으로 처리**합니다. 이미 가진 Apple ID 토큰을 직접 쓸 땐 커스텀 메서드를 사용합니다.

| 상황 | 기본 | 커스텀 |
|------|------|--------|
| 신규&nbsp;로그인 | [신규 로그인](./signin) | [ID 토큰 로그인](./signin-token) |
| 게스트(익명)&nbsp;→&nbsp;연동 | [게스트 연동](./link) | [ID 토큰 연동](./link-token) |
| 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | [추가 연동](./add) | [ID 토큰 추가 연동](./add-token) |

::: info Android 게스트·추가 연동
연동은 iOS에서 동작합니다. Android에서 연동하려면 이미 가진 Apple ID 토큰을 커스텀 메서드([게스트 연동 · 커스텀](./link-token)·[추가 연동 · 커스텀](./add-token))에 전달하세요. 신규 로그인은 [신규 로그인](./signin)이 Android도 자동 처리합니다.
:::

플랫폼 구분 없이 [Apple 연동 해제](./unlink)로 현재 계정에서 Apple 연동을 제거할 수 있습니다.

## 탈퇴 시 Apple 연결 해제 {#revoke}

Apple은 계정을 삭제할 때 Sign in with Apple 연결도 끊도록 요구합니다. 끊지 않으면 앱 심사에서 거절될 수 있습니다. SDK는 [탈퇴 신청](/guide/withdrawal/)이 성공하는 즉시 연결을 끊고, 유예가 끝나 계정이 실제로 삭제될 때 한 번 더 시도합니다.

연결을 끊으려면 로그인할 때 받은 **일회용 인증 코드**가 서버에 전달돼 있어야 합니다.

| 로그인 방식 | 해야 할 일 |
|------|------|
| [신규 로그인](./signin)·[게스트 연동](./link)·[추가 연동](./add) | 없음. SDK가 자동으로 전달합니다 |
| 커스텀 ID 토큰 메서드 | Apple 로그인 플러그인이 주는 인증 코드를 `authorizationCode`로 함께 넘깁니다 |

서버에는 [시크릿](/guide/start/database-setup#secrets) `APPLE_TEAM_ID`·`APPLE_KEY_ID`·`APPLE_PRIVATE_KEY`·`APPLE_BUNDLE_ID`와 `apple-token` 함수가 있어야 합니다.

::: warning Android 브라우저 로그인은 연결을 끊지 못합니다
Android의 [신규 로그인](./signin)은 브라우저로 Supabase가 대신 로그인하므로 인증 코드가 앱에 오지 않습니다. 플레이나누 창으로 로그인해도 창이 토큰만 넘겨 주므로 같습니다. App Store 심사 대상은 iOS 앱이라 심사에는 영향이 없습니다.
:::
