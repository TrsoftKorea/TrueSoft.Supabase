# Apple 로그인 · Android

```csharp
Task<SupabaseCallResult> Supabase.TrySignInWithAppleViaBrowserAsync(string redirectScheme, string redirectHost = "login-callback")
```

Android 등 네이티브 Sign in with Apple을 쓸 수 없는 플랫폼에서, 시스템 브라우저로 Apple 로그인을 띄우고 딥링크로 돌아온 세션으로 로그인합니다. iOS는 [네이티브 로그인](./signin)을 쓰세요.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `redirectScheme` | 앱 딥링크 스킴(예: `mygame`). AndroidManifest에 등록한 값과 같아야 합니다 |
| `redirectHost` | 딥링크 호스트 (기본값: `login-callback`). Supabase Redirect URL 허용목록과 일치해야 합니다 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.OAuthRedirectSchemeEmpty` | 딥링크 스킴이 비어있음 |
| `SupabaseFailReason.OAuthRefreshTokenMissing` | 리다이렉트에 세션 토큰이 없음 |
| `SupabaseFailReason.OAuthLoginInProgress` | 이미 진행 중인 로그인이 있음 |
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |

## 사전 준비

1. [대시보드·빌드 설정](./setup)에서 Supabase Apple provider를 활성화합니다.
2. Supabase 대시보드 **Authentication > URL Configuration > Redirect URLs**에 `{scheme}://{host}`(예: `mygame://login-callback`)를 추가합니다.
3. `AndroidManifest.xml`의 메인 액티비티에 딥링크 intent-filter를 추가합니다.

```xml
<intent-filter>
    <action android:name="android.intent.action.VIEW" />
    <category android:name="android.intent.category.DEFAULT" />
    <category android:name="android.intent.category.BROWSABLE" />
    <data android:scheme="mygame" android:host="login-callback" />
</intent-filter>
```

## 사용

```csharp
var ok = await Supabase.TrySignInWithAppleViaBrowserAsync("mygame");
if (!ok) Debug.LogWarning($"Apple 로그인 실패: {ok.Reason}");
```

::: info 딥링크를 직접 처리할 때
브라우저 실행·딥링크 수신을 게임이 자체 관리한다면, `Supabase.BuildOAuthAuthorizeUrl("apple", redirectTo)`로 URL을 만들어 열고, 돌아온 URL을 `Supabase.TryCompleteOAuthRedirectAsync(url)`에 넘기세요.
:::
