# 수신 설정 바꾸기

```csharp
Task<SupabaseResult<SupabasePushPreferences>> Supabase.SetPushPreferencesAsync(
    bool? enabled       = null,
    bool? adConsent     = null,
    bool? nightConsent  = null,
    bool? mail          = null,
    bool? supportReply  = null,
    bool? friendRequest = null,
    bool? lobbyInvite   = null)
```

알림 전체·상황별 켜기와 광고·야간 수신 동의를 바꿉니다. 게임 설정 화면의 알림 스위치가 부르며, 바꾸지 않을 값은 넘기지 않습니다. 광고 수신 동의를 철회하면 야간 수신 동의도 함께 철회됩니다.

```csharp
public async void OnFriendAlarmToggle(bool on)
{
    var r = await Supabase.SetPushPreferencesAsync(friendRequest: on);
    if (!r.IsSuccess) friendAlarmToggle.SetIsOnWithoutNotify(!on);
}

public async void OnAdConsentToggle(bool on)
{
    var r = await Supabase.SetPushPreferencesAsync(adConsent: on);
    if (r.IsSuccess)
        ShowToast($"광고성 알림 수신 {(on ? "동의" : "거부")} · {r.Data.AdConsentAt?.ToLocalTime():yyyy-MM-dd HH:mm}");
}
```

광고 수신에 동의하거나 철회한 뒤에는 게임이 돌려받은 시각으로 처리 결과를 플레이어에게 알립니다. 시각은 UTC로 오므로 기기 시각으로 바꿔 보여 줍니다. 같은 값으로 다시 동의해도 시각이 새로 적혀, 주기적인 수신 동의 재확인 기록으로 쓸 수 있습니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `enabled` | 알림을 받을지. 끄면 상황별 값과 상관없이 모든 알림이 오지 않습니다 (기본값: null) |
| `adConsent` | 광고성 알림 수신 동의 (기본값: null) |
| `nightConsent` | 밤 9시~아침 8시 광고성 알림 수신 동의. 시각은 운영 콘솔에서 정한 프로젝트 시간대 기준입니다. 광고 수신 동의가 있어야 켤 수 있습니다 (기본값: null) |
| `mail` | 우편 도착 알림 (기본값: null) |
| `supportReply` | 고객센터 답변 알림 (기본값: null) |
| `friendRequest` | 친구 요청 알림 (기본값: null) |
| `lobbyInvite` | 매치 로비 초대 알림 (기본값: null) |

**반환**

`.Data`는 바뀐 뒤의 `SupabasePushPreferences`입니다. 프로퍼티는 [수신 설정 읽기](/guide/push/get-preferences)와 같습니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.ProfileNotFound` | 로그인한 계정의 플레이어 프로필이 없습니다. 로그인 후처리가 끝난 뒤 다시 부릅니다 |
| `SupabaseReason.PushNightRequiresAdConsent` | 광고 수신에 동의하지 않은 플레이어의 야간 수신 동의만 켜려 했습니다. `adConsent: true`와 함께 넘깁니다 |

**실사용 예시**

설정 화면의 야간 알림 스위치를 켤 때 광고 수신 동의가 없으면 함께 동의를 받습니다.

```csharp
public async void OnNightToggle(bool on)
{
    var result = await Supabase.SetPushPreferencesAsync(nightConsent: on);
    if (result.Reason == SupabaseReason.PushNightRequiresAdConsent)
    {
        if (!await ConfirmPopup("야간 알림을 받으려면 광고성 알림 수신에도 동의해야 합니다"))
        {
            nightToggle.SetIsOnWithoutNotify(false);
            return;
        }
        result = await Supabase.SetPushPreferencesAsync(adConsent: true, nightConsent: true);
    }

    if (!result.IsSuccess) { nightToggle.SetIsOnWithoutNotify(!on); return; }
    adToggle.SetIsOnWithoutNotify(result.Data.AdConsent);
}
```
