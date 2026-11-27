# 상태 조회

```csharp
Task<SupabaseResult<SupabaseAppStatus>> Supabase.GetAppStatusAsync(string language = null)
```

이 앱이 업데이트해야 하는지와 점검 중인지 조회합니다. 로그인하지 않아도 됩니다. 로그인 중에 부르면 그 계정이 [테스트 플레이어](/guide/app-status/#operators)으로 점검·최소 버전을 통과하는지도 반영합니다.

```csharp
var status = await Supabase.GetAppStatusAsync();
if (status.IsSuccess && status.Data.UpdateRequired && status.Data.StoreUrl != null)
    Application.OpenURL(status.Data.StoreUrl);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `language` | 점검 안내 문구의 언어. 비우면 기기 언어 (기본값: null) |

**반환**

`.Data`는 `SupabaseAppStatus`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `UpdateRequired` | `bool` | 이 앱의 버전이 최소 버전보다 낮고 버전 통과 테스트 플레이어가 아니면 true |
| `VersionAllowed` | `bool` | 버전 통과 테스트 플레이어로 로그인해 있으면 true. 로그인 전에는 false |
| `MinVersion` | `string` | 이 플랫폼의 최소 버전. 정하지 않았으면 null |
| `StoreUrl` | `string` | 업데이트 안내에서 열 스토어 주소. 모르면 null |
| `InMaintenance` | `bool` | 지금 점검 중이고 이 계정이 점검 통과 테스트 플레이어가 아니면 true |
| `MaintenanceAllowed` | `bool` | 점검 통과 테스트 플레이어로 로그인해 있으면 true. 로그인 전에는 false |
| `MaintenanceMessage` | `string` | 점검 안내 문구. 그 언어 문구가 없으면 영어, 영어도 없으면 기본 문구. 점검을 켜 두지 않았으면 null |
| `MaintenanceStartsAt` | `DateTimeOffset?` | 점검 시작 시각. 지금보다 뒤면 예정된 점검. 화면에 보일 때는 `ToLocalTime()` |
| `MaintenanceEndsAt` | `DateTimeOffset?` | 점검 예상 종료 시각. 화면에 보일 때는 `ToLocalTime()` |

**실사용 예시**

타이틀 화면에서 로그인 전에 확인해, 업데이트가 필요하면 스토어로 보내고 점검 중이면 안내를 띄웁니다. 예정된 점검은 미리 알립니다.

```csharp
public async void OnTitleReady()
{
    var status = await Supabase.GetAppStatusAsync(GameSettings.LanguageCode);
    if (status.IsSuccess)
    {
        var s = status.Data;
        if (s.UpdateRequired && s.StoreUrl != null) { ShowPopup("새 버전이 나왔습니다", onConfirm: () => Application.OpenURL(s.StoreUrl)); return; }
        if (s.InMaintenance) { ShowMaintenancePopup(s.MaintenanceMessage, s.MaintenanceEndsAt?.ToLocalTime()); return; }
        if (s.MaintenanceStartsAt > DateTimeOffset.UtcNow) ShowToast($"{s.MaintenanceStartsAt.Value.ToLocalTime():HH:mm}부터 점검이 있습니다");
    }

    var login = await Supabase.TriggerAutoLoginAsync();
    if (login.Reason == SupabaseReason.UpdateRequired) { ShowUpdatePopup(); return; }
    if (login.Reason == SupabaseReason.Maintenance) { ShowMaintenancePopup(null, null); return; }
}
```

::: tip 조회에 실패하면
네트워크가 끊겼거나 서버가 답하지 않으면 `IsSuccess`가 false입니다. 이때 막지 않고 로그인으로 넘어가도 됩니다. 최소 버전보다 낮으면 로그인이 `SupabaseReason.UpdateRequired`로, 점검 중이면 `SupabaseReason.Maintenance`로 실패합니다.
:::
