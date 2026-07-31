---
name: ""
description: DevilSlayer·DefenceR(ProjectR) 등 모든 Supabase 프로젝트의 DB 내부 구조는 100% 동일해야 함
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
---

라이브 게임 프로젝트들(ProjectR/DefenceR `wxivrmvtpufeczltward`, DevilSlayer `owumqjyctqhuyailqutd`, 이후 추가 포함)의 **DB 내부 구조는 서로 완전히 동일**해야 한다. 새 프로젝트가 생겨도 같은 방식으로 관리 가능해야 한다.

**Why:** 사용자 명시(2026-07-09): "devilslayer과 defencer은 내부 구조에 대해 차이가 있으면 안돼. 나중에 다른 프로젝트가 생겨도 동일하게 관리가 가능해야 해."

**How to apply:**
- 프로젝트별 분기 SQL 금지. 샘플 `Samples~/DatabaseSetup/SQL/player/*.sql`이 유일 캐노니컬 소스. 어느 프로젝트든 동일 SQL을 그대로 적용. 새 프로젝트 온보딩 = 같은 파일 순서대로 실행.
- grant는 고정 role로 균일(`service_role`/`authenticated`). **프로젝트 기본 권한 차이 주의**: ProjectR은 신규 public 테이블에 service_role DML을 기본 grant, DevilSlayer는 안 함 → 캐노니컬 SQL에 `revoke select,insert,update,delete ... from service_role`를 **명시**해야 어느 프로젝트든 수렴.
- 작업 후 **구조 동일성 대조**: 컬럼·정책(polcmd::text 캐스팅 필요)·함수 시그니처(pg_get_function_identity_arguments)·grant를 `md5(string_agg(... order by))` 핑거프린트로 두 프로젝트 비교, **일치 확인**.
- 기존 divergence 발견 시(예: ProjectR 06_mails 하드닝 미완) 먼저 제거해 동일화한 뒤 신규 작업. [[feedback_sql_apply]] [[project_mailbox_admin_send]]
- **엣지 함수도 두 프로젝트에 각각 배포해야 하며 드리프트가 잘 생긴다.** SQL 핑거프린트로는 안 잡히니 `get_edge_function`으로 두 프로젝트의 배포본을 직접 대조할 것. 실제 사례(2026-07-16): DevilSlayer `withdrawal-guard`가 구버전(v4, `.eq("account_id", user.id)` 필터 누락)이라 `user_profiles` 공개 SELECT RLS 때문에 전체 프로필 행이 반환→`maybeSingle()`이 "multiple (or no) rows" 500. 로그인마다 500(SDK는 false로 방어하나 ~12초 낭비 + 탈퇴삭제 감지 무력화). 로컬 소스·ProjectR(v30)엔 이미 수정본 있었음 → DevilSlayer만 v5로 재배포해 해소.
