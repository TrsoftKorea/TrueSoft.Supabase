# 점수 기록 · 생성 클래스

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitScoreAsync<TRow>(double score, TRow row)
```

순위와 함께 표시할 값을 점수와 같이 보냅니다. 어느 리더보드인지는 행 타입에서 읽으므로 타입 파라미터를 적을 필요가 없습니다.

```csharp
var row = new GuildLeaderboard.Row { CharacterLevel = 42, GuildName = "붉은검" };
await Supabase.SubmitScoreAsync(1250, row);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `score` | 이번에 획득한 점수 |
| `row` | [클래스 생성기](/guide/leaderboard/columns#generate)가 만든 중첩 `Row` 인스턴스 |

**반환**

`.Data`는 `LeaderboardSubmitResult`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Score` | `double` | 기록 방식(최고·최저·최신·누적)을 적용해 저장된 점수 |
| `RotationCount` | `int` | 기록된 회차 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardRowRequired` | 행 객체가 null입니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardEnded` | 종료·비활성 리더보드라 기록할 수 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |
| `SupabaseReason.LeaderboardScoreInvalid` | 점수가 NaN이거나 무한대입니다 |

**실사용 예시**

판이 끝나면 점수와 함께 지금 레벨과 길드를 보내고, 리더보드가 끝났으면 안내합니다.

```csharp
async void OnStageCleared(int score)
{
    var row = new GuildLeaderboard.Row
    {
        CharacterLevel = PlayerSave.Level,
        GuildName      = PlayerSave.GuildName,   // 바뀌지 않은 칸도 매번 채워 보냅니다
    };

    var result = await Supabase.SubmitScoreAsync(score, row);
    if (result.IsSuccess) ShowResultScore(result.Data.Score);
    else if (result.Reason == SupabaseReason.LeaderboardEnded) ShowToast("기간이 끝나 순위에 기록되지 않았습니다");
    else ShowToast("점수를 기록하지 못했습니다");
}
```

::: info 행에 담긴 값은 전부 전송됩니다
`Row`의 필드는 값을 바꾸지 않은 것까지 모두 보내집니다. 특정 필드만 갱신하는 개념이 아니라 "지금 이 값들로 채워라"에 가깝습니다.
:::
