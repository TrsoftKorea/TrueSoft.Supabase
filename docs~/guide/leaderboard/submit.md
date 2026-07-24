# 점수 기록

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitLeaderboardScoreAsync(
    string                              code,
    double                              score,
    IReadOnlyDictionary<string, object> data = null)
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
| `data` | 플레이어 데이터 필드 값. 이 리더보드에 등록된 필드만 허용 (기본값: `null`) |

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
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |

## 생성한 클래스로 기록

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitLeaderboardScoreAsync(
    double          score,
    ILeaderboardRow row)
```

[클래스 생성기](./columns#generate)로 만든 리더보드 행 타입을 그대로 넘기면 리더보드 코드와 필드 값을 행에서 읽어 씁니다. 사전을 직접 만들 필요가 없습니다.

```csharp
var row = new ArenaLeaderboardRow { CharLevel = 42, ClearTime = 87.3 };
await Supabase.SubmitLeaderboardScoreAsync(1250, row);
```

플레이어 데이터 필드를 함께 보내려면 [플레이어 데이터 필드](./columns)를 참고하세요.
