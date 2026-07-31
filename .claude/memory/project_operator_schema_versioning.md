---
name: project_operator_schema_versioning
description: 운영자 스키마 변경 버전관리(스테이징→게시→롤백) 기능 진행 상황
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f192039-fca3-497f-bd1f-9da182660d0d
  modified: 2026-07-27T09:03:06.956Z
---

Retool 운영자가 라이브 스키마에 영향 주는 작업(리더보드 필드·테이블, user_data 필드, RemoteConfig 키)을 즉시 적용하지 않고 **draft로 스테이징 → "게시" 한 번에 원자 적용(한 트랜잭션) → 버전 단위 롤백**하는 기능. Retool 게시·git 커밋과 같은 심성. target(마왕/데슬)별 독립. 파괴적 변경(컬럼 drop·테이블 delete)·두 프로젝트 동기화는 개발자가 직접(범위 밖). 계획: `C:\Users\User\.claude\plans\radiant-swinging-toast.md`.

**완료**
- DB: `Sql/player/17_admin_schema_versions.sql` — `ts_schema_draft`·`ts_schema_version` 테이블 + `ts_admin_schema_stage`/`publish`/`revert` RPC(service_role 전용). 4개 기능 전체 디스패치. **마왕·데슬 양쪽 적용·검증 완료**(스테이징 무영향·게시 원자성·롤백·파괴적 op 차단). 게시 RPC는 기존 admin RPC를 perform으로 호출(트랜잭션 공유→원자성).
- Retool 백엔드: `/backend/schemaChange/` 6개(stageChange·getDraft·discardDraft·publish·getVersions·revertVersion) — **게시됨(main)**.
- Retool 프론트: `/frontend/pages/SchemaChanges.tsx`("변경 관리" 페이지 — draft·게시·버전이력·롤백). App.tsx 라우트 `/schema-changes` + Layout 네비(History 아이콘) 추가됨.

- 스테이징 전환: 각 모달의 **백엔드 함수를 stage 호출로 전환**(프론트 모달 그대로). dataManager add/update/deleteColumn, leaderboard manageColumn/upsert/deleteLeaderboard, remoteConfig 6종. RemoteConfig는 값이 jsonb 병합이라 config 전용 stage RPC(`ts_admin_schema_stage_config_*` + `_ts_config_base`/`_ts_stage_config_row`)로 "키당 대기 op 하나·유효기준에 편집 얹기"를 DB에서 처리. stage에 dedup(같은 변경 2번 눌러도 draft 1개) 추가. 전부 양 DB 적용·검증·게시됨.
- Layout에 "미게시 변경 N건" 배너(useGetDraft 5초 폴링, /schema-changes 링크) 추가·게시됨.

**operator 구분은 범위 밖(사용자 확정)** — 날짜·시각(published_at·created_at·reverted_at)만으로 구분 충분. operator 컬럼은 null로 남기고 UI에 표시 안 함(제거하지 말 것, 나중에 필요 시 채움).

**2026-07-27 마감 — 미게시 표시 방식 확정**
- 결론: **모든 페이지는 라이브 상태만 표시**하고, 대기 변경은 별도로 알린다("예상 상태(effective) 표시" 방식은 목록 백엔드마다 draft 병합이 필요하고 staged 생성 행의 파생값이 부정확해 기각).
- 공용 조각: `/frontend/lib/schemaLabels.ts`(opLabel) + `/frontend/pages/ui/PendingChanges.tsx`(이 화면의 대기 변경을 한 줄씩 + "변경 취소" 버튼). 게시 동선은 Layout 전역 배너 하나로 통일(패널에 게시 링크 두지 않음).
- 적용: 리더보드 탭·리더보드 필드 탭·원격 설정·유저 데이터 필드 4곳.
- 변경 관리 페이지: 내용 열을 params 덤프 대신 기능별 한 줄 요약(`formatSummary`)으로. 기존 상태를 바꾸는 작업은 "~으로 변경" 어투. 게시 이름은 게시 모달에서 입력(비우면 작업 요약이 자동 기록). 사이드바에서 "변경 관리"를 기능 목록과 분리해 하단에 배지와 함께 배치.
- 버그 2건 수정: `deleteRemoteConfig.ts`가 설정 삭제 대신 아이템 삭제 RPC를 호출하던 것 → `ts_admin_schema_stage_config_delete`로 교정. `deleteRemoteConfigItem.ts`가 테이블을 직접 UPDATE해 스테이징을 우회하던 것 → `ts_admin_schema_stage_config_item_delete`로 교정. 두 RPC 모두 양 DB에 이미 존재(SQL 변경 없음).
- `remote_config.delete`는 게시 시 이전 상태를 저장하므로 **되돌리기 가능**(파괴적 경고 대상 아님). 파괴적=되돌리기 불가는 `*.drop`·`leaderboard_table.delete`뿐.

**남은 일(선택)**
- 모달 버튼 문구는 아직 "추가/저장/삭제"(실제로는 스테이징). 배너·패널로 안내 중이라 급하지 않음.

**Retool 게시 실패 대응**: "Publish failed. Please try again."가 미리보기 정상·Code 오류 없음·승인 대기 0인데도 반복되면 대개 **일시적 stuck 상태**. 아무 파일에 no-op 한 줄 추가로 새 커밋 만들어 재게시하면 풀림(실제로 이 방법으로 해결).

**Retool 훅**: 백엔드 함수 `foo.ts` → 자동 생성 훅 `useFoo`(`../hooks/backend/<folder>`에서 import). MCP read_react_app_files로는 안 보이지만 빌드 시 존재. 사용법: 조회는 `api.trigger(params,{skipCache:true})`+`api.data`+`api.loading`, 변경은 `await api.trigger(params).result`.

**주의**: Retool AI에 빌드 오류 수정을 맡기면 새로 만든 페이지를 빈 스텁으로 갈아엎을 수 있음. 신규 페이지 생성/복원 스레드에 "오류 시 스텁 금지, 정확한 오류 원문 보고 후 훅 배선만 최소 수정" 가드를 넣을 것. [[project_retool_thread_publish_hygiene]]
