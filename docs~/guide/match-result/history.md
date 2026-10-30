# 최근 전적

```csharp
Task<SupabaseResult<IReadOnlyList<MatchRecord>>> Supabase.GetMatchHistoryAsync(
    string          gameCode = null,
    int             limit    = 20,
    DateTimeOffset? before   = null)
```

본인의 매치 기록을 최근 순으로 돌려줍니다. 무효가 된 판도 상태와 함께 들어 있습니다.

```csharp
var page = await Supabase.GetMatchHistoryAsync("arena_1v1", limit: 20);
if (!page.IsSuccess)
    return;

foreach (var record in page.Data)
    AddRow(record.IsWin, record.Score, record.Status, record.ReportedAt);

// 다음 쪽
if (page.Data.Count > 0)
{
    var next = await Supabase.GetMatchHistoryAsync(
        "arena_1v1", limit: 20, before: page.Data[page.Data.Count - 1].ReportedAt);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `gameCode` | 게임 코드. 비우면 모든 게임 코드의 기록을 섞어 돌려줍니다 (기본값: `null`) |
| `limit` | 한 번에 받을 개수. 1~100 밖이면 그 범위로 맞춥니다 (기본값: `20`) |
| `before` | 다음 쪽을 받을 때 앞 쪽 마지막 기록의 `ReportedAt` (기본값: `null`) |

**반환**

`.Data`는 `MatchRecord` 목록입니다. 기록이 없으면 빈 목록입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `SessionId` | `string` | 신고할 때 보낸 세션 ID |
| `GameCode` | `string` | 게임 코드 |
| `IsWin` | `bool` | 본인 기준 승패 |
| `Score` | `double?` | 신고할 때 보낸 점수. 보내지 않았으면 `null` |
| `Status` | `MatchRecordStatus` | `Verified`·`Pending`·`Mismatch`·`Solo`. 서버가 SDK보다 새 상태를 돌려주면 `Unknown` |
| `OpponentAccountId` | `string` | 상대 계정 ID. 상대 없는 판이면 `null` |
| `ReportedAt` | `DateTimeOffset` | 신고한 시각 |
