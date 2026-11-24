# 점검·버전

오래된 앱과 점검 중의 접속을 막습니다. 운영 콘솔 **설정 › 점검·버전**에서 플랫폼별 최소 버전과 점검을 정하면, 게임은 로그인 전에 상태를 읽어 업데이트 안내나 점검 안내를 띄웁니다. 안내 화면은 게임이 그립니다.

::: warning 이 기능을 넣은 빌드부터 막힙니다
업데이트 안내는 상태를 읽는 빌드만 띄웁니다. 그 전에 나간 빌드는 최소 버전을 올려도 그대로 들어옵니다. 점검은 서버가 모든 빌드의 요청을 거절하지만, 옛 빌드는 안내 없이 요청이 실패합니다. 막아야 할 판이 생기기 전, 첫 출시 빌드에 넣으세요.
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

## 점검 중에 들어갈 계정 {#allow}

운영 콘솔에 유저 ID를 등록한 계정은 점검 중에도 들어갑니다. 로그인하기 전에는 어느 계정인지 모르므로 상태 조회는 점검 중이라고 답합니다. 운영자가 점검 중 게임을 확인하려면 점검 안내에서 로그인을 시도하는 길을 두세요. 등록한 계정이면 로그인이 성공하고, 아니면 `SupabaseReason.Maintenance`로 실패합니다.

## 접속 중에 점검이 시작되면 {#started}

점검 시작 시각이 되면 서버가 로그인한 플레이어의 요청을 거절합니다. SDK는 거절을 받는 순간 세션을 끊고 [점검 시작](/guide/app-status/started)을 알립니다. 아무것도 읽거나 쓰지 않는 동안에도 60초마다 보내는 접속 확인에서 잡히므로, 늦어도 1분 안에 알게 됩니다. 기기에 저장된 로그인은 지우지 않아 점검이 끝나면 자동 로그인으로 그대로 들어옵니다.

::: info 예상 종료 시각
예상 종료 시각은 안내용입니다. 그 시각이 지나도 운영자가 점검을 끌 때까지 점검이 이어집니다.
:::

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`GetAppStatusAsync`](/guide/app-status/status) | 최소 버전·점검 상태 조회 |
| [`OnMaintenanceStarted`](/guide/app-status/started) | 접속 중 점검 시작 알림 |
