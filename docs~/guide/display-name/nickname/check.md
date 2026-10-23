# 닉네임 중복 확인

```csharp
Task<SupabaseResult> Supabase.IsNameAvailableAsync(string displayName)
```

닉네임 사용 가능 여부를 확인합니다. `result.IsSuccess`가 `true`면 사용 가능입니다. 쓸 수 없으면 `.Reason`이 이유를 알려 줍니다. 현재 계정이 이미 사용 중인 닉네임은 사용 가능으로 처리합니다.

```csharp
var result = await Supabase.IsNameAvailableAsync(displayName);
if (result.IsSuccess)
    ShowMessage("사용 가능한 닉네임입니다.");
else if (result.Reason == SupabaseReason.NameForbidden)
    ShowMessage("사용할 수 없는 단어가 들어 있습니다.");
else
    ShowMessage("이미 사용 중인 닉네임입니다.");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 확인할 닉네임. 최대 64자 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseReason.NameForbidden` | 금칙어가 들어 있거나 운영 콘솔에서 금지한 닉네임 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
