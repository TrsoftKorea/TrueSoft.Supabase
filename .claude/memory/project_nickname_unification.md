---
name: "3"
description: display_names·user_metadata·Retool 닉네임 일치. seeding 트리거 + 백필로 통일(2026-07-09).
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
---

닉네임은 **본인 화면 = 남의 조회 = Retool 집계** 세 곳이 항상 일치해야 한다(사용자 요구). 정본은 `auth.user_metadata.displayName`, `display_names` 테이블은 그 미러.

**원래 문제:** 쓰기 경로가 둘이라 갈라짐 — 직접 설정(`displayname-set` 에지함수)은 display_names + user_metadata 양쪽을 쓰지만, 자동 기본값(`Player_xxxxxxxx`)은 `UpdateUserMetadataDisplayNameAsync`로 **user_metadata에만** 씀([SupabaseSDK.cs:3444](Runtime/Unity/SupabaseSDK.cs:3444), 구글 신규가입만). 익명은 아예 기본값도 안 넣음. → display_names 비어 Retool·타인조회가 `이름없음`.

**해결(2026-07-09, 서버 완결):** `user_profiles` INSERT 시 `display_names`를 자동 seeding하는 트리거 `trg_seed_display_name`(함수 `ts_seed_display_name_for_profile`, SECURITY DEFINER)를 **ProjectR·DevilSlayer 양쪽 apply_migration**. 이름 = metadata.displayName 있으면 그 값, 없으면 `Player_`+account_id앞8자. 전역 이름 유니크 충돌 시 Player_ 기본값 폴백. metadata가 비면(익명) 같은 값으로 metadata도 채움(기존 값 보존). 기존 계정은 백필 1회(ProjectR 4건·DevilSlayer 7건, 충돌 0). 샘플은 [02_profiles.sql](Samples~/DatabaseSetup/SQL/player/02_profiles.sql)에 트리거+주석 반영.

**왜 클라 변경 불필요:** 로그인 시 `TryEnsureProfileRowAfterSignInAsync`가 프로필 INSERT(→트리거 seeding) 후 `GetProfileAsync`(→`displayname-get`→display_names)로 `MyProfile.DisplayName`을 채운다([SupabaseSDK.cs:3370](Runtime/Unity/SupabaseSDK.cs:3370)). 그래서 최초 로그인부터 자기화면도 seeded 값. `user_profiles`엔 display_name 컬럼 없음(닉네임은 display_names 전용).

**닉네임 유니크 범위 = 전역(2026-07-09 통일).** 사용자 결정: 닉네임=식별자 유지 + PlayNANOO가 전역이라 **전역 고유**로 통일. 원래는 ProjectR만 전역 인덱스 `(lower(trim(display_name)))`, DevilSlayer·샘플은 서버별 `(server_id, ...)`이었고, ProjectR은 인덱스는 전역인데 `ts_is_display_name_available`는 서버별로 검사하는 **잠재 버그**가 있었음(다른 서버 닉을 "가능"이라 하고 저장 시 전역 유니크에 걸림). 정렬 내용(양 프로젝트 적용 완료):
- DevilSlayer 인덱스 서버별→전역(서버 1개라 충돌 0).
- `ts_is_display_name_available` server_id 필터 제거 → 전역(plpgsql→sql).
- **RLS 하드닝**: SELECT 정책은 `display_names_select_own`(`account_id = auth.uid()`) — 직접 REST는 본인 행만. 남의 닉은 `displayname-get`이 **service_role로 조회해 display_name만** 반환(ProjectR v31·DevilSlayer v4, sha 동일). 이유: 정책을 전역으로 열면 로그인 사용자가 display_names 전체(account_id·user_id=Google sub)를 REST로 덤프 가능 → 서버 완결로 차단.
- 샘플 [02_profiles.sql](Samples~/DatabaseSetup/SQL/player/02_profiles.sql)·[displayname-get/index.ts](Samples~/DatabaseSetup/EdgeFunctions/displayname-get/index.ts) 정렬. seeding 트리거 충돌검사는 이미 전역이라 그대로.

관련: [[feedback_sql_apply]] (배포본 먼저 확인 원칙이 또 유효했음).

**account_id NULL(탈퇴/삭제) 프로필은 seeding 불가**(display_names.account_id가 PK) — 정상 제외.
