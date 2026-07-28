# 점수 기록

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitScoreAsync<TLeaderboard>(double score)
```

본인 점수를 기록합니다. 최고·최저·최신·누적 중 어떤 방식으로 반영될지는 리더보드 설정에 따라 서버가 결정하므로, 게임은 항상 이번에 얻은 점수를 그대로 보내면 됩니다.

```csharp
var result = await Supabase.SubmitScoreAsync<ArenaLeaderboard>(1250);
if (result.IsSuccess)
{
    // 기록 방식이 적용된 뒤의 최종 점수
    scoreLabel.text = result.Data.Score.ToString();
}
```

**타입 파라미터**

| 파라미터 | 설명 |
|----------|------|
| `TLeaderboard` | [클래스 생성기](./columns#generate)로 만든 리더보드 타입 |

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `score` | 이번에 획득한 점수 |

**반환**

`.Data`는 `LeaderboardSubmitResult`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Score` | `double` | 기록 방식이 적용된 뒤의 최종 점수 |
| `.RotationCount` | `int` | 기록된 회차 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardEnded` | 종료·비활성 리더보드라 기록할 수 없습니다 |

순위와 함께 값을 보내려면 [점수 기록 · 생성 클래스](./submit-row)를 참고하세요.
