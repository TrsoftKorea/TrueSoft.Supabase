# 본인 기록 삭제

```csharp
Task<SupabaseResult> Supabase.DeleteMyScoreAsync<TLeaderboard>(int? rotationCount = null)
```

본인 기록을 리더보드에서 지웁니다. 기록이 없으면 아무것도 하지 않고 성공합니다.

```csharp
var result = await Supabase.DeleteMyScoreAsync<ArenaLeaderboard>();
if (result.IsSuccess)
    RefreshLeaderboard();
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `TLeaderboard` | [클래스 생성기](./columns#generate)로 만든 리더보드 타입 |
| `rotationCount` | 삭제할 회차. `null`이면 현재 회차 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |

::: info 다른 플레이어 기록 삭제
운영자가 특정 플레이어의 기록을 지우거나 점수를 정정하는 기능은 어드민(Retool)에 있습니다. 게임에서는 본인 기록만 지울 수 있습니다.
:::
