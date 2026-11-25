# 기타 API

## 서버 시간

| 메서드 | 설명 |
|--------|------|
| [`GetServerNowAsync`](/guide/server-time/now) | 서버 기준 UTC 시각 조회 |
| [`GetServerNow`](/guide/server-time/now-sync) | 캐시된 기준점으로 즉시 반환 |

## 점검·버전

| 메서드 | 설명 |
|--------|------|
| [`GetAppStatusAsync`](/guide/app-status/status) | 최소 버전·점검 상태 조회 |
| [`OnMaintenanceStarted`](/guide/app-status/started) | 접속 중 점검 시작 알림 |
| [`OnUpdateRequired`](/guide/app-status/update-required) | 접속 중 최소 버전 미달 알림 |
