---
name: project_signin_result_profile
description: 내 프로필은 로그인 result(SupabaseSignInResult.Profile)로만 노출 — 프로퍼티·getter 없음
metadata:
  type: project
  originSessionId: 81278906-80cd-40f2-8a71-c24bedcd5493
---

**Supabase.MyProfile 프로퍼티·GetMyProfileAsync 제거(2026-07-15). 내 프로필은 로그인 함수 result에만 담긴다.**

경위: 세션 중 "IsNewUser를 LoadAsync result([[project_staticusersave_onfirstload]]의 SupabaseLoadResult)에 담은 것"과 동일 패턴으로, "MyProfile을 획득하는 함수(=로그인) result에 담아라"는 사용자 지시. 중간에 GetMyProfileAsync(SupabaseResult<PublicProfile> 반환) 안을 거쳤다가 최종적으로 **로그인 result 방식**으로 확정.

**타입 이름 변경(2026-07-16):** `PublicProfileSnapshot` → **`PublicProfile`**(길어서 축약). 파일도 `Runtime/Core/Data/PublicProfile.cs`(.cs+.meta git mv로 GUID 보존). 코드·문서 전 참조 sed 치환. 아래 서술의 `PublicProfile`은 옛 `PublicProfileSnapshot`.

**신규 타입** `SupabaseSignInResult : SupabaseResult`(`Runtime/Core/Models/SupabaseSignInResult.cs`) — `.Profile`(PublicProfile, 실패 시 null) + 명시적 `implicit operator bool`. `Success(profile)`/`new Fail(errorCode, banInfo)` 팩토리. **파생 result 타입은 implicit bool을 명시 정의**한다(SupabaseResult<T> 패턴; 상속 확실치 않아 SupabaseLoadResult·SupabaseSignInResult 둘 다 추가).

**로그인 파사드 7개**가 반환: `SignInAnonymouslyAsync`·`SignInWithGoogleAsync`·`SignInWithGoogleIdTokenAsync`·`SignInWithAppleAsync`·`SignInWithAppleIdTokenAsync`·`TriggerAutoLoginAsync`·`RestoreSessionAsync`. 구현: 파사드에서 래핑(`Supabase.cs`의 `ToSignInResultAsync` 헬퍼가 SDK Try*(SupabaseResult 반환, 불변) 성공 시 프로필을 실어 SignInResult로 변환). 연동(link)·해제(unlink)는 로그인 아님 → 제외.

**세션 지속 캐시 완전 제거 → 일회성 전달 필드(2026-07-16).** 이전엔 정적 `_myProfile`이 세션 내내 프로필을 들고 있었으나(로그인 결과 공급 + SetDisplayName 반환), 사용자가 "아직 캐싱 데이터가 있냐"며 제거 지시. **제약:** 로그인 반환 체인은 인터셉터 경계(PlayNANOO)가 결과를 비제네릭 `Task<SupabaseResult>`로 붕괴시켜 세션·프로필이 못 넘어옴 → 반환 체인 관통은 침습적이라 기각. **채택(사용자 선택 "일회성 전달 필드"):** `_myProfile` → `_pendingSignInProfile`(일회성 전달 슬롯). `TryEnsureProfileRowAfterSignInAsync`가 여기에 세팅(로직·8개 호출부 불변), `CurrentMyProfile` getter → `ConsumePendingSignInProfile()`(읽고 즉시 `Empty`로 클리어). `ToSignInResultAsync`가 성공/실패 무관 1회 소비. 즉 로그인→결과 순간에만 값 존재, **세션 동안 SDK가 프로필 보유 안 함**. `ClearSession`도 리셋 유지.

**핵심 제약:** 프로필을 나중에 읽는 SDK getter 없음. 로그인 result의 `.Profile`을 게임이 직접 보관(샘플 `_lastProfile`). 다른 유저는 `GetPublicProfileAsync(userId)`(서버코드 미포함) 유지.

**이름 충돌 정리 — 프로필 UserId→PlayerUserId(2026-07-16).** `Supabase.UserId`(=`auth.users.id`=account_id, 세션 신원, 가변)와 `PublicProfile.UserId`(=`profiles.user_id`=영속 플레이어 id)가 둘 다 "UserId"라 혼동 → 사용자가 이름 변경 지시(주의문구 대신). **프로필 쪽만** `PublicProfile.UserId` → **`PlayerUserId`**로 rename(내부 `SupabaseUser.PlayerUserId`와 일치, 영향 적음). `Supabase.UserId`(account_id)는 게임 코드가 널리 써서 그대로 둠(=account_id 의미는 유지, breaking 회피). 소비처: SupabaseSDK 프로필 재구성 2곳·샘플 재구성 1곳(`Supabase.UserId` 사용부는 세션 id라 불변). docs `profile.md` 반환 표는 UserId 미노출이라 무변경.

**닉네임 변경은 이름만 반환(2026-07-16).** `SetMyDisplayNameAsync`는 최종적으로 **`Task<SupabaseResult<string>>`**(`.Data`=적용된 정규화 닉네임 문자열). (중간에 `<PublicProfile>`(전체 프로필) 안을 거쳤으나 캐시 제거와 함께 string으로 확정 — 프로필 반환하려면 캐시가 필요해서.) 게임은 보관 프로필의 **이름만** 로컬 교체(샘플이 `new PublicProfile(...구필드..., setOk.Data, ...)`로 재구성). 구현: 코어 `SetMyDisplayNameAsync`(SupabaseSDK, `<bool>`=changed/no_change, 내부·로깅용)에서 `_myProfile` 갱신 블록 제거, `TrySetMyDisplayNameAsync`가 입력에서 `norm` 직접 계산해 `<string>`로 반환. PlayNANOO 인터셉터(`_interceptSetMyDisplayName`)는 SupabaseResult 레벨 불변. 세션 메타(`user_metadata.displayName`) 갱신은 존치(프로필 캐시 아님).

**touch point:** `Supabase.cs`(파사드 `SetMyDisplayNameAsync`→`<string>`, `ToSignInResultAsync` 소비), `SupabaseSDK.cs`(`_pendingSignInProfile`·`ConsumePendingSignInProfile`·SetDisplayName), 샘플(`_lastProfile` 이름만 재구성), docs `nickname/set.md`(시그니처·반환 `<string>`)·로그인 6페이지+`load.md` 반환 섹션 보강. **DefenceR·생성기 무영향**(로그인은 bool로만 씀, SetDisplayName 미사용). 커밋·재생성·컴파일은 사용자.
