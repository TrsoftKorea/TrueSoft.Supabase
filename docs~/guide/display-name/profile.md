# 프로필 조회

## 내 프로필 {#my-profile}

내 프로필(닉네임, 서버 코드 등)은 **로그인 결과**(`SupabaseSignInResult`)의 `.Profile`에 담겨 옵니다. 별도 조회 없이 로그인 직후 사용하고, 이후에도 쓰려면 게임에서 보관하세요.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();   // TriggerAutoLoginAsync 등 모든 로그인 동일
if (result.IsSuccess)
{
    var profile = result.Profile;
    ShowMyProfile(profile.DisplayName, profile.ServerCode);
}
```

## 다른 플레이어 프로필

```csharp
Task<SupabaseResult<PublicProfileSnapshot>> Supabase.GetPublicProfileAsync(string userId)
```

다른 플레이어의 공개 프로필(닉네임, 서버 코드 등)을 조회합니다.

```csharp
var result = await Supabase.GetPublicProfileAsync(userId);
if (result.IsSuccess)
{
    var profile = result.Data;
    ShowProfile(profile.DisplayName, profile.ServerCode);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |

**반환**

두 조회 모두 `.Data`에 `PublicProfileSnapshot`이 담깁니다. 조회 실패 시 없음.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Data.DisplayName` | `string` | 닉네임 |
| `.Data.ServerCode` | `string` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `.Data.IsWithdrawn` | `bool` | 탈퇴 예약 여부 |
