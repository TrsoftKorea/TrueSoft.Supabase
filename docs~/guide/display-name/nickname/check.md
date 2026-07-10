# 닉네임 중복 확인

```csharp
Task<SupabaseResult> Supabase.IsDisplayNameAvailableAsync(string displayName)
```

닉네임 사용 가능 여부를 확인합니다. `result.IsSuccess`가 `true`면 사용 가능, `false`면 이미 사용 중입니다. 현재 계정이 이미 사용 중인 닉네임은 사용 가능으로 처리합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 확인할 닉네임. 최대 64자 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.DisplayNameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
