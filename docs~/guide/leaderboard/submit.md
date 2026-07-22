# 점수 기록

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitLeaderboardScoreAsync(
    string                              code,
    double                              score,
    string                              extraData = null,
    IReadOnlyDictionary<string, object> data      = null)
```

본인 점수를 기록합니다. 최고·최저·최신·누적 중 어떤 방식으로 반영될지는 리더보드 설정에 따라 서버가 결정하므로, 게임은 항상 이번에 얻은 점수를 그대로 보내면 됩니다.

```csharp
var result = await Supabase.SubmitLeaderboardScoreAsync("arena", 1250);
if (result.IsSuccess)
{
    // 기록 방식이 적용된 뒤의 최종 점수
    scoreLabel.text = result.Data.Score.ToString();
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `code` | 리더보드 코드 |
| `score` | 이번에 획득한 점수 |
| `extraData` | 자유 형식 문자열 (기본값: `null`) |
| `data` | 플레이어 데이터 컬럼 값. 이 리더보드에 등록된 컬럼만 허용 (기본값: `null`) |

**반환**

`.Data`는 `LeaderboardSubmitResult`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Score` | `double` | 기록 방식이 적용된 뒤의 최종 점수 |
| `.RotationCount` | `int` | 기록된 회차 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardEnded` | 종료·비활성 리더보드라 기록할 수 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 컬럼을 보냈습니다 |

플레이어 데이터 컬럼을 함께 보내려면 [플레이어 데이터 컬럼](./columns)을 참고하세요.
