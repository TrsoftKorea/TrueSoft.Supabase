# 점검·버전

오래된 앱과 점검 중의 접속을 막습니다. 운영 콘솔 **설정 › 접속**에서 플랫폼별 최소 버전과 점검을 정하면, 게임은 로그인 전에 상태를 읽어 업데이트 안내나 점검 안내를 띄웁니다. 안내 화면은 게임이 그립니다.

::: warning 이 기능을 넣은 빌드부터 막힙니다
업데이트 안내는 상태를 읽는 빌드만 띄웁니다. 그 전에 나간 빌드는 최소 버전을 올려도 그대로 들어옵니다. SDK 0.6.0 부터는 서버도 낮은 버전의 요청을 거절하지만, 그 전 SDK 로 나간 빌드는 서버가 버전을 몰라 막지 못합니다. 점검은 서버가 모든 빌드의 요청을 거절하지만, 옛 빌드는 안내 없이 요청이 실패합니다. 막아야 할 판이 생기기 전, 첫 출시 빌드에 넣으세요.
:::

## 타이틀에서 확인하기 {#flow}

타이틀 화면에서 로그인하기 전에 [상태 조회](/guide/app-status/status)를 부릅니다. 최소 버전보다 낮으면 스토어로 보내고, 점검 중이면 점검 안내를 띄운 뒤 로그인하지 않습니다.

```csharp
var status = await Supabase.GetAppStatusAsync();
if (status.IsSuccess && status.Data.UpdateRequired) { ShowUpdatePopup(status.Data.StoreUrl); return; }   // iOS 는 StoreUrl 이 null 일 수 있습니다
if (status.IsSuccess && status.Data.InMaintenance) { ShowMaintenancePopup(status.Data); return; }

await Supabase.TriggerAutoLoginAsync();
```

최소 버전은 `Application.version`을 점으로 나눈 숫자끼리 비교합니다. 운영 콘솔에서 비워 두면 막지 않습니다. 스토어 주소는 Android는 비워 두면 패키지 이름으로 Google Play 주소를 만들고, iOS는 운영 콘솔에 App Store 주소를 넣어야 채워집니다.

## 운영 계정 {#operators}

운영 콘솔 **설정 › 접속**에 유저 ID를 등록하고 권한을 켠 계정은 점검 중이나 최소 버전보다 낮은 앱으로도 들어갑니다. 로그인하기 전에는 어느 계정인지 모르므로 상태 조회는 점검 중·업데이트 필요라고 답합니다. 운영자가 확인하려면 점검 안내와 업데이트 안내에 눈에 띄지 않는 로그인 입구를 두세요. 권한이 있는 계정이면 로그인이 성공하고, 아니면 `SupabaseReason.Maintenance`나 `SupabaseReason.UpdateRequired`로 실패합니다. 플레이어가 입구를 찾아도 로그인은 막힙니다.

```csharp
// 업데이트 안내의 로고를 다섯 번 누르면 로그인을 시도합니다
async void OnHiddenEntrance()
{
    var login = await Supabase.TriggerAutoLoginAsync();
    if (login.IsSuccess) EnterLobby();
}
```

운영 계정에는 점검·버전 통과 말고도 리더보드 순위와 운영 콘솔 통계에서 빼기, [원격 설정](/guide/remote-config/)의 운영 계정 값 받기, 우편 시험 발송 받기 권한이 있습니다. 계정마다 따로 켭니다.

## 접속 중에 점검이 시작되면 {#started}

점검 시작 시각이 되면 서버가 로그인한 플레이어의 요청을 거절합니다. SDK는 거절을 받는 순간 세션을 끊고 [점검 시작](/guide/app-status/started)을 알립니다. 아무것도 읽거나 쓰지 않는 동안에도 60초마다 보내는 접속 확인에서 잡히므로, 늦어도 1분 안에 알게 됩니다. 기기에 저장된 로그인은 지우지 않아 점검이 끝나면 자동 로그인으로 그대로 들어옵니다.

접속 중에 최소 버전을 올려도 같은 방식으로 끊기고 [업데이트 필요](/guide/app-status/update-required)를 알립니다. 점검과 최소 버전에 둘 다 걸리면 업데이트 필요가 먼저입니다.

::: info 예상 종료 시각
예상 종료 시각은 안내용입니다. 그 시각이 지나도 운영자가 점검을 끌 때까지 점검이 이어집니다.
:::

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`GetAppStatusAsync`](/guide/app-status/status) | 최소 버전·점검 상태 조회 |
| [`OnMaintenanceStarted`](/guide/app-status/started) | 접속 중 점검 시작 알림 |
| [`OnUpdateRequired`](/guide/app-status/update-required) | 접속 중 최소 버전 미달 알림 |
