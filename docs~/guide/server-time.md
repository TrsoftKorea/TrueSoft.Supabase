# 서버 시간

```csharp
Task<DateTime> Supabase.TryGetServerUtcNowAsync(DateTime defaultValue = default)
```

서버 기준 UTC 시각을 조회합니다. 조회 실패 시 `defaultValue`를 반환합니다. 클라이언트 시계는 사용자가 바꿀 수 있으니 출석·이벤트 기간·쿨다운 판정에 사용하세요. 로그인 없이 호출할 수 있습니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `defaultValue` | 조회 실패 시 반환할 기본값 (기본값: `default` → `0001-01-01`) |

**반환**

서버 기준 UTC `DateTime`. 조회 실패 시 `defaultValue`.

```csharp
var now = await Supabase.TryGetServerUtcNowAsync(DateTime.UtcNow);
if (now < eventEndUtc)
    GrantEventReward();
```
