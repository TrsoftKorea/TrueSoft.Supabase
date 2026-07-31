---
name: feedback_file_delete
description: "SDK 파일 삭제 규칙 — 게임 참조 먼저 확인, Samples~는 SDK 소스에서 삭제"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 57fbc782-c0d2-4151-bc9e-e4e98f5a00cb
  modified: 2026-07-27T09:02:22.513Z
---

## Runtime 파일 삭제 전 게임 참조 확인

SDK Runtime 파일을 지우기 전에 게임 어셈블리(DefenceR 등)가 그 타입을 직접 참조하는지 확인한다.

**Why:** `TrueBase.Unity.DataColumnAttribute`를 "Core 미러라 중복"이라 판단해 삭제했는데, 게임 어셈블리는 Core asmdef를 참조하지 않아 그 미러에 의존하고 있었다. 컴파일 오류가 나서 되돌렸다.

**How to apply:** 컴파일 오류(ambiguous reference 등)가 나면 삭제보다 `using` 제거·네임스페이스 정리를 먼저 시도한다. 삭제는 게임에서 전혀 안 쓰인다고 확인한 뒤에만.

## Samples~ 는 SDK 소스에서 삭제

`Samples~` 하위 파일은 SDK 소스(`D:\Project\TrueSoft.Supabase\Samples~\`)에서 지운다.

**Why:** Unity Package Manager로 임포트된 `Assets\Samples\...` 사본은 재임포트 시 SDK 소스에서 복원되므로, 임포트본만 지우면 의미가 없다.

**How to apply:** Assets 경로는 건드리지 않는다.

## 기능 이전·삭제 시 안내 문구 금지

기능이 옮겨지거나 삭제될 때 **원래 자리에 "○○로 이동했습니다" 같은 안내를 남기지 않는다.** 흔적 없이 제거한다. 잔여물이 UI·코드에 남아 지저분하다(예: SupabaseSettings 인스펙터의 클래스 생성기 이동 안내 HelpBox 제거 요청). 관련: [[feedback_doc_style]]
