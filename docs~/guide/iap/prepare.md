# 사전 준비

결제 후 서버에서 영수증을 검증하고 아이템을 안전하게 지급하는 기능입니다.  
클라이언트에서 직접 아이템을 주는 방식과 달리, 서버가 결제 사실을 확인한 뒤에만 지급합니다.  
Android (Google Play)와 iOS (App Store) 소모품 아이템을 하나의 코드로 처리합니다.

[Database Setup](/guide/start/database-setup) 절차를 먼저 완료하세요.

이후 Package Manager에서 `com.unity.purchasing`을 설치합니다 — **최신 버전(최소 5.1) 권장**. 5.1부터 iOS SK1 강제를 포함한 전체 기능을 지원하며, 레거시 v4(4.x) 프로젝트는 최소 4.0.0부터 호환됩니다. [버전별 차이](./versions.md#iap-versions)를 참고하세요.
