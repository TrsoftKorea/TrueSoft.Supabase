# 서버 시각 조회

```csharp
Task<SupabaseResult<DateTimeOffset>> Supabase.GetServerNowAsync()
```

서버 기준 UTC 시각을 반환합니다. 기준점이 있으면 네트워크 없이, 없으면 서버에서 받아 돌려줍니다.

```csharp
var t = await Supabase.GetServerNowAsync();
if (!t.IsSuccess) return;

if (t.Data < eventEndUtc)
    GrantEventReward();
```

**반환**

`.Data`는 서버 기준 UTC 시각 `DateTimeOffset`입니다.

**실사용 예시**

출석 보상 버튼에서 서버 시각으로 오늘 받았는지 가려, 기기 시계를 바꿔도 두 번 받지 못하게 합니다.

```csharp
public async void OnClickDailyReward()
{
    var now = await Supabase.GetServerNowAsync();
    if (!now.IsSuccess) { ShowToast("시간을 확인하지 못했습니다. 잠시 뒤 다시 눌러 주세요"); return; }

    var today = now.Data.ToOffset(TimeSpan.FromHours(9)).Date;   // 게임이 하루를 자르는 시간대. 여기서는 한국
    if (PlayerSave.LastDailyRewardDate == today) { ShowToast("오늘은 이미 받았습니다"); return; }

    PlayerSave.LastDailyRewardDate = today;
    PlayerSave.Gold += 100;
    Supabase.RequestSave();
    ShowRewardPopup("gold", 100);
}
```
