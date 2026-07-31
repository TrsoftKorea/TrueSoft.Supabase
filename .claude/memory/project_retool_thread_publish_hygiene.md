---
name: project_retool_thread_publish_hygiene
description: Retool React 앱 스레드/게시 위생 — 스택된 스레드가 이전 수정을 되돌리는 함정
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d25e5781-e65c-4b06-a749-94eee8c1f114
---

트루베이스 Retool React 앱(`b518a11a-5d80-...`)에서 MCP 스레드로 작업할 때, **오래된 main에서 갈라진 스레드를 게시하면 그 사이 다른 스레드가 고친 내용이 되돌아간다.**

**Why:** 2026-07-08 영수증 검증 페이지 작업 중, getPurchases 상태 필터 수정(`$9::int`→`::text`)을 담은 스레드가 미게시 상태였는데, 그 전에 만든 DateRangePicker 스레드(더 오래된 main 기준)를 게시하자 getPurchases가 버그 버전으로 되돌아감. 또 신규 컴포넌트(스레드 브랜치)와 수동 붙여넣기(에디터 초안)가 서로 다른 브랜치에 있어 `Could not resolve './ui/DateRangePicker'` 빌드 실패 발생.

**How to apply:**
- 새 작업 스레드는 **직전 게시 직후 최신 main 기준**으로 시작(`startNewThreadFromMain`). 여러 스레드를 쌓아두지 말 것.
- CLAUDE.md 규칙상 **신규 파일=스레드 도구, 기존 파일 수정=채팅 전체 코드 붙여넣기**로 메커니즘 자체가 분리되어 있어 "한 스레드에 묶기"가 불가능하다. 대신 **게시 순서로 해결**: 신규 컴포넌트(스레드)를 먼저 게시해 main에 올린 뒤, 그 컴포넌트를 import하는 기존 파일 수정본을 붙여넣고 게시. 순서를 반대로 하면 `Could not resolve './ui/컴포넌트명'` 빌드 에러(2026-07-08 DateRangePicker, 2026-07-13 TableStatusRow에서 동일 패턴 재확인).
- 게시는 `retool_publish_react_app`에 **해당 스레드 generationHandle을 넘겨** 특정 스레드를 콕 집어 게시(에디터 수동 Publish는 엉킨 초안을 게시할 수 있음). 단 프로덕션 배포라 자동 승인 모드에선 차단될 수 있음 — 사용자가 승인/해제해야 함.
- 게시 후 **게시된 main을 다시 읽어** 의도한 파일들이 다 반영됐는지(특히 이전 fix가 안 되돌아갔는지) 확인. 관련: [[project_retool_project_switching_methodB]]
