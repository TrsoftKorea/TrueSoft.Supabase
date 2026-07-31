---
name: project_supaseresult_reason_failcode
description: SupabaseResult.ErrorMessage→Reason 리네임 + SupabaseFailCode enum 병행(FailCode 파생 프로퍼티)
metadata: 
  node_type: memory
  type: project
  originSessionId: 81278906-80cd-40f2-8a71-c24bedcd5493
---

SupabaseResult 실패 사유 API 정리(2026-07-14, 2단계).

**1) ErrorMessage → Reason 리네임:** `SupabaseResult.ErrorMessage`(string) → `.Reason`. `.Fail(...)` 호출부 432곳은 **전부 위치 인자**라 파라미터명만 바꾸면 무영향 — 실제 수정은 타입 정의 + `.ErrorMessage` **읽기** 130곳. **함정:** `SupabaseHttpResponse.ErrorMessage`(HTTP 전송 계층, 별개 타입)와 `ParseTableResult.ErrorMessage`(Editor 전용)도 이름이 같아 섞임 — Core/Data·Core/Auth 서비스는 `var response = await _httpClient.SendAsync(...)`로 타입명이 코드에 안 보여 사전 grep으로 안 걸러짐. 이 둘은 **범위 밖(안 건드림)**. 문서 "실패 원인" 표 헤더·CLAUDE Rule 9는 원래부터 "Reason"이라 코드가 outlier였음(이 리네임으로 코드-문서 용어 일치).

**2) SupabaseFailCode enum 병행(방식 A: enum + string 병행):** 순수 enum 불가 — 실패 사유 ~30%가 동적(예외 메시지·RPC reason 컬럼·네이티브 SDK 오류), 43%가 Core 레이어(엔진 비의존 asmdef라 Unity의 SupabaseFailReason 참조 불가). 그래서:
- `Runtime/Core/Models/SupabaseFailCode.cs`: `enum SupabaseFailCode {None, Unknown, +56}` + `SupabaseFailCodeMap.FromErrorCode(string)` switch(문자열→enum). None=성공/null, Unknown=카탈로그 밖 동적.
- **최종 이름(2단계 flip):** `SupabaseResult.Reason` = **enum**(`FromErrorCode(ErrorCode)` 파생, 무상태) + `SupabaseResult.ErrorCode` = **string 원문**. 게임은 `result.Reason == SupabaseFailCode.UserBanned`로 타입안전 분기, 동적 원문은 `.ErrorCode`. (`.Fail(...)` 인자는 여전히 문자열=ErrorCode 원문, 호출부 432곳 위치인자라 무영향.) 처음엔 Reason=string+FailCode=enum였다가 사용자 요청으로 이름 맞바꿈.
- **문자열 값 기준 매핑**이라 호출부가 SupabaseFailReason 상수든 raw 리터럴이든 동일 인식(예: raw `"user_banned"`도 UserBanned로 매핑).
- Unity `SupabaseFailReason`(56 const string) **유지**(하위호환+`.ErrorCode` 비교). enum 멤버명=상수명 1:1.
- **3자 동기화 필수:** 새 사유 추가 시 SupabaseFailReason 상수(Unity)·SupabaseFailCode enum·FromErrorCode switch **세 곳**. Core는 Unity 참조 못 해 문자열이 양쪽 존재(드리프트 위험 → 값 diff로 검증했음, 현재 56=56=56 완전 일치).

**적용:** 문서 api/index.md(enum 우선 예제)·auth/ban·social/{google,apple}/unlink → `result.Reason == SupabaseFailCode.X`. 샘플 ExampleSupabaseScenarios(연동해제 2곳 enum, `using TrueBase.Core.Common` 추가). 나머지 per-function "실패 원인" 표 30개는 SupabaseFailReason.X 문자열 유지(enum명 동일해 자명, 30파일 churn 회피). CLAUDE 결과처리절+Rule9 갱신. DefenceR SupabaseManager는 `.ErrorCode` 로깅만(enum 분기 없음).

**미검증:** Unity 컴파일(러너 없음). switch expression+`null or ""` 패턴은 C#9(Unity 2022.3 지원). DefenceR는 [[project_defencer_consumes_sdk_via_github]] 패키지 갱신 후 반영. 커밋은 사용자.
