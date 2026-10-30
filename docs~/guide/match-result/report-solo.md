# 상대 없는 판 기록

```csharp
Task<SupabaseResult> Supabase.ReportSoloMatchResultAsync(
    string  sessionId,
    string  gameCode,
    bool    isWin,
    double? score = null)
```

봇을 상대했거나 혼자 한 판의 결과를 기록합니다. 맞춰 볼 상대 신고가 없어 보상은 지급하지 않고, [전적 요약](/guide/match-result/stats)의 `.Solo`에만 셉니다.

```csharp
if (opponentIsBot)
    await Supabase.ReportSoloMatchResultAsync(sessionId, "arena_1v1", isWin: didWin, score: wave);
else
    await Supabase.ReportMatchResultAsync(sessionId, "arena_1v1", didWin, opponent.AccountId, score: wave);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `sessionId` | 게임이 부여한 매치 세션 식별자. 판마다 새로 만들어야 합니다 |
| `gameCode` | 운영 콘솔에 등록한 게임 코드 |
| `isWin` | 본인 기준 승패. 협동·혼자 하는 모드라면 성공 여부 |
| `score` | 웨이브·점수·시간처럼 게임이 정하는 판 점수 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchSessionIdEmpty` | 세션 ID가 비어 있습니다 |
| `SupabaseReason.MatchGameCodeEmpty` | 게임 코드가 비어 있습니다 |
| `SupabaseReason.MatchRewardConfigNotFound` | 해당 게임 코드가 운영 콘솔에 등록돼 있지 않습니다 |
| `SupabaseReason.MatchResultMismatch` | 같은 세션을 대인전이나 다른 게임 코드로 이미 신고했습니다 |
| `SupabaseReason.MatchScoreInvalid` | 점수가 NaN이거나 무한대입니다 |

::: warning 같은 세션 ID를 다시 쓰면 기록되지 않습니다
같은 세션을 다시 신고하면 처음 기록이 그대로 남고 이번 결과는 무시됩니다. 실패 없이 넘어가므로, 판마다 세션 ID를 새로 만들지 않으면 두 번째 판부터 조용히 빠집니다.
:::

::: warning 상대 없는 판은 검증되지 않습니다
플레이어 본인의 신고만으로 기록되므로 조작할 수 있습니다. 재화를 주는 보상의 근거로 쓰지 마세요.
:::
