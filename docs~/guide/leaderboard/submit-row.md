# 점수 기록 · 생성 클래스

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitScoreAsync<TRow>(double score, TRow row)
```

순위와 함께 표시할 값을 점수와 같이 보냅니다. 어느 리더보드인지는 행 타입에서 읽으므로 타입 파라미터를 적을 필요가 없습니다.

```csharp
var row = new ArenaLeaderboard.Row { CharacterLevel = 42, GuildName = "붉은검" };
await Supabase.SubmitScoreAsync(1250, row);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `score` | 이번에 획득한 점수 |
| `row` | [클래스 생성기](./columns#generate)가 만든 중첩 `Row` 인스턴스 |

**반환**

`.Data`는 `LeaderboardSubmitResult`이며 `.Score`(최종 점수)·`.RotationCount`(기록된 회차)를 담습니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardRowRequired` | 행 객체가 null입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardEnded` | 종료·비활성 리더보드라 기록할 수 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |

::: info 행에 담긴 값은 전부 전송됩니다
`Row`의 필드는 값을 바꾸지 않은 것까지 모두 보내집니다. 특정 필드만 갱신하는 개념이 아니라 "지금 이 값들로 채워라"에 가깝습니다.
:::
