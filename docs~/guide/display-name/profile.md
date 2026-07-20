# 공개 프로필 조회

```csharp
Task<SupabaseResult<PublicProfile>> Supabase.GetPublicProfileAsync(string userId)
```

다른 플레이어의 공개 프로필(닉네임, 서버 코드 등)을 조회합니다. 내 프로필은 별도 조회 없이 [로그인 결과](/guide/auth/anonymous)의 `.Profile`로 바로 옵니다.

```csharp
var result = await Supabase.GetPublicProfileAsync(userId);
if (result.IsSuccess)
{
    var profile = result.Data;
    ShowProfile(profile.Name, profile.ServerCode);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |

**반환**

`.Data`에 `PublicProfile`이 담깁니다. 조회 실패 시 없음.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Data.Name` | `string` | 닉네임 |
| `.Data.ServerCode` | `string` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `.Data.IsWithdrawn` | `bool` | 탈퇴 예약 여부 |

**에러 코드**

| ErrorCode | 설명 |
|--------|------|
| `SupabaseErrorCode.NotSignedIn` | 로그인 상태가 아닙니다 |
