# 리더보드 목록 조회

```csharp
Task<SupabaseResult<IReadOnlyList<LeaderboardTable>>> Supabase.GetLeaderboardsAsync()
```

사용 가능한 리더보드 목록을 조회합니다. 비활성이거나 종료된 리더보드는 빠집니다.

```csharp
var result = await Supabase.GetLeaderboardsAsync();
if (result.IsSuccess)
{
    foreach (var t in result.Data)
        AddLeaderboardTab(t.Code, t.DisplayName);
}
```

**반환**

`.Data`에 `LeaderboardTable` 목록이 코드 오름차순으로 담깁니다. 목록 응답에는 참여자 수와 등록 컬럼이 포함되지 않으므로, 그 값이 필요하면 [리더보드 조회](./table)를 사용하세요.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip 리더보드 탭을 하드코딩하지 않기
이 메서드로 탭을 구성하면 운영자가 Retool에서 리더보드를 추가·종료할 때 클라이언트를 다시 빌드하지 않아도 됩니다.
:::
