# 추가 데이터 수정

```csharp
Task<SupabaseResult> Supabase.SetRowAsync<TRow>(TRow row)
```

이미 기록된 본인 항목의 등록 필드를 **현재 회차**에서 수정합니다. **점수는 바뀌지 않습니다.** 어느 리더보드인지는 행 타입에서 읽습니다.

넘기는 행은 **[`Supabase.ToRow`](./columns#to-row)로 만든 것**이어야 합니다. 직접 `new`한 행은 거부됩니다.

```csharp
var me  = await Supabase.GetRankAsync<GuildLeaderboard>();
var row = Supabase.ToRow<GuildLeaderboard.Row>(me.Data);   // 서버 현재값
row.GuildName = "새 길드";                                   // 바꿀 것만 수정
await Supabase.SetRowAsync(row);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `row` | [`Supabase.ToRow`](./columns#to-row)로 조회 결과에서 만든 행 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardRowRequired` | 행 객체가 null입니다 |
| `SupabaseReason.LeaderboardRowNotLoaded` | 조회에서 만들지 않은 행입니다. `ToRow`로 받아 고쳐서 넘기세요 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardScoreNotFound` | 그 회차에 본인 기록이 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |

::: info 왜 조회한 행만 받나요
`Row`의 필드는 값을 바꾸지 않은 것까지 **모두** 전송됩니다. 직접 만든 행을 넘기면 채우지 않은 필드가 `0`·`null`로 덮어써져 값이 조용히 사라집니다. 그래서 SDK가 조회로 채운 행만 받습니다.
:::

::: warning 기록이 있어야 수정됩니다
점수를 한 번도 올리지 않았다면 `LeaderboardScoreNotFound`로 실패합니다. 먼저 [점수 기록](./submit)이 필요합니다.
:::
