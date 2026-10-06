# 로그인

## 로그인 호출 {#login-call}

로그인 API는 이관 전·중·후 동일합니다. 자세한 내용은 [인증](/guide/auth/anonymous)을 참고하세요.

플레이나누 로그인이 성공하면 아래 프로퍼티를 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `TrueBaseNanoo.UserId` | 플레이나누 uuid. 로그인 전에는 null |
| `TrueBaseNanoo.OpenId` | 플레이나누 openid. SDK가 반환하지 않으면 null |

## Android Apple 로그인 {#apple-android}

[Apple 신규 로그인](/guide/social/apple/signin)과 같은 `Supabase.SignInWithAppleAsync()`를 부릅니다. 플레이나누 런타임이 있으면 Android에서는 브라우저 대신 플레이나누 창이 열리고, 받은 토큰으로 Supabase와 플레이나누에 함께 로그인합니다. iOS는 달라지는 점이 없습니다.

Android에서 달라지는 결과는 다음과 같습니다.

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AppleSignInCancelled` | 창을 닫거나 뒤로가기로 나감. 창이 닫힌 뒤 약 2초 안에 옵니다 |
| `SupabaseReason.OAuthLoginInProgress` | 창이 뜨는 중이거나 로그인 처리 중에 다시 누름. 새 창은 열리지 않고, 진행 중인 로그인은 그대로 이어집니다 |
| `SupabaseReason.AnonymousRequiresLink` | 게스트 세션에서 호출함. 게스트 연동은 지원하지 않으니 로그아웃 후 호출합니다 |

::: warning 창이 닫힌 것은 SDK가 짐작합니다
플레이나누 창은 로그인에 성공했을 때만 알려 주고, 닫기·뒤로가기·애플 쪽 실패는 알려 주지 않습니다. SDK는 창이 닫혀 게임 화면으로 돌아온 뒤 토큰이 오지 않으면 취소로 봅니다. 그래서 창을 닫은 뒤 약 2초 동안은 결과가 오지 않습니다 — 로그인을 부를 때 띄운 로딩 표시를 결과가 올 때까지 유지하면, 유저에게는 창을 닫은 뒤 잠깐 로딩하다 풀리는 것으로 보입니다. 이 신호를 주지 않는 기기에서는 결과가 오지 않을 수 있으니, 로딩 화면에 닫기 버튼을 두는 것을 권장합니다. 닫은 뒤 다시 누르면 남아 있던 요청은 취소로 끝나고 새 창이 열립니다. 이때 앞 호출이 받는 취소 결과는 무시하고, 마지막 호출의 결과만 처리하세요. 로그인 처리 중에 다시 누른 호출은 `OAuthLoginInProgress`로 끝나니, 이 결과로는 로딩 화면을 닫지 마세요.
:::

## 자동 로그인

`Supabase.TriggerAutoLoginAsync()`는 플레이나누 런타임이 있을 때 두 세션을 모두 복원합니다.

1. Supabase 리프레시 토큰으로 세션 복원
2. 저장된 플레이나누 액세스 토큰으로 `TokenSignIn` 호출. 만료됐으면 저장된 플레이나누 refresh 토큰으로 새 토큰을 받음
3. 둘 다 성공하면 `true` 반환

저장된 플레이나누 토큰이 없거나 복원에 실패하면 Supabase 세션까지 로그아웃한 뒤 `false`를 반환합니다. 두 세션이 항상 동시에 유효하도록 보장하며, 이 경우 게임은 자동 로그인 실패로 받아 명시 로그인으로 유도합니다.

`SignOutFullyAsync()`는 Supabase와 플레이나누 토큰을 모두 삭제합니다.

## 플레이나누 토큰 갱신 {#nanoo-token-refresh}

게임을 켜 두는 동안 `PlayNanooRuntime`이 1시간마다 플레이나누 refresh 토큰으로 새 토큰을 받습니다. 게임 코드가 할 일은 없습니다. 갱신에 실패하면 토큰을 지우지 않고 5분 뒤 다시 시도하며, `[PlayNanooRuntime] PlayNANOO 토큰 갱신 실패` 로그에 플레이나누 에러 코드를 남깁니다. 갱신이 언제 일어나는지 보려면 [진단 › Nanoo Trace](./sync#trace)를 켭니다.
