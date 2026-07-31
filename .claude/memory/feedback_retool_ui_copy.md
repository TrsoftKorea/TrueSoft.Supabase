---
name: feedback_retool_ui_copy
description: "Retool UI 설명 문구는 내부 SDK 용어 금지, 처음 보는 운영자 기준 평이하게"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: abfd5b20-08ea-4615-9621-139d8bb0baa8
---

Retool 화면에 노출되는 **설명·안내 문구는 처음 보는 개발자·운영자 기준**으로 쓴다. 내부 구현 용어를 넣지 않는다.

**금지 예시(내부 용어):** `[AutoDefault]`, `mails.category에 저장`, "슬롯별 기본값", "클라이언트에서 채웁니다", "폴백 병합", "diff PATCH" 등 SDK/DB 내부를 아는 사람만 이해할 표현.

**대신:** 그 화면에서 운영자가 실제로 할 일·알아야 할 것만 평이하게. 예 — JSON 컬럼 기본값 안내: ❌ "컬렉션(JSON) 컬럼은… 슬롯별 기본값은 [AutoDefault]로, 초기 데이터는 클라이언트에서 채웁니다" → ✅ "JSON 컬럼은 기본값을 지정하지 않습니다. 시작값은 게임에서 관리됩니다."

**Why:** 사용자가 반복해서 지적함("내가 아니라 처음보는 개발자 또는 운영자가 쓰기 편하게"). 내가 코드를 아니까 무심코 내부 용어를 넣는 경향이 있음.

**How to apply:** Retool 문구를 쓸 때마다 "이 화면만 처음 보는 운영자가 이 단어를 아는가?"를 자문. 모르면 빼거나 일상어로 바꾼다. 코드 파일명·속성명·SDK 개념명은 UI 문구에 넣지 않는다. [[feedback_doc_style]] · [[project_mailbox_category]]
