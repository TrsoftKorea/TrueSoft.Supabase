# 리더보드 조회

```csharp
Task<SupabaseResult<LeaderboardTable>> Supabase.GetLeaderboardAsync<TLeaderboard>()
```

리더보드 1건의 설정과 현재 회차 상태를 조회합니다. 남은 시간 카운트다운이나 참여자 수 표시에 사용합니다.

```csharp
var result = await Supabase.GetLeaderboardAsync<ArenaLeaderboard>();
if (result.IsSuccess)
{
    var t = result.Data;
    countLabel.text = $"{t.TotalIds}명 참여";
    if (t.RotationTimeLeft.HasValue)
        timerLabel.text = FormatRemaining(t.RotationTimeLeft.Value);
}
```

**타입 파라미터**

| 파라미터 | 설명 |
|----------|------|
| `TLeaderboard` | [클래스 생성기](./columns#generate)로 만든 리더보드 타입 |

**반환**

`.Data`는 `LeaderboardTable`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Code` | `string` | 리더보드 코드 |
| `.DisplayName` | `string` | 표시명 |
| `.RecordType` | `LeaderboardRecordType` | 최고·최저·최신·누적 |
| `.SortType` | `LeaderboardSortType` | 정렬 방향 |
| `.Rotation` | `LeaderboardRotation` | 초기화 주기 |
| `.RotationCount` | `int` | 현재 회차 |
| `.RotationTimeLeft` | `int?` | 다음 회차까지 남은 초. 주기가 없으면 `null` |
| `.EndsAt` | `DateTimeOffset?` | 종료 예약 시각. 없으면 `null` |
| `.IsEnded` | `bool` | 종료·비활성 여부 |
| `.TotalIds` | `int` | 현재 회차 참여자 수 |
| `.Columns` | `IReadOnlyList<string>` | 등록된 플레이어 데이터 필드 이름 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
