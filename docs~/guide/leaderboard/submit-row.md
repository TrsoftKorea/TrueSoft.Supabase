# 점수 기록 · 생성 클래스

```csharp
Task<SupabaseResult<LeaderboardSubmitResult>> Supabase.SubmitLeaderboardScoreAsync(
    double          score,
    ILeaderboardRow row)
```

[클래스 생성기](./columns#generate)로 만든 리더보드 행 타입을 그대로 넘기면 리더보드 코드와 필드 값을 행에서 읽어 씁니다. 사전을 직접 만들 필요가 없습니다. 사전으로 직접 보내는 방식은 [점수 기록](./submit)을 참고하세요.

```csharp
var row = new ArenaLeaderboardRow { CharacterLevel = 42, GuildName = "붉은검" };
await Supabase.SubmitLeaderboardScoreAsync(1250, row);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `score` | 이번에 획득한 점수 |
| `row` | 생성기로 만든 리더보드 행 인스턴스. 리더보드 코드·필드 값을 여기서 읽습니다 |

**반환**

`.Data`는 `LeaderboardSubmitResult`이며 `.Score`(최종 점수)·`.RotationCount`(기록된 회차)를 담습니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardRowRequired` | 행 객체가 null입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardEnded` | 종료·비활성 리더보드라 기록할 수 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |
