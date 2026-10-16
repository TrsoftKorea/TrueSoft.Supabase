# 봇 판 기록

```csharp
Task<SupabaseResult> Supabase.ReportBotMatchResultAsync(
    string sessionId,
    string gameCode,
    bool   isWin)
```

봇을 상대로 한 판의 결과를 기록합니다. 맞춰 볼 상대 신고가 없어 보상은 지급하지 않고, [승리 수 조회](/guide/match-result/win-count)의 `.Bot`에만 셉니다.

```csharp
if (opponentIsBot)
    await Supabase.ReportBotMatchResultAsync(sessionId, "arena_1v1", isWin: didWin);
else
    await Supabase.ReportMatchResultAsync(sessionId, "arena_1v1", didWin, opponent.AccountId);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `sessionId` | 게임이 부여한 매치 세션 식별자. 대인전 세션과 겹치지 않아야 합니다 |
| `gameCode` | 운영이 어드민에 등록한 게임 코드 |
| `isWin` | 본인 기준 승패 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchSessionIdEmpty` | 세션 ID가 비어 있습니다 |
| `SupabaseReason.MatchGameCodeEmpty` | 게임 코드가 비어 있습니다 |
| `SupabaseReason.MatchRewardConfigNotFound` | 해당 게임 코드가 어드민에 등록돼 있지 않습니다 |
| `SupabaseReason.MatchResultMismatch` | 같은 세션을 대인전이나 다른 게임 코드로 이미 신고했습니다 |

::: warning 봇 판은 검증되지 않습니다
봇 판은 유저 본인의 신고만으로 기록되므로 조작할 수 있습니다. 재화를 주는 보상의 근거로 쓰지 마세요. 같은 세션을 다시 신고해도 한 번만 기록됩니다.
:::
