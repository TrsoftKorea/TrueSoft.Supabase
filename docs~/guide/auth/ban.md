# 차단된 계정 처리 {#ban-handling}

Supabase 대시보드에서 계정을 차단(`banned_until` 설정)하면, 해당 계정으로 로그인 시 SDK가 자동으로 차단 정보를 가져와 `result.BanInfo`에 채웁니다.

```csharp
var result = await Supabase.TrySignInAnonymouslyAsync();

if (!result.Success && result.BanInfo != null)
{
    var info = result.BanInfo;

    if (info.IsPermanentBan)
        Debug.Log("영구 차단");
    else
        Debug.Log($"차단 해제: {info.BannedUntil:yyyy-MM-dd HH:mm}");

    if (!string.IsNullOrEmpty(info.BanMessage))
        Debug.Log($"사유: {info.BanMessage}");
}
```

`SupabaseCallResult.Reason == SupabaseFailReason.UserBanned`일 때만 `BanInfo`가 유효하며, 그 외에는 항상 `null`입니다.

## 수동 조회

```csharp
Task<SupabaseBanInfo> Supabase.TryGetBanInfoAsync(string accountId)
```

특정 계정의 차단 정보를 조회합니다. 차단 상태가 아니거나 조회 실패 시 `null`을 반환합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `accountId` | 조회할 계정 ID (`auth.users.id`) |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.IsPermanentBan` | `bool` | 영구 차단 여부 |
| `.BannedUntil` | `DateTimeOffset?` | 차단 해제 일시. 영구 차단이면 의미 없음 |
| `.BanMessage` | `string` | 어드민이 설정한 차단 사유 메시지. 없으면 빈 문자열 |
