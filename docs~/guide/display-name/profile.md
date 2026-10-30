# 공개 프로필 조회

```csharp
Task<SupabaseResult<PublicProfile>> Supabase.GetPublicProfileAsync(string userId)
```

다른 플레이어의 공개 프로필을 조회합니다 — 닉네임·서버 코드 등이 담깁니다. 내 프로필은 별도 조회 없이 [로그인 결과](/guide/auth/anonymous)의 `.Profile`로 바로 옵니다.

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

`.Data`는 `PublicProfile`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Name` | `string` | 닉네임 |
| `ServerCode` | `string` | 서버 코드. 예: `"GLOBAL"`, `"KR1"` |
| `IsWithdrawn` | `bool` | 탈퇴 예약 여부 |
