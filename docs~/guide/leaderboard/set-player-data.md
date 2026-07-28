# 추가 데이터 수정

```csharp
Task<SupabaseResult> Supabase.SetRowAsync<TRow>(TRow row, int? rotationCount = null)
```

이미 기록된 본인 항목의 등록 필드를 수정합니다. **점수는 바뀌지 않습니다.** 길드명이 바뀌거나 프로필 아이콘을 교체했을 때처럼 순위와 무관한 표시 정보만 갱신할 때 사용합니다. 어느 리더보드인지는 행 타입에서 읽습니다.

```csharp
await Supabase.SetRowAsync(new ArenaLeaderboard.Row { GuildName = "새 길드" });
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `row` | [클래스 생성기](./columns#generate)가 만든 중첩 `Row` 인스턴스 |
| `rotationCount` | 수정할 회차. `null`이면 현재 회차 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LeaderboardRowRequired` | 행 객체가 null입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardScoreNotFound` | 그 회차에 본인 기록이 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 필드를 보냈습니다 |

::: info 행에 담긴 값은 전부 전송됩니다
`Row`의 필드는 값을 바꾸지 않은 것까지 모두 보내집니다. 일부만 고치려면 [순위 조회](./range)로 받은 값을 [`Supabase.ToRow`](./columns#to-row)로 되돌린 뒤 고쳐서 보내세요.
:::
