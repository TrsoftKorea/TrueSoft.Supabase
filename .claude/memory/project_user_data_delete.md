---
name: project_user_data_delete
description: 본인 세이브 삭제 기능 PlayerSave.DeleteAsync() — 서버 DELETE + 로컬 리셋
metadata: 
  node_type: memory
  type: project
  originSessionId: d649c959-f17d-4eef-8b24-a9c883a675a6
---

PlayNANOO storage-delete 참고로 SDK에 본인 세이브 삭제 기능 추가(2026-07-13). 게임 SDK 변경만, **DB 변경 없음**.

**핵심 발견:** `user_data`는 이미 클라이언트 DELETE RLS(`delete_own_authenticated` 정책+grant)가 열려 있어 새 RPC 불필요. DELETE 시 트리거 안 걸림(3개 다 UPDATE 전용), 참조 FK 없음 → 오류 없이 삭제됨(ProjectR 실측). 필드 보호는 UPDATE WITH CHECK라 DELETE 무관 — 그래서 "기본값 PATCH 리셋"이 아니라 **행 DELETE**를 씀(PATCH 리셋은 보호 필드 낮출 때 막힘). ts_ensure_my_row가 다음 로드 때 기본 행 재생성 → 실질 "기본값 리셋". 탈퇴(계정 삭제)와 별개.

**함정:** DB만 지우면 로컬 StaticUserSave.Current가 옛 데이터를 들고 있어 자동 저장이 ensureRowFirst로 행을 되살림 → 삭제 되돌아감. 그래서 DeleteAsync는 **먼저 ResetLocalState()로 로컬 기본값 리셋 후 서버 DELETE**(await 도중 자동 저장이 돌아도 diff 비어 PATCH 안 나감). 좁은 레이스(전송 중이던 PATCH가 DELETE 뒤 도착)는 남아 문서에 "조용한 시점 호출" 안내. 실패 시 로컬=기본값·서버=옛 데이터 → LoadAsync 재동기 안내.

**구현(6곳):** SupabaseUserDataService.DeleteMyRowAsync(REST DELETE `?account_id=eq.`, 멱등) → UserSavesFacade.DeleteMyRowAsync<T>(세션·테이블 해석) → SupabaseSDK.DeleteUserDataAsync<T>+TryDeleteUserDataAsync<T>(ApiLogTags.UserDataDelete) → Supabase.DeleteUserDataAsync<T>(internal) → StaticUserSave.DeleteAsync()(public, ResetLocalState 재사용) → 생성기 PostgrestOpenApiUserSaveClass.cs가 PlayerSave.DeleteAsync() 정적 래퍼 emit. SupabaseFailReason.UserSaveDeleteFailed 추가. 문서 docs~/guide/user-data/delete.md + game-data.md·config 사이드바. 샘플 ExampleSupabaseScenarios 키 X.

**범위 밖:** 컬럼 단위 삭제, mails/purchases/profile(세이브 아님), PlayNanoo 병행(Supabase user_data만 지움). 미검증: Unity 컴파일(brace/paren 균형만 확인)·Play Mode(키 X)·커밋은 사용자.
