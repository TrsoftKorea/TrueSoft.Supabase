# 추가 데이터 수정

```csharp
Task<SupabaseResult> Supabase.SetLeaderboardPlayerDataAsync(
    string                              code,
    string                              extraData     = null,
    IReadOnlyDictionary<string, object> data          = null,
    int?                                rotationCount = null)
```

이미 기록된 본인 항목의 추가 데이터를 수정합니다. **점수는 바뀌지 않습니다.** 길드명이 바뀌거나 프로필 아이콘을 교체했을 때처럼 순위와 무관한 표시 정보만 갱신할 때 사용합니다.

```csharp
await Supabase.SetLeaderboardPlayerDataAsync(
    "arena",
    data: new Dictionary<string, object> { ["guild_name"] = "새 길드" });
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `code` | 리더보드 코드 |
| `extraData` | 자유 형식 문자열. `null`이면 기존 값 유지 (기본값: `null`) |
| `data` | 수정할 플레이어 데이터 컬럼 값 (기본값: `null`) |
| `rotationCount` | 수정할 회차. `null`이면 현재 회차 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `SupabaseReason.LeaderboardScoreNotFound` | 그 회차에 본인 기록이 없습니다 |
| `SupabaseReason.LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 컬럼을 보냈습니다 |
