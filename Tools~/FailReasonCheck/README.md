# 에러코드 카탈로그 무결성 검사

`SupabaseFailCode`(enum + `FromErrorCode` map)와 `SupabaseErrorCode`(문자열 상수)이 서로 어긋나지 않는지 Unity 없이 검증하는 도구입니다. 둘 다 Core(`Runtime/Core/Models/`)에 있습니다.

## 왜 필요한가

에러코드 사유는 **`SupabaseFailCode` enum + `SupabaseErrorCode` 상수** 두 곳을 손으로 맞춰야 합니다(`FromErrorCode` 스위치는 상수를 직접 참조하므로 문자열은 상수에만 존재). 이름이 어긋나거나 map이 잘못 참조되면 런타임에 조용히 `Unknown`으로 처리되거나 분기가 틀립니다. 이 검사가 그 드리프트를 자동으로 잡습니다.

두 파일 모두 UnityEngine 의존이 0이라 `dotnet`으로 함께 컴파일해 리플렉션으로 검증할 수 있습니다.

## 사용법

```sh
dotnet run --project Tools~/FailReasonCheck
```

- 종료 코드 `0` = 정합성 통과. `1` = 불일치(하드 오류).
- 경고는 종료 코드에 영향을 주지 않습니다.

## 검사 항목

| 구분 | 내용 |
|------|------|
| 하드 오류 | enum 멤버 ↔ 동명 `SupabaseErrorCode` 상수 1:1 대응 |
| 하드 오류 | `FromErrorCode(상수값)` == 동명 enum 멤버 (map 정합성) |
| 하드 오류 | 에러코드 문자열 중복 없음 |
| 하드 오류 | `null`·빈문자열 → `None`, 미정의 문자열 → `Unknown` |
| 경고 | `Runtime`·`Editor` 어디에서도 방출/참조되지 않는 죽은 사유 |

## 한계

- 방출 스캔은 **경고**입니다. 서버(Edge Function)에서만 발생하고 C#에서는 매핑만 하는 사유가 있다면 오탐일 수 있으니, 경고가 뜨면 실제로 죽은 사유인지 확인하세요.
- 사유를 추가/제거할 때 이 검사를 통과시키면 3곳 동기화가 보장됩니다. `dotnet build Tools~/CoreCompileCheck`(컴파일 검증)와 함께 돌리면 좋습니다.

## Unity 영향

폴더명이 `~`로 끝나 Unity가 완전히 무시합니다(`.meta` 미생성).
