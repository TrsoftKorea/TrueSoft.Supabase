---
name: project_retool_confirm_dialog
description: Retool 운영 화면의 확인 창은 window.confirm 대신 공용 ConfirmDialog 컴포넌트를 쓴다
metadata: 
  node_type: memory
  type: project
  originSessionId: d061fbfd-afff-4ef4-8461-8742d6aa0337
  modified: 2026-08-05T03:19:52.133Z
---

트루베이스 Retool 앱에서 되돌리기 어려운 동작을 확인받을 때는 **`/frontend/pages/ui/ConfirmDialog.tsx`** 를 쓴다. `window.confirm` 은 쓰지 않는다.

**Why:** 브라우저 `confirm` 은 제목 줄에 `sandbox-….retool.app에 삽입된 페이지 내용:` 이 그대로 나오고 그 문구를 바꿀 수 없다. 무엇을 지우는지 보여줄 수도 없어 엉뚱한 행을 지우기 쉽다. 2026-07-31 에 13개 파일 16곳을 전부 전환했다.

**How to apply:**
- props: `open` · `title` · `description` · `confirmLabel` · `danger` · `busy` · `onConfirm` · `onCancel` · `children`
- `children` 에 **지울 대상의 이름·내용**을 넣는다. 어느 행인지 눈으로 확인시키는 것이 이 창의 핵심이다
- 되돌릴 수 없으면 `danger`(빨간 버튼), 되돌릴 수 있으면 생략(파란 버튼). 차단 해제처럼 다시 할 수 있는 동작은 `danger` 를 붙이지 않는다
- 진행 중에는 `busy` 로 버튼·Esc·바깥 클릭을 모두 막는다
- 로딩 오버레이가 있는 화면(`PlayerDetail`)은 확인 창을 **오버레이 바깥**에 둔다. 안에 두면 진행 중에 오버레이가 창을 덮는다
- 페이지마다 모달을 새로 짜지 않는다 — 문구·색·버튼 배치가 갈라진다

**남은 것:** `window.alert` 은 아직 그대로다(`SchemaChanges`·`LeaderboardsTab`·`ScoresTab` 등의 오류 알림). 확인을 받는 용도가 아니라 별건으로 뒀지만, 이것도 브라우저 창이라 제목에 샌드박스 주소가 뜬다. 정리하려면 화면 안 배너로 옮겨야 한다.

관련: [[project_retool_thread_publish_hygiene]] · [[feedback_retool_ui_copy]] · [[project_retool_page_header]]
