---
name: feedback_game_changes_on_request_only
description: 게임 프로젝트(DefenceR·DevilSlayer) 반영은 사용자가 직접. 명시적 요청이 있을 때만 작업.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 84f7aac8-ee86-4421-b6db-a9c113936544
  modified: 2026-07-20T07:39:07.183Z
---

SDK 변경이 게임 프로젝트(DefenceR·DevilSlayer 등 SDK 소비 측)에 breaking change를 일으켜도, 게임 코드 수정은 **사용자가 직접** 한다. Claude는 명시적 요청이 있을 때만 게임 프로젝트를 손댄다.

**Why:** 게임 반영 타이밍·방식은 사용자가 관리한다. SDK 작업 중 임의로 게임까지 건드리면 사용자의 흐름을 방해한다.

**How to apply:** SDK 작업 완료 후에는 게임에 필요한 변경점(예: `SupabaseErrorCode` 리네임 반영, `using` 추가)을 **안내만** 하고, 게임 파일은 수정하지 않는다. 게임 작업은 사용자가 "게임도 반영해줘" 식으로 명시할 때만 진행. SDK는 GitHub UPM으로 소비되므로 [[project_defencer_consumes_sdk_via_github]] 흐름(커밋·푸시 후 패키지 갱신)도 사용자 몫.
