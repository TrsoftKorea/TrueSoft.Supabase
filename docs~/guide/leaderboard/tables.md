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

`.Data`는 코드 오름차순의 `LeaderboardTable` 목록입니다. 목록 응답에는 참여자 수와 등록 컬럼이 포함되지 않으므로, 그 값이 필요하면 [리더보드 조회](/guide/leaderboard/table)를 사용하세요.

**실사용 예시**

순위 화면을 열 때 탭을 만들고 첫 탭의 순위를 바로 보여 줍니다.

```csharp
public async void OnOpenRankingScreen()
{
    var result = await Supabase.GetLeaderboardsAsync();
    if (!result.IsSuccess) { ShowToast("순위표를 불러오지 못했습니다"); return; }
    if (result.Data.Count == 0) { emptyLabel.SetActive(true); return; }   // 진행 중인 리더보드가 없습니다

    foreach (var t in result.Data)
        AddLeaderboardTab(t.Code, t.DisplayName, onSelect: () => ShowRanking(t.Code));
    ShowRanking(result.Data[0].Code);
}
```

::: tip 리더보드 탭을 하드코딩하지 않기
이 메서드로 탭을 구성하면 운영자가 운영 콘솔에서 리더보드를 추가·종료할 때 클라이언트를 다시 빌드하지 않아도 됩니다.
:::
