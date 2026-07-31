---
name: project_retool_resource_bindings
description: Retool 두 앱의 Supabase 리소스 바인딩 규칙과 데슬 SQL 인젝션 가드 주의점
metadata: 
  node_type: memory
  type: project
  originSessionId: d5fba00e-281d-4083-aa85-297fd1ce4ed3
---

TrueSoft Retool 어드민 툴은 두 앱(마왕 Plus `b518a11a-5d80-...` / 데슬 Plus `1f735e30-6482-...`)이며 리소스 바인딩이 다르다.

- **마왕**: `supabaseDefencer` = DefenceR DB(ProjectR `wxivrmvtpufeczltward`). `supabase`는 이것의 **deprecated 별칭**(= DefenceR).
- **데슬**: `supabaseDevilslayer` = DevilSlayer DB(`owumqjyctqhuyailqutd`)가 올바른 DB. **데슬에서 `supabase`를 쓰면 마왕 DB(DefenceR)를 건드린다 — 버그.** 데슬 코드는 반드시 `supabaseDevilslayer`.

**Why:** 2026-06-19 데슬의 `deleteColumn.ts`/`updateColumn.ts`가 `supabase`(=DefenceR)를 써서 데슬 컬럼 작업이 마왕 DB를 변경하던 버그를 발견·수정.

**How to apply:** 데슬 백엔드 파일 작성/검토 시 리소스명이 `supabaseDevilslayer`인지 항상 확인. 마왕은 `supabase`(또는 `supabaseDefencer`) 사용.

**데슬 SQL 인젝션 가드:** 데슬의 `supabaseDevilslayer` 리소스는 쿼리 문자열에 `${...}` 템플릿 보간이 있으면 **저장이 차단**된다(write-time guard). 따라서 식별자 보간이 필요한 DDL(`ALTER TABLE ... "${col}"`)은 데슬에 직접 쓸 수 없고, **RPC 패턴**으로 해야 한다 — DB에 `format(%I)` 기반 `SECURITY DEFINER` 함수(`admin_add/drop/update_user_data_column`)를 만들고 `query('SELECT fn($1,...)', [...])`로 파라미터 호출. 마왕 리소스에는 이 가드가 없다. 컬럼 추가/삭제/수정·`overwritePlayerData`는 모두 이 패턴으로 통일됨. 관련: [[feedback_sql_apply]]
