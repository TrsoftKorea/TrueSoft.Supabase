# 수신 설정 읽기

```csharp
Task<SupabaseResult<SupabasePushPreferences>> Supabase.GetPushPreferencesAsync()
```

내 푸시 수신 설정을 읽습니다. 게임 설정 화면의 알림 스위치를 채울 때 씁니다. 한 번도 바꾸지 않았으면 알림과 상황별 알림은 모두 켬, 광고·야간 수신은 미동의입니다.

```csharp
var pref = await Supabase.GetPushPreferencesAsync();
if (pref.IsSuccess)
{
    pushToggle.SetIsOnWithoutNotify(pref.Data.Enabled);
    friendAlarmToggle.SetIsOnWithoutNotify(pref.Data.FriendRequest);
    adToggle.SetIsOnWithoutNotify(pref.Data.AdConsent);
}
```

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Enabled` | `bool` | 알림을 받는지. 끄면 상황별 값과 상관없이 오지 않습니다 |
| `Mail` | `bool` | 우편 도착 알림 |
| `SupportReply` | `bool` | 고객센터 답변 알림 |
| `FriendRequest` | `bool` | 친구 요청 알림 |
| `LobbyInvite` | `bool` | 매치 로비 초대 알림 |
| `AdConsent` | `bool` | 광고성 알림 수신 동의 |
| `AdConsentAt` | `DateTimeOffset?` | 광고 수신 동의·철회가 마지막으로 바뀐 시각 |
| `NightConsent` | `bool` | 밤 9시~아침 8시 광고성 알림 수신 동의 |
| `NightConsentAt` | `DateTimeOffset?` | 야간 수신 동의·철회가 마지막으로 바뀐 시각 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
