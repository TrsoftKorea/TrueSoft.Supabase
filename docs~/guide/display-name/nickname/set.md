# 닉네임 설정

```csharp
Task<SupabaseResult<string>> Supabase.SetNameAsync(string displayName)
```

내 닉네임을 설정합니다. 현재 닉네임과 동일하면 네트워크 요청 없이 성공 처리됩니다. 성공 시 `result.Data`에 정규화되어 적용된 닉네임 문자열이 담기므로, 로그인 시 보관해 둔 프로필의 이름을 이 값으로 교체하세요.

```csharp
var result = await Supabase.SetNameAsync(displayName);
if (result.IsSuccess)
{
    ShowMessage($"닉네임이 {result.Data}으로 변경되었습니다.");   // result.Data = 적용된 닉네임
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

`.Data`는 정규화되어 적용된 닉네임 `string`입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NameTaken` | 이미 사용 중인 닉네임입니다 |
| `SupabaseReason.NameForbidden` | 금칙어가 들어 있거나 운영 콘솔에서 금지한 닉네임입니다 |
| `SupabaseReason.NameTooLong` | 허용 길이를 넘었습니다 |
