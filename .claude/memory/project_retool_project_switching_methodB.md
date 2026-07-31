---
name: project-retool-project-switching-methodb
description: 트루베이스 Retool 앱의 라이브 프로젝트 전환(방식 B) 아키텍처 — 중앙 리소스 레지스트리
metadata: 
  node_type: memory
  type: project
  originSessionId: d25e5781-e65c-4b06-a749-94eee8c1f114
---

트루베이스 Retool React 앱(`b518a11a-5d80-...`)은 운영자가 **배포된 `*.retool.app` 앱에서 라이브로 게임 프로젝트를 전환**할 수 있다(방식 B). Retool 환경 전환(방식 A)은 React 앱·배포앱에서 안 먹혀서 폐기했고([[project_retool_env_switching]]), 코드에서 리소스를 직접 고르는 방식으로 구현(2026-06-25).

**구조 (N개 프로젝트로 확장 가능):**
- `/backend/lib/resources.ts` — 단일 소스. `getDb(target)`/`getEdge(target)`가 `ProjectTarget`('defencer'|'devilslayer')에 따라 `supabaseDefencer`/`supabaseDevilslayer`, `supabaseEdgeDefencer`/`supabaseEdgeDevilslayer` 리소스를 반환. (이 앱의 DefenceR 정식 바인딩명은 `supabaseDefencer`/`supabaseEdgeDefencer`. `supabase`/`supabaseEdge`는 deprecated 별칭.)
- 백엔드 25개 함수 전부 `Params`에 `target: ProjectTarget` 추가 + 본문에서 `const db = getDb(req.params.target)` 식으로 분기. (Retool React 백엔드 함수는 `/backend/resources/` 제외하고 공유 모듈 import 가능 — 검증됨.)
- `/frontend/lib/projectTarget.ts` — `getTarget()`/`setTarget()`(localStorage `tb_project_target`), `PROJECTS` 목록. 프론트 모든 `.trigger()`에 `target: getTarget()` 전달.
- `Layout.tsx` 사이드바에 전환 토글(setTarget + `window.location.reload()`).

**새 프로젝트 추가 시:** 리소스 바인딩 추가 + `resources.ts` 분기 1줄 + `projectTarget.ts` PROJECTS 1줄 + union 1개. **25개 함수는 다시 안 건드림.**

**주의:** 클립보드 교차 붙여넣기 가드 `APP_KEY`(DataLogs.tsx·PlayerDataTab.tsx)가 하드코딩이면 단일 앱에서 무력화됨 → `getTarget()` 기반으로 둘 것.

**Why:** 운영자가 두 앱/두 URL 없이 한 배포 앱에서 마왕(DefenceR)/데빌슬레이어(DevilSlayer)를 오가게 하기 위함. 작업은 단일 브랜치에 몰아 한 번에 Publish.
