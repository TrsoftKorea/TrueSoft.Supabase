---
name: feedback_sdk_naming_datetimeoffset
description: "SDK 공개 식별자는 간결하게(중복 수식어 제거), 절대 시각은 DateTimeOffset. 함수·필드·타입 모두. 추가·수정 시에도 적용."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 84f7aac8-ee86-4421-b6db-a9c113936544
  modified: 2026-07-30T08:32:41.210Z
---

SDK 식별자를 새로 만들거나 고칠 때 아래 두 규칙을 적용한다.

**적용 대상 = 게임(개발자) 대면 식별자만.** 목적은 개발자가 호출·입력할 때 편하게 하는 것이다: `Supabase` 파사드 공개 메서드, 개발자가 읽는 반환 데이터의 프로퍼티·타입·enum(`PublicProfile.Name`, `SupabaseFailCode.NameTaken` 등). **내부 구현 이름(`SupabaseSDK.Try*`·Core 서비스·Auth 서비스·인터셉터 필드·로그 태그·PlayerPrefs 키 등)은 대상이 아니다 — 건드리지 않는다(상관없음).** 내부는 오히려 서버 어휘(`displayName`)와 맞춰두는 게 자연스럽다.

## 1. 이름은 간결하게 — "불필요한 중복 수식어"만 제거

구분 기능이 없어진 수식어는 뺀다. 함수뿐 아니라 **필드·프로퍼티·타입 이름에도 동일** 적용.
- `Utc` — 타입이 `DateTimeOffset`이면 이미 UTC/오프셋을 뜻하므로 뺀다. 예: `GetServerUtcNowAsync`→`GetServerNowAsync`.
- `My` — 본인 대상 함수는 뺀다(자주 씀). 타인 대상은 `Public*` 접두어가 이미 구분하므로 본인 쪽만 짧게. 예: `SetMyDisplayNameAsync`→`SetDisplayNameAsync`(→`SetNameAsync`), `GetMyMailsAsync`→`GetMailsAsync`, 타입 `MyWithdrawalStatus`→`WithdrawalStatus`. 타인 함수엔 접두어를 유지/추가(`GetPublicNameAsync(userId)`).
- `ToCurrentAnonymous`→`ToGuest`(더 짧고 직관적), `GetMailDetailAsync`→`GetMailAsync`, `DisplayName`→`Name` 등.

**단, "중복"이 아닌 표준 용어는 자르지 않는다.** 처음엔 `DisplayName`을 유지 권장했으나(서버 계약 용어) 사용자가 API 표면만 `Name`으로 축약하도록 결정함.

**서버/DB 계약은 절대 바꾸지 않는다** — 케이스가 달라 대개 안전(대소문자 구분 치환):
- JSON/메타 키: `displayName`(camel), Auth `user_metadata.displayName`
- 실패 코드 문자열: `display_name_taken`, `display_name_too_long`(snake) — enum/const **이름**은 바뀌어도 **문자열 값은 유지**
- DB 테이블/컬럼: `display_names`, `display_name`
PascalCase C# 식별자만 `Name`으로, camel/snake 서버 어휘는 그대로.

## 2. 절대 시각은 `DateTimeOffset`

공개 API 경계를 넘는 절대 시각(서버 시각, DB `timestamptz`)은 `DateTime`이 아니라 `DateTimeOffset`(`?`)을 쓴다. **파사드 시그니처는 `SdkAudit`의 R11이 검사하므로 여기서 외울 필요 없다.**
**내부용 `DateTime`은 유지** — RemoteConfig 스테일 쿨다운, 저장 대기 데드라인, 서버 기록용 `DateTime.UtcNow.ToString("o")`(updated_at 등), 내부 파싱 비교. 오프셋이 의미 없는 곳은 바꾸지 않는다.

## 반영 파급

이름 변경은 파사드·`SupabaseSDK.Try*`·Core 서비스·`MailboxFacade`·`<see cref>`·샘플·문서까지 일관 적용(대소문자 구분 치환이 안전). 소비 게임([[project_defencer_consumes_sdk_via_github]] DefenceR `SupabaseManager.cs`, 데슬 `ServerManager`)도 호출부 갱신 필요 — DefenceR는 직접, 데슬(ProjectNS_Android)은 사용자가. [[feedback_supabase_no_alias]]
