# 로그인

## 로그인 호출

로그인 API는 이관 전·중·후 동일합니다. 자세한 내용은 [인증](/guide/auth/anonymous)을 참고하세요.

Android에서 Apple 로그인은 플레이나누 WebView를 통해 토큰을 획득한 뒤 호출합니다.

```csharp
// Apple (Android) — 플레이나누 WebView로 토큰 획득
playNanooRuntime.StartAppleSignInAndroid();
```

플레이나누 로그인이 성공하면 아래 프로퍼티를 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `PlayNanooRuntimeBase.UserId` | 플레이나누 uuid. 로그인 전에는 null |
| `PlayNanooRuntimeBase.OpenId` | 플레이나누 openid. SDK가 반환하지 않으면 null |

## 자동 로그인

`Supabase.TriggerAutoLoginAsync()`는 플레이나누 런타임이 있을 때 두 세션을 모두 복원합니다.

1. Supabase 리프레시 토큰으로 세션 복원
2. 저장된 플레이나누 액세스 토큰으로 `TokenSignIn` 호출
3. 둘 다 성공하면 `true` 반환

플레이나누 복원이 실패하면 Supabase 세션도 롤백한 뒤 `false`를 반환합니다. 두 세션이 항상 동시에 유효하도록 보장합니다.

`SignOutFullyAsync()`는 Supabase와 플레이나누 액세스 토큰을 모두 삭제합니다. 플레이나누 액세스 토큰 유효기간은 24시간이라, 그 이후에는 자동 로그인이 만료되어 플레이어가 직접 로그인합니다.
