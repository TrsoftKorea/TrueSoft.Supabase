# 닉네임 중복 확인

```csharp
Task<SupabaseResult> Supabase.IsNameAvailableAsync(string displayName)
```

닉네임 사용 가능 여부를 확인합니다. `result.IsSuccess`가 `true`면 사용 가능, `false`면 이미 사용 중입니다. 현재 계정이 이미 사용 중인 닉네임은 사용 가능으로 처리합니다.

```csharp
var result = await Supabase.IsNameAvailableAsync(displayName);
if (result.IsSuccess)
{
    ShowMessage("사용 가능한 닉네임입니다.");   // IsSuccess=true → 사용 가능
}
else
{
    ShowMessage("이미 사용 중인 닉네임입니다.");
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 확인할 닉네임. 최대 64자 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
