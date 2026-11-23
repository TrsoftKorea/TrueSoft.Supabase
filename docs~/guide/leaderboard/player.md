# 플레이어 순위 조회

```csharp
Task<SupabaseResult<LeaderboardPlayerEntry>> Supabase.GetRankAsync<TLeaderboard>(
    string accountId     = null,
    int?   rotationCount = null)
```

플레이어 1명의 순위를 조회합니다. 아직 기록이 없어도 실패가 아니라 `Registered`가 `false`인 결과로 성공합니다.

```csharp
var result = await Supabase.GetRankAsync<ArenaLeaderboard>();
if (result.IsSuccess)
{
    myRankLabel.text = result.Data.Registered
        ? $"{result.Data.Rank}위"
        : "기록 없음";
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `TLeaderboard` | [클래스 생성기](/guide/leaderboard/columns#generate)로 만든 리더보드 타입 |
| `accountId` | 조회할 계정. `null`이면 본인 (기본값: `null`) |
| `rotationCount` | 조회할 회차. `null`이면 현재 회차 (기본값: `null`) |

**반환**

`.Data`는 `LeaderboardPlayerEntry`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Registered` | `bool` | 이 회차에 기록이 있는지. `false`면 아래 값은 비어 있음 |
| `Rank` | `int` | 순위 |
| `AccountId` | `string` | 계정 ID |
| `UserId` | `string` | 플레이어 고유 ID |
| `DisplayName` | `string` | 현재 닉네임 |
| `Score` | `double` | 점수 |
| `RotationCount` | `int` | 회차 |
| `Data` | `Dictionary<string, object>` | 등록된 플레이어 데이터 필드 값. [`Supabase.ToRow`](/guide/leaderboard/columns#to-row)로 생성 타입의 행으로 바꿔 씁니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardRotationNotFound` | 존재하지 않는 회차입니다 |

**실사용 예시**

순위표에서 회차 탭을 고르면 그 회차의 내 순위 줄을 맨 아래에 붙입니다.

```csharp
public async void OnSelectRotationTab(int rotationCount)
{
    var result = await Supabase.GetRankAsync<ArenaLeaderboard>(rotationCount: rotationCount);
    if (!result.IsSuccess)
    {
        if (result.Reason == SupabaseReason.LeaderboardRotationNotFound)
            ShowToast("그 회차 기록이 없습니다");
        return;
    }

    var me = result.Data;
    myRankRow.SetActive(me.Registered);   // 기록이 없으면 내 줄을 숨깁니다
    if (me.Registered) SetMyRankRow(me.Rank, me.DisplayName, me.Score);
}
```

::: warning 내 순위 조회 비용
내 순위는 나보다 앞선 기록 수를 세어 계산하므로 리더보드가 커질수록 비용이 늘어납니다. 매 프레임 호출하지 말고 결과를 캐시해 두세요.
:::
