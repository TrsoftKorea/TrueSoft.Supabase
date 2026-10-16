# 승리 수 조회

```csharp
Task<SupabaseResult<MatchWinCount>> Supabase.GetMatchWinCountAsync(string gameCode)
```

본인이 그 게임 코드로 이긴 판 수를 검증 상태별로 나눠 돌려줍니다. 결과가 맞지 않아 무효가 된 판은 어디에도 세지 않습니다.

```csharp
var result = await Supabase.GetMatchWinCountAsync("arena_1v1");
if (!result.IsSuccess)
    return;

ShowRewardTrack(result.Data.Total);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `gameCode` | 셀 게임 코드 |

**반환**

`.Data`는 `MatchWinCount`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Verified` | `int` | 상대 신고와 맞춰 본 승리 |
| `.Pending` | `int` | 상대가 아직 신고하지 않은 승리. 상대가 다르게 신고하면 빠집니다 |
| `.Bot` | `int` | [봇 판](/guide/match-result/report-bot)에서 이긴 수 |
| `.Total` | `int` | 세 값의 합 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchGameCodeEmpty` | 게임 코드가 비어 있습니다 |

::: warning 화면 표시에는 Total, 지급 판단에는 Verified
상대가 신고 전에 게임을 끄면 그 판은 계속 `Pending`으로 남습니다. 그래서 화면에 보여 줄 승리 수는 `Total`이 맞습니다. 다만 `Pending`과 `Bot`은 유저 본인의 신고뿐이라 조작할 수 있으니, 재화를 주는 판단에는 `Verified`만 쓰세요.
:::
