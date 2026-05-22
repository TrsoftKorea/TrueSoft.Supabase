# 애널리틱스

세션 추적과 이벤트 기록 기능입니다.  
로그인 시 세션이 자동으로 시작되고, 앱 실행 중에는 활성 상태가 유지됩니다.  
광고 재생 등 게임 내 이벤트는 한 줄 호출로 기록할 수 있습니다.

---

## 사전 준비

[빠른 시작](./getting-started.md)의 **Database Setup** 절차를 먼저 완료하세요.  
`SQL/player/10_analytics.sql`을 Supabase SQL Editor에서 실행합니다.

---

## 세션 추적

로그인이 완료되면 세션이 **자동으로 시작**됩니다.  
앱 실행 중에는 5분마다 활성 시각이 자동 갱신되고, 앱 종료·백그라운드 전환 시 세션이 자동으로 닫힙니다.  
`SupabaseRuntime`이 씬에 배치되어 있으면 별도 코드 없이 동작합니다.

현재 세션 ID가 필요한 경우 읽을 수 있습니다.

```csharp
string sessionId = Supabase.SessionId;
```

로그인 전이거나 세션이 닫힌 경우 빈 문자열을 반환합니다.

---

## 이벤트 기록

광고 재생 완료, 레벨 클리어 등 임의 이벤트를 이름으로 기록합니다.  
`account_id` / `user_id` / `session_id`는 자동으로 주입됩니다.

```csharp
// 실패 시 경고 로그 자동 출력
await Supabase.TryRecordAnalyticsEventAsync("ad_rewarded_complete");
```

이벤트 이름은 자유롭게 정의할 수 있습니다. 예시:

| 이벤트 이름 | 시점 |
|-------------|------|
| `ad_rewarded_start` | 보상형 광고 시청 시작 |
| `ad_rewarded_complete` | 보상형 광고 시청 완료 |
| `ad_interstitial_show` | 전면 광고 노출 |
| `tutorial_complete` | 튜토리얼 완료 |

---

## DB 테이블 구조

**`analytics_sessions`** — 세션 라이프사이클

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `session_id` | text | 세션 고유 ID (UUID) |
| `account_id` | uuid | 로그인 계정 ID |
| `user_id` | text | 영구 플레이어 ID |
| `started_at` | timestamptz | 세션 시작 시각 |
| `last_active_at` | timestamptz | 마지막 활성 시각 (5분마다 갱신) |
| `ended_at` | timestamptz | 세션 종료 시각 |
| `platform` | text | 플랫폼 (`android` / `ios` / `windows` / `macos`) |
| `app_version` | text | 앱 버전 |
| `is_closed` | boolean | 세션 종료 여부 |

**`analytics_events`** — 이름 기반 이벤트

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `account_id` | uuid | 로그인 계정 ID |
| `user_id` | text | 영구 플레이어 ID |
| `session_id` | text | 이벤트 발생 시점의 세션 ID |
| `event_name` | text | 이벤트 이름 |
| `event_time` | timestamptz | 이벤트 발생 시각 (자동) |
