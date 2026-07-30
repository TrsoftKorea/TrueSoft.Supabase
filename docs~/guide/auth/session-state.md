# 로그인 후 상태

로그인(익명·소셜·자동 로그인 등 방식과 무관)이 성공하면 아래 프로퍼티를 바로 사용할 수 있습니다. 세션에서 파생되는 라이브 값이라 언제 읽어도 정확합니다.

| 프로퍼티 | 설명 |
|---------|------|
| `Supabase.IsLoggedIn` | 현재 로그인 여부 |
| `Supabase.UserId` | 현재 로그인 계정 ID (`auth.users.id`) |
| `Supabase.IsAnonymous` | 익명 로그인 여부 |
| `Supabase.IsLinkedWithGoogle` | Google 연동 여부 |
| `Supabase.IsLinkedWithApple` | Apple 연동 여부 |

내 프로필은 이 프로퍼티들과 별개로 로그인 결과의 `.Profile`에 담겨 옵니다. 닉네임·서버 코드·탈퇴 상태가 여기 들어 있습니다.
