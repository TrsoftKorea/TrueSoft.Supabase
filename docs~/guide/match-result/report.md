# 매치 결과 신고

```csharp
Task<SupabaseResult<MatchResultReportOutcome>> Supabase.ReportMatchResultAsync(
    string sessionId,
    string gameCode,
    bool   isWin,
    string opponentAccountId)
```

본인이 겪은 매치 결과를 신고합니다. 상대의 신고와 [교차검증](/guide/match-result/#cross-check)돼야 보상이 지급됩니다.

```csharp
var result = await Supabase.ReportMatchResultAsync(
    photonRoomName, "arena_1v1", isWin: didWin, opponentAccountId: opponent.AccountId);

if (!result.IsSuccess)
{
    if (result.Reason == SupabaseReason.MatchResultMismatch)
        return; // 조용히 무시 — 통신 유실이나 부정 신고 시도일 수 있다
    ShowError(result.ErrorCode);
    return;
}

switch (result.Data.Status)
{
    case MatchResultStatus.Pending:
        break; // 상대 신고를 기다린다. 별도 폴링은 필요 없다 — 상대가 신고하면 그쪽 호출에서 함께 처리된다
    case MatchResultStatus.Paid:
    case MatchResultStatus.AlreadyPaid:
        if (result.Data.Rewarded)
            await RefreshMailboxAsync();
        break;
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `sessionId` | 게임이 부여한 매치 세션 식별자. 예: Photon 룸 이름 |
| `gameCode` | 보상 설정을 구분하는 코드. 운영이 어드민에 미리 등록해야 합니다 |
| `isWin` | 본인 기준 승패 |
| `opponentAccountId` | 상대로 지목하는 계정 ID |

**반환**

`.Data`는 `MatchResultReportOutcome`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Status` | `MatchResultStatus` | `Pending`은 상대 신고 대기, `Paid`는 이번 호출로 지급 완료, `AlreadyPaid`는 이전에 이미 지급된 세션을 재신고한 경우 |
| `.Rewarded` | `bool` | 본인에게 보상 우편이 지급됐는지. `Pending`이면 항상 false |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchSessionIdEmpty` | 세션 ID가 비어 있습니다 |
| `SupabaseReason.MatchGameCodeEmpty` | 게임 코드가 비어 있습니다 |
| `SupabaseReason.MatchOpponentRequired` | 상대 계정이 지정되지 않았습니다 |
| `SupabaseReason.MatchOpponentInvalid` | 상대 계정으로 본인을 지목했습니다 |
| `SupabaseReason.MatchRewardConfigNotFound` | 해당 게임 코드에 등록된 보상 설정이 없습니다 |
| `SupabaseReason.MatchResultMismatch` | 상호 지목이 어긋났거나 승패가 검증 정책과 맞지 않습니다. 지급되지 않습니다 |

::: info 대기 상태는 폴링하지 않습니다
`Pending`은 오류가 아니라 상대의 신고를 기다리는 정상 상태입니다. 상대가 신고하면 그 호출에서 양쪽 모두 함께 처리되므로, 게임이 따로 재확인 요청을 보낼 필요가 없습니다.
:::
