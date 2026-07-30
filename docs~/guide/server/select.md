# 서버 선택

```csharp
SupabaseResult Supabase.SetServerCode(string serverCode)
```

이 기기에서 접속할 서버 코드를 저장합니다. 네트워크를 타지 않고 즉시 반환합니다.

```csharp
if (Supabase.SetServerCode("KR1"))
    await Supabase.SignInAnonymouslyAsync();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `serverCode` | `game_servers.code` 값. 예: `GLOBAL`·`KR1` |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.ServerCodeEmpty` | 서버 코드가 비어 있습니다 |

::: warning 로그인 전에 호출하세요
저장한 코드는 **로그인할 때** 계정 이주에 쓰입니다. 이미 로그인한 상태에서 바꾸면 다음 로그인부터 적용됩니다.
:::
