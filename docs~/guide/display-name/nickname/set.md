# 닉네임 설정

```csharp
Task<SupabaseResult<PublicProfileSnapshot>> Supabase.SetMyDisplayNameAsync(string displayName)
```

내 닉네임을 설정합니다. 현재 닉네임과 동일하면 네트워크 요청 없이 성공 처리됩니다. 성공 시 `result.Data`에 변경된 닉네임이 반영된 내 프로필이 담기므로, [로그인 시 보관해 둔 프로필](/guide/display-name/profile#my-profile)을 이 값으로 교체하세요.

```csharp
var result = await Supabase.SetMyDisplayNameAsync(displayName);
if (result.IsSuccess)
{
    myProfile = result.Data;   // 변경된 닉네임이 반영된 프로필로 교체
    ShowMessage($"닉네임이 {result.Data.DisplayName}으로 변경되었습니다.");
}
else
{
    ShowError(result.Reason);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 설정할 닉네임. 최대 64자 |

**반환**

`.Data` — 변경된 닉네임이 반영된 내 프로필(`PublicProfileSnapshot`). 실패 시 없음.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.DisplayNameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseFailReason.DisplayNameTooLong` | 허용 길이 초과 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
