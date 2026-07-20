# 서버 정보 조회

```csharp
Task<SupabaseResult<ServerInfo>> Supabase.GetServerInfoAsync()
```

DB에 기록된 내 서버(`profiles.server_id`)에 대응하는 서버 코드를 조회합니다. 서버 배정은 로그인 시 자동으로 동기화됩니다.

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `ServerId` | `string` | 서버의 내부 ID |
| `ServerCode` | `string` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseErrorCode.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseErrorCode.NotInitialized` | SDK가 초기화되지 않았습니다 |
