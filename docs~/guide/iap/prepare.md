# 사전 준비

## 설치

결제 후 서버에서 영수증을 검증하고 아이템을 안전하게 지급하는 기능입니다.  
클라이언트에서 직접 아이템을 주는 방식과 달리, 서버가 결제 사실을 확인한 뒤에만 지급합니다.  
Android (Google Play)와 iOS (App Store) 소모품 아이템을 하나의 코드로 처리합니다.

[데이터베이스 설정](/guide/start/database-setup) 절차를 먼저 완료하세요.

이후 Package Manager에서 `com.unity.purchasing`을 설치합니다 — **5.1 이상의 최신 버전을 권장합니다.** [버전별 차이](./versions.md#iap-versions)를 참고하세요.

## 환불 기록 {#refunds}

스토어에서 환불된 결제는 운영 콘솔 구매 내역에 취소선으로 표시되고, 플레이어의 총 결제금액에서 빠집니다. 게임 코드는 바꿀 것이 없습니다.

| 스토어 | 준비 |
|--------|------|
| Google&nbsp;Play | `purchase-refund-sync-google` 배포와 [프로젝트 주소 등록](/guide/start/database-setup#project-url). 하루 한 번 자동으로 확인합니다 |
| App&nbsp;Store | `purchase-notify-apple` 배포 후 App Store Connect에 알림 주소 등록. 환불이 생기면 애플이 바로 알려 줍니다 |

App Store Connect에서 앱을 고르고 **일반 > 앱 정보 > App Store Server Notifications**로 갑니다. **Production Server URL**과 **Sandbox Server URL**에 아래 주소를 넣고 **Version 2**를 고릅니다.

```text
https://<project-ref>.supabase.co/functions/v1/purchase-notify-apple
```

::: warning 이미 다른 주소가 있으면 바꾸기 전에 확인하세요
App Store Connect에는 환경마다 주소를 하나만 넣을 수 있습니다. 다른 서비스가 그 주소로 알림을 받고 있었다면, 바꾸는 순간 그 서비스는 알림을 받지 못합니다.
:::

::: info 환불돼도 아이템은 회수되지 않습니다
기록과 표시만 합니다. 차단이나 아이템 회수는 운영자가 플레이어 상세에서 환불 내역을 보고 정합니다.
:::
