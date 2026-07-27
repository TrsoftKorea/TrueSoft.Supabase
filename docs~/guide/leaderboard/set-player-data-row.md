# 추가 데이터 수정 · 생성 클래스

```csharp
Task<SupabaseResult> Supabase.SetLeaderboardPlayerDataAsync(
    ILeaderboardRow row,
    int?            rotationCount = null)
```

[클래스 생성기](./columns#generate)로 만든 리더보드 행 타입을 그대로 넘기면 리더보드 코드와 필드 값을 행에서 읽어 씁니다. 점수는 바뀌지 않습니다. 사전으로 직접 보내는 방식은 [추가 데이터 수정](./set-player-data)을 참고하세요.

```csharp
var row = new ArenaLeaderboardRow { GuildName = "새 길드" };
await Supabase.SetLeaderboardPlayerDataAsync(row);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `row` | 생성기로 만든 리더보드 행 인스턴스. 리더보드 코드·필드 값을 여기서 읽습니다 |
| `rotationCount` | 수정할 회차. `null`이면 현재 회차 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardRowRequired` | 행 객체가 null입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardScoreNotFound` | 그 회차에 본인 기록이 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |
