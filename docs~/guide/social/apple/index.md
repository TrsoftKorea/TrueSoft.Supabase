# Apple

::: tip
소셜 로그인은 선택 기능입니다. 익명 로그인만으로도 게임을 운영할 수 있습니다.
:::

먼저 [대시보드·빌드 설정](./setup)을 완료한 뒤, iOS는 네이티브 메서드를 사용하세요. 외부 OAuth·웹에서 받은 ID 토큰을 직접 쓸 땐 커스텀 메서드를 사용합니다.

| 상황 | iOS 네이티브 | 커스텀 |
|------|--------------|--------|
| 신규&nbsp;로그인 | [네이티브 로그인](./signin) | [ID 토큰 로그인](./signin-token) |
| 게스트(익명)&nbsp;→&nbsp;연동 | [네이티브 연동](./link) | [ID 토큰 연동](./link-token) |
| 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | [네이티브 추가 연동](./add) | [ID 토큰 추가 연동](./add-token) |

Android에서는 네이티브 Sign in with Apple을 쓸 수 없으므로 [브라우저 로그인](./signin-android)(Supabase 호스팅 OAuth + 딥링크)을 사용하세요.

플랫폼 구분 없이 [Apple 연동 해제](./unlink)로 현재 계정에서 Apple 연동을 제거할 수 있습니다.
