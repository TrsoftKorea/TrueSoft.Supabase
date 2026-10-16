# 전적 요약

```csharp
Task<SupabaseResult<MatchStats>> Supabase.GetMatchStatsAsync(string gameCode)
```

본인이 그 게임 코드로 남긴 승·패 수와 점수 최댓값·최솟값을 확인 단계별로 나눠 돌려줍니다. 결과가 맞지 않아 무효가 된 판은 어디에도 세지 않습니다.

```csharp
var result = await Supabase.GetMatchStatsAsync("arena_1v1");
if (!result.IsSuccess)
    return;

var all = result.Data.Total;
ShowRewardTrack(all.Wins);
ShowBestWave(all.MaxScore ?? 0);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `gameCode` | 셀 게임 코드 |

**반환**

`.Data`는 `MatchStats`입니다. 단계마다 같은 모양인 `MatchStatsBucket`을 들고 있습니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Verified` | `MatchStatsBucket` | 상대 신고와 맞춰 본 판 |
| `.Pending` | `MatchStatsBucket` | 상대가 아직 신고하지 않은 판. 상대가 다르게 신고하면 빠집니다 |
| `.Solo` | `MatchStatsBucket` | [상대 없는 판](/guide/match-result/report-solo) |
| `.Total` | `MatchStatsBucket` | 세 단계를 합친 값 |

`MatchStatsBucket`의 프로퍼티는 다음과 같습니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Wins` | `int` | 이긴 판 수. 협동이라면 성공한 판 수 |
| `.Losses` | `int` | 진 판 수 |
| `.MaxScore` | `double?` | 신고한 점수 중 가장 큰 값. 점수를 보낸 판이 없으면 `null` |
| `.MinScore` | `double?` | 신고한 점수 중 가장 작은 값. 기록 시간처럼 작을수록 좋은 값에 씁니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchGameCodeEmpty` | 게임 코드가 비어 있습니다 |

::: warning 화면 표시에는 Total, 지급 판단에는 Verified
상대가 신고 전에 게임을 끄면 그 판은 계속 `Pending`으로 남습니다. 그래서 화면에 보여 줄 값은 `Total`이 맞습니다. 다만 `Pending`과 `Solo`는 유저 본인의 신고뿐이라 조작할 수 있으니, 재화를 주는 판단에는 `Verified`만 쓰세요. 점수는 `Verified` 판이라도 각자 보낸 값입니다.
:::

::: info 랭킹은 리더보드로
다른 유저와 순위를 겨루거나 시즌마다 초기화하려면 [리더보드](/guide/leaderboard/)에 점수를 따로 기록합니다.
:::
