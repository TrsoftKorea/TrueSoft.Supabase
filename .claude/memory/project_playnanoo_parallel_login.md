---
name: project_playnanoo_parallel_login
description: "PlayNANOO 로그인 병렬화 — 로그인 3경로만 동시 실행, 링크는 순차 유지"
metadata: 
  node_type: memory
  type: project
  originSessionId: 81278906-80cd-40f2-8a71-c24bedcd5493
---

PlayNanooRuntimeBase 로그인 인터셉터 병렬화(2026-07-14). 기존엔 PlayNANOO 로그인 → (성공 시) Supabase 로그인 순차라 왕복 2개가 sum이었음.

**병렬화 안전 근거:** SDK persistent `user_id`는 **Supabase 세션의 `session.User.PlayerUserId`**([UserSavesFacade.cs:88])에서 옴 — PlayNANOO uuid(`PlayNanooRuntimeBase.UserId`)를 **소비 안 함**(UserId는 PlayNANOO 자체 용도로만 저장). 두 로그인은 입력(id token)만 공유하고 결과 의존 없음 → `Task.WhenAll` 안전. 지연 sum→max(≈절반).

**적용(로그인 3경로):** `InterceptSignInAnonymously`, `InterceptSignInWithGoogleIdToken`·`InterceptSignInWithAppleIdToken`(동일해 공통 `InterceptSocialSignInAsync`로 통합). 콜백 Nanoo 로그인을 `NanooGuestSignInAsync`/`NanooSocialSignInAsync`(TCS+RunContinuationsAsynchronously)로 Task 래핑, `NanooSignInResult{Ok,ErrorCode}` 반환. WhenAll 후 재조정: 둘다성공→SyncData / Nanoo만성공→롤백 / Nanoo실패(30007)→취소토큰 이미발급 Fail / Nanoo실패(계정삭제)→Supabase재가입 이미됨→Nanoo재로그인 / Nanoo실패+Supabase성공→SignOutFully.

**링크 4경로는 순차 유지(병렬화 안 함):** 롤백이 로그인은 sign-out(클린)이지만 링크는 **unlink**라 위험. 특히 **익명→소셜 링크는 Supabase만 성공 시 unlink-last-identity로 롤백 불가**(계정이 이미 소셜계정, 유일 연동이라 해제 안 됨). 순차가 "PlayNANOO 성공 확인 후에만 Supabase 계정 변형(link)"으로 이 상황 원천 차단 — 의도적 안전장치. 링크는 일회성이라 지연 이득도 작음. 소셜→소셜(2nd provider)만 이론상 병렬 가능하나 이득<복잡도라 안 함.

**미검증:** Unity 컴파일(러너 없음). 샘플만 수정, SDK 코어 불변. 게임 코드·외부 동작 불변. DefenceR는 [[project_defencer_consumes_sdk_via_github]] 패키지 갱신 후. 커밋은 사용자.
