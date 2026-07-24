# 순위 조회

```csharp
Task<SupabaseResult<IReadOnlyList<LeaderboardEntry>>> Supabase.GetLeaderboardRangeAsync(
    string code,
    int    start         = 1,
    int    end           = 100,
    int?   rotationCount = null)
```

순위를 범위로 조회합니다. 한 번에 최대 100건까지 반환되며, 더 필요하면 `start`를 옮겨 다음 구간을 요청합니다.

```csharp
var result = await Supabase.GetLeaderboardRangeAsync("arena", 1, 50);
if (result.IsSuccess)
{
    foreach (var entry in result.Data)
        AddRankRow(entry.Rank, entry.DisplayName, entry.Score);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `code` | 리더보드 코드 |
| `start` | 시작 순위. 1부터 (기본값: 1) |
| `end` | 끝 순위. `start + 99`를 넘으면 잘립니다 (기본값: 100) |
| `rotationCount` | 조회할 회차. `null`이면 현재 회차 (기본값: `null`) |

**반환**

`.Data`에 `LeaderboardEntry` 목록이 순위 오름차순으로 담깁니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Rank` | `int` | 순위. 동점이면 먼저 도달한 쪽이 위 |
| `.AccountId` | `string` | 계정 ID |
| `.DisplayName` | `string` | 현재 닉네임. 없으면 `null` |
| `.Score` | `double` | 점수 |
| `.ExtraData` | `string` | 자유 형식 문자열 |
| `.RotationCount` | `int` | 회차 |
| `.Data` | `Dictionary<string, object>` | 등록된 플레이어 데이터 필드 값 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardRotationNotFound` | 존재하지 않는 회차입니다 |

::: tip 종료된 리더보드도 조회됩니다
기록은 막히지만 순위 조회는 계속 됩니다. 시즌이 끝난 뒤 최종 순위를 보여줄 때 그대로 사용하세요.
:::

지난 회차를 보려면 `rotationCount`에 이전 회차 번호를 넘깁니다. 현재 회차 번호는 [리더보드 조회](./table)로 확인합니다.
