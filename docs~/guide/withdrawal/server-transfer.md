# 서버 이주

플레이어를 다른 서버로 이동시킵니다. 서버별로 닉네임 고유성이 관리되므로, 이주 대상 서버에 같은 닉네임이 이미 존재하면 실패합니다.

```csharp
Task<SupabaseCallResult> Supabase.TryTransferMyServerAsync(string targetServerCode, string reason = null)
```

현재 계정을 지정한 서버로 이주합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `targetServerCode` | 이주할 서버 코드 (예: `"GLOBAL"`, `"KR1"`) | `string` |
| `reason` | 이주 사유. 서버 로그에만 기록됨 (기본값: `null`) | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

```csharp
await Supabase.TryTransferMyServerAsync("GLOBAL", reason: null);
```
