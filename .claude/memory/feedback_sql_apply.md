---
name: SQL 변경 시 DB 직접 적용
description: SQL 파일 수정 시 Supabase MCP로 DB에도 직접 적용할 것
type: feedback
originSessionId: 70fa99a5-2414-4407-8c40-a6e2cb457475
---
SQL 파일(`Sql/player/*.sql`, edge functions 등)을 수정할 때는 파일 수정에 그치지 않고 Supabase MCP(`apply_migration`, `deploy_edge_function` 등)로 DB에도 직접 적용한다.

**Why:** 사용자가 SQL은 Claude가 CLI/MCP로 직접 적용해주기를 원함.

**How to apply:** SQL 파일 변경 후 apply_migration 또는 적절한 Supabase MCP 도구로 즉시 DB 반영. 커밋/푸시는 여전히 사용자가 직접.

**에지함수(2026-07-08 재확인):** 사용자가 "sql이나 function은 네가 직접 수정해줘"라고 명시 → ProjectR에 `deploy_edge_function`으로 직접 배포한다. 단:
- **배포본을 먼저 `get_edge_function`으로 받아** 거기에 수정만 적용해 재배포한다(샘플 파일을 그대로 밀면 프로젝트별 분기·env를 덮을 수 있음). 이번엔 배포본==샘플 템플릿이라 동일했음.
- **클라이언트↔서버 계약이 바뀌는 변경**(예: price_amount 단위 major→micros)은 **배포 타이밍을 경고**한다 — 함수만 먼저 배포 시 구버전 클라 요청이 잘못 해석됨(지급은 무관, 회계값만 어긋남). 새 클라 빌드와 함께 출시 필요.
- 배포 후 `get_edge_function`으로 재조회해 **손 전사 오류 없는지 핵심부 검증**.

**라이브 게임 프로젝트가 둘(2026-07-08):** `ProjectR`(`wxivrmvtpufeczltward`, 마왕)과 `DevilSlayer`(`owumqjyctqhuyailqutd`). IAP·에지함수·SQL 등 **공통 변경은 양쪽 모두** 적용해야 한다(사용자가 "DevilSlayer도 적용해줘"). **두 프로젝트의 배포본이 서로 다를 수 있음** — DevilSlayer의 purchase-verify-*는 `user_profiles` 조회에 `.eq("account_id", user.id)` 필터가 없는 구버전이었음(ProjectR엔 있음). 그러므로 반드시 각 프로젝트의 배포본을 개별로 받아 그 위에 수정만 적용한다. [[project_retool_resource_bindings]]
