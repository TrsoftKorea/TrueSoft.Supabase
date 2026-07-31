---
name: project_retool_table_status_row_rollout
description: 트루베이스 Retool 앱 표 로딩 UI 통일 작업 — 신규 컴포넌트 TableStatusRow 롤아웃 진행 상황
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
  modified: 2026-07-27T09:02:49.081Z
---

트루베이스 Retool React 앱(`b518a11a-5d80-...`)에서 표 로딩·빈 상태 UI를 공통 컴포넌트 `/frontend/pages/ui/TableStatusRow.tsx`(스피너 행 스타일)로 통일하는 작업이 다른 세션(2026-07-13)에서 진행 중.

**적용 완료(해당 세션 보고 기준, 미검증)**: GameItems·PlayerList·Purchases·Mails. 이후 leaderboard 3탭(LeaderboardsTab·ScoresTab)도 사용 중.
**미적용 — 2026-07-27 파일 직접 확인**: `RemoteConfig`·`data/ColumnManagementTab` 둘 다 로딩/빈 상태를 자체 `<tr><td>`로 구현(TableStatusRow import 없음).
**미확인**: `DataLogs`(표 2개)·`data/PlayerDataTab` — 열어보지 않음.

**Why:** 페이지마다 로딩/빈 상태를 각자 구현해 스타일이 제각각이었음.

**How to apply:** Retool 관련 요청을 받으면 이 롤아웃이 완료됐는지 먼저 확인(`retool_list_react_app_files`로 `TableStatusRow` import 여부 확인) — 특히 `DataLogs`·`RemoteConfig`·`ColumnManagementTab`·`PlayerDataTab`을 건드릴 때 이 통일 작업과 충돌하지 않는지 주의. `TableStatusRow.tsx` 자체는 스레드로 생성되어 **게시 전에는 main에 없을 수 있음** — 사용 전 먼저 파일 존재를 확인할 것. 게시 순서 함정은 [[project_retool_thread_publish_hygiene]] 참고.
