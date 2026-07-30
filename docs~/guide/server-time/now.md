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

`.Data`에 서버 기준 UTC 시각이 `DateTimeOffset`으로 담깁니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NetworkError` | 한 번도 시각을 받지 못했고 지금도 서버에 닿지 못했습니다 |
