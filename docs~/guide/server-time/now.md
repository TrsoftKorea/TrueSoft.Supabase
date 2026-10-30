# 서버 시각 조회

```csharp
Task<SupabaseResult<DateTimeOffset>> Supabase.GetServerNowAsync()
```

서버 기준 UTC 시각을 반환합니다. 기준점이 있으면 네트워크 없이, 없으면 서버에서 받아 돌려줍니다.

```csharp
var t = await Supabase.GetServerNowAsync();
if (!t.IsSuccess) return;

if (t.Data < eventEndUtc)
    GrantEventReward();
```

**반환**

`.Data`는 서버 기준 UTC 시각 `DateTimeOffset`입니다.
