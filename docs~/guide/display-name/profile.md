# 프로필 조회

다른 플레이어의 공개 프로필(닉네임, 서버 코드 등)을 조회합니다.  
**내 프로필은 로그인 완료 시 자동으로 조회·캐시**됩니다. 별도 API 호출 없이 바로 사용할 수 있습니다.  
사용 가능한 프로퍼티 목록은 [로그인 후 사용 가능한 값](/guide/auth/auto-login#after-login-values)을 참고하세요.

```csharp
Task<PublicProfileSnapshot> Supabase.TryGetPublicProfileAsync(string userId)
```

다른 플레이어의 공개 프로필을 조회합니다. 조회 실패 시 `null`을 반환합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.DisplayName` | `string` | 닉네임 |
| `.ServerCode` | `string` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `.IsWithdrawn` | `bool` | 탈퇴 예약 여부 |

```csharp
var profile = await Supabase.TryGetPublicProfileAsync(userId);
```
