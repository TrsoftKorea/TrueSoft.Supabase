# 푸시 알림

운영자가 우편을 보내거나 문의에 답했을 때, 친구 요청이나 로비 초대를 받았을 때 플레이어 기기로 알림이 갑니다. 보내는 일은 서버가 하므로 게임은 기기를 등록하고, 설정 화면에 알림 스위치와 수신 동의를 두기만 하면 됩니다. 문구는 운영 콘솔 **설정 › 푸시 알림**에서 정합니다.

## 준비 {#setup}

1. [Firebase 콘솔](https://console.firebase.google.com)에서 프로젝트를 만들고 Android 앱을 추가합니다. 패키지 이름은 게임과 같아야 합니다.
2. `SupabaseSettings`의 **푸시 알림 사용**을 켜고, 아래 [Firebase 값](#firebase-values) 네 칸을 넣습니다.
3. **Android 알림 아이콘**에 흰색 그림에 배경이 투명한 96×96 PNG를 넣습니다. 비우면 상태 표시줄에 흰 네모로 보일 수 있습니다.
4. Apple Developer에서 앱의 **Push Notifications** 기능을 켜고 APNs 키(.p8)를 만듭니다.
5. 서버를 준비합니다. `install.sql`을 실행하고, 엣지 함수 `push-send`를 **Verify JWT**를 끈 채 [배포](/guide/start/database-setup#edge-function-deploy)하고, [프로젝트 주소](/guide/start/database-setup#project-url)를 등록합니다. 프로젝트 주소가 없으면 알림이 나가지 않습니다.
6. 운영 콘솔 **설정 › 서비스 연결 › 푸시 알림**에 Firebase 서비스 계정 키와 APNs 키를 넣습니다. 키를 가져오는 방법은 그 화면의 각 칸에 있습니다. Team ID·번들 ID는 Sign in with Apple·App Store에 넣은 값과 같은 칸이라 한 곳에 넣으면 함께 채워집니다.
7. 운영 콘솔 **설정 › 푸시 알림**에서 상황별 문구를 확인합니다. 기본 문구가 한국어·영어로 들어 있습니다.
8. 게임 코드에서 로그인 뒤 [기기 등록](#register)을 부르고, 설정 화면에 [수신 동의](#consent)와 [알림 스위치](#toggles)를 둡니다.

::: warning 유니티 판
Android 푸시 부품은 유니티 2022.3.38f1 이상이 필요합니다. 그보다 오래된 2022.3은 빌드 도구가 Firebase의 최소 요구보다 낮습니다.
:::

::: info 빌드에 들어가는 것
**푸시 알림 사용**을 켠 게임만 Android 빌드에 Firebase 메시징 부품과 알림 권한이, iOS 빌드에 Push Notifications 기능과 기기 토큰을 받는 설정이 들어갑니다. 따로 손댈 매니페스트·Xcode 설정은 없습니다. 게임에 Firebase 유니티 패키지가 이미 있으면 그 패키지의 Firebase 설정을 그대로 씁니다.
:::

::: warning 알림 아이콘을 다른 곳에서 지정한 게임
게임이 다른 플러그인이나 매니페스트에서 Firebase 알림 아이콘을 이미 지정했다면 **Android 알림 아이콘** 칸을 비웁니다. 두 곳에서 다른 그림을 지정하면 빌드가 멈출 수 있습니다.
:::

::: warning Firebase 유니티 메시징과 함께 쓸 수 없음
게임에 Firebase 유니티 **메시징** 패키지가 있으면 Android 빌드가 안내와 함께 멈춥니다. 두 푸시가 같은 알림 신호를 나눠 받아 어느 쪽 알림이 끊길지 알 수 없기 때문입니다. 둘 중 하나만 씁니다.
:::

## 처음 시험하기 {#first-test}

1. 기기에 빌드해 로그인하고 [기기 등록](#register)이 성공하는지 봅니다. 운영 콘솔 **설정 › 푸시 알림** 위쪽의 기기 수가 늘어납니다.
2. 게임을 홈 화면으로 내리거나 끕니다. 게임이 화면에 떠 있으면 알림이 기기에 뜨지 않습니다.
3. 운영 콘솔 **우편함 › 우편 발송**에서 받는 사람을 시험 계정으로 고르고 **푸시 알림**을 켠 채 보냅니다.
4. 알림이 오지 않으면 **설정 › 푸시 알림 › 보낸 결과**의 최근 실패·건너뜀에서 이유를 봅니다. 운영 콘솔 **자동 작업**의 "남은 푸시 알림 보내기"가 실패로 나오면 서버 준비를 다시 확인합니다.

## 기기 등록 {#register}

로그인한 뒤, 실행할 때마다 [기기 등록](/guide/push/register)을 한 번 부릅니다. 처음 부를 때 기기가 알림 권한을 묻습니다.

로그아웃(`Supabase.SignOutFullyAsync`)하면 이 기기의 등록이 풀려 다음 계정이 앞 계정의 알림을 받지 않습니다. 다른 기기에서 같은 계정으로 로그인해 이 기기가 밀려난 경우에는 등록이 남아, 이 기기로 다시 로그인해 등록할 때까지 그 계정의 알림이 옵니다.

## 수신 동의 {#consent}

광고성 알림은 광고 수신에 동의한 플레이어에게만 갑니다. 밤 9시부터 아침 8시 사이에 보낸 광고성 알림은 야간 수신에도 동의한 플레이어에게만 바로 가고, 나머지 동의자에게는 아침 8시에 갑니다. 이 시각은 플레이어 기기가 아니라 운영 콘솔 **설정 › 시간대**의 게임 시간대 기준입니다. 친구 요청·고객센터 답변처럼 광고가 아닌 알림은 밤에도 바로 갑니다.

동의 창은 게임이 띄우고 결과를 SDK에 넘깁니다. 야간 수신은 광고 수신에 동의해야 켤 수 있습니다.

```csharp
public async void OnClickAgreeAds(bool agree, bool agreeNight)
{
    var r = await Supabase.SetPushPreferencesAsync(adConsent: agree, nightConsent: agree && agreeNight);
    if (r.IsSuccess)
        ShowToast($"광고성 알림 수신 {(r.Data.AdConsent ? "동의" : "거부")} · {r.Data.AdConsentAt?.ToLocalTime():yyyy-MM-dd HH:mm}");
}
```

동의하거나 철회한 뒤 처리 결과와 그 시각을 플레이어에게 알리는 것은 게임 몫입니다. 돌려받은 `AdConsentAt`이 그 시각입니다.

## 알림 스위치 {#toggles}

플레이어가 상황마다 알림을 켜고 끕니다. 처음에는 모두 켜져 있고, 끈 알림은 그 플레이어에게 가지 않습니다.

```csharp
await Supabase.SetPushPreferencesAsync(enabled: allToggle.isOn);       // 알림 전체
await Supabase.SetPushPreferencesAsync(friendRequest: friendToggle.isOn);
await Supabase.SetPushPreferencesAsync(lobbyInvite: inviteToggle.isOn);
```

`enabled`를 끄면 상황별 값과 상관없이 모든 알림이 오지 않습니다. 스위치를 처음 채울 때는 [수신 설정 읽기](/guide/push/get-preferences)를 씁니다.

## 게임이 켜져 있을 때 {#foreground}

게임이 화면에 떠 있는 동안 온 알림은 기기에 띄우지 않고 [게임 중에 온 알림](/guide/push/received) 이벤트로 게임에 넘깁니다. 우편 배지·새 답변 표시는 그 이벤트에서 새로 고칩니다. 게임이 뒤에 있거나 꺼져 있을 때 온 알림을 누르면 게임이 열립니다. 어떤 알림을 눌렀는지는 게임에 전달되지 않습니다.

## 알림이 가는 상황 {#triggers}

| 상황 | 언제 | 문구에 넣을 수 있는 값 |
|------|------|------|
| 우편&nbsp;도착 | 운영자가 우편을 보낼 때 "푸시 알림"을 켠 우편. 예약·반복 발송도 됩니다 | `{title}` 우편 제목 |
| 고객센터&nbsp;답변 | 운영자가 문의에 답했을 때 | 없음 |
| 친구&nbsp;요청 | 새 친구 요청을 받았을 때 | `{name}` 보낸 플레이어 닉네임 |
| 로비&nbsp;초대 | 매치 로비에 초대받았을 때 | `{name}` 초대한 플레이어 닉네임, `{lobby}` 방 이름 |

문구는 기기 언어로 고릅니다. 운영 콘솔에서 추가한 언어가 없으면 기본 문구가 갑니다. 광고성으로 보낸 우편 알림은 제목 앞에 `(광고)`가, 본문 끝에 수신 거부 안내가 붙습니다.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`RegisterPushAsync`](/guide/push/register) | 알림 권한을 묻고 기기 등록 |
| [`GetPushPreferencesAsync`](/guide/push/get-preferences) | 내 수신 설정 읽기 |
| [`SetPushPreferencesAsync`](/guide/push/set-preferences) | 알림 전체·상황별 켜기, 광고·야간 수신 동의 |
| [`OnPushReceived`](/guide/push/received) | 게임이 화면에 떠 있는 동안 온 알림 이벤트 |

## Firebase 값 {#firebase-values}

`google-services.json` 파일은 게임에 넣지 않습니다. 빌드할 때 SDK가 아래 네 값으로 같은 설정을 만듭니다. 값은 Firebase 콘솔의 **프로젝트 설정 › 일반**에서 보거나, 그 화면에서 내려받은 `google-services.json`에서 옮겨 적습니다.

| SupabaseSettings 칸 | `google-services.json` 위치 |
|------|------|
| Firebase&nbsp;프로젝트&nbsp;ID | `project_info.project_id` |
| Firebase&nbsp;Android&nbsp;앱&nbsp;ID | `client[].client_info.mobilesdk_app_id` |
| Firebase&nbsp;웹&nbsp;API&nbsp;키 | `client[].api_key[].current_key` |
| Firebase&nbsp;발신자&nbsp;ID | `project_info.project_number` |

`client`가 여럿이면 `client_info.android_client_info.package_name`이 게임 패키지 이름과 같은 것을 고릅니다.
