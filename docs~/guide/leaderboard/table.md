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
| `TLeaderboard` | [클래스 생성기](/guide/leaderboard/columns#generate)로 만든 리더보드 타입 |

**반환**

`.Data`는 `LeaderboardTable`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Code` | `string` | 리더보드 코드 |
| `DisplayName` | `string` | 표시명 |
| `RecordType` | `LeaderboardRecordType` | 최고·최저·최신·누적 |
| `SortType` | `LeaderboardSortType` | 정렬 방향 |
| `Rotation` | `LeaderboardRotation` | 초기화 주기 |
| `RotationCount` | `int` | 현재 회차 |
| `RotationTimeLeft` | `int?` | 다음 회차까지 남은 초. 주기가 없으면 `null` |
| `EndsAt` | `DateTimeOffset?` | 종료 예약 시각. 없으면 `null` |
| `IsEnded` | `bool` | 종료·비활성 여부 |
| `TotalIds` | `int` | 현재 회차 참여자 수 |
| `Columns` | `IReadOnlyList<string>` | 등록된 플레이어 데이터 필드 이름 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |

**실사용 예시**

순위 화면을 열 때 회차와 남은 시간을 띄우고, 끝난 리더보드면 도전 버튼을 막습니다.

```csharp
public async void OnOpenArenaRanking()
{
    var result = await Supabase.GetLeaderboardAsync<ArenaLeaderboard>();
    if (!result.IsSuccess) { ShowToast("순위표를 불러오지 못했습니다"); return; }

    var t = result.Data;
    seasonLabel.text = $"{t.DisplayName} {t.RotationCount}회차";
    challengeButton.interactable = !t.IsEnded;

    if (t.RotationTimeLeft.HasValue)   // 받은 뒤로는 화면이 직접 줄여 나갑니다
        StartCountdown(t.RotationTimeLeft.Value, onZero: OnOpenArenaRanking);
}
```
