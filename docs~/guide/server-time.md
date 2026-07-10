# 서버 시간

```csharp
Task<SupabaseResult<DateTime>> Supabase.GetServerUtcNowAsync()
```

서버 기준 UTC 시각을 조회합니다. 클라이언트 시계는 사용자가 바꿀 수 있으니 출석·이벤트 기간·쿨다운 판정에 사용하세요. 로그인 없이 호출할 수 있습니다.

**반환**

`.Data`에 서버 기준 UTC `DateTime`이 담깁니다. 조회 실패 시 `.IsSuccess`가 `false`입니다.

```csharp
var t = await Supabase.GetServerUtcNowAsync();
if (!t)
    return;   // 조회 실패
if (t.Data < eventEndUtc)
    GrantEventReward();
```
