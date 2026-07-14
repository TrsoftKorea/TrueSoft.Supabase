# 차단된 계정 처리 {#ban-handling}

차단된 계정의 로그인 결과 처리와 차단 정보 조회 방법입니다.

## 로그인 시 자동 감지

Supabase 대시보드에서 계정을 차단(`banned_until` 설정)하면, 해당 계정으로 로그인 시 SDK가 자동으로 차단 정보를 가져와 `result.BanInfo`에 채웁니다.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();

if (!result.IsSuccess && result.BanInfo != null)
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

`SupabaseResult.Reason == SupabaseFailCode.UserBanned`일 때만 `BanInfo`가 유효하며, 그 외에는 항상 `null`입니다.

## 수동 조회 {#manual-lookup}

```csharp
Task<SupabaseResult<SupabaseBanInfo>> Supabase.GetBanInfoAsync(string accountId)
```

특정 계정의 차단 정보를 조회합니다. 조회에 성공하면 `.Data`에 차단 정보가 담기며, 차단 상태가 아니면 `.Data == null`입니다. 조회 자체가 실패하면 `!result.IsSuccess`입니다.

```csharp
var ban = await Supabase.GetBanInfoAsync(id);
if (!ban)
    Debug.Log("조회 실패");
else if (ban.Data == null)
    Debug.Log("정상 (차단 없음)");
else if (ban.Data.IsPermanentBan)
    Debug.Log("영구 차단");
else
    Debug.Log($"차단 해제: {ban.Data.BannedUntil:yyyy-MM-dd HH:mm}");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `accountId` | 조회할 계정 ID (`auth.users.id`) |

**반환**

`.Data`는 차단 정보(`SupabaseBanInfo`)입니다. 차단 상태가 아니면 `null`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Data.IsPermanentBan` | `bool` | 영구 차단 여부 |
| `.Data.BannedUntil` | `DateTimeOffset?` | 차단 해제 일시. 영구 차단이면 의미 없음 |
| `.Data.BanMessage` | `string` | 어드민이 설정한 차단 사유 메시지. 없으면 빈 문자열 |
