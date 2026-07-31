---
name: project_retool_page_header
description: Retool 페이지 제목·부제목은 PageHeader 공용 컴포넌트로 통일. h2+p 직접 작성 금지.
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f192039-fca3-497f-bd1f-9da182660d0d
  modified: 2026-07-22T04:23:10.296Z
---

Retool 어드민(트루베이스, appUuid `b518a11a-5d80-11f1-9bd8-b3fb46e07228`)의 페이지 상단 제목·부제목은 **`/frontend/pages/ui/PageHeader.tsx` 공용 컴포넌트**로 통일했다(2026-07-22 생성).

```tsx
<PageHeader title="제목" description="부제목" actions={<button/>} />
```

- 페이지에서 `<h2 className="text-2xl…">` + `<p className="text-sm…">`를 **직접 쓰지 않는다**. 페이지마다 폰트 크기·간격이 어긋난 게 이 컴포넌트를 만든 이유.
- 표준: 제목 `text-2xl font-semibold text-neutral-900`, 부제목 `text-sm text-neutral-500 mt-1`. 둘을 `<div>`로 묶어 부모 `space-y-4`가 헤더 덩어리 하나에만 걸리게 한다.
- 제목 옆 버튼(RemoteConfig `설정 추가` 등)은 `actions` 슬롯 사용.
- 페이지 컨테이너는 `space-y-4`로 통일(DataLogs가 `gap-6`이라 혼자 달랐음).

**부제목 문구 규칙**: 짧은 한 문장, 페이지가 무엇인지만 담백하게. 예: `우편 발송 시 선택할 분류 목록입니다.` 부연 설명 둘째 문장을 붙이지 않는다(사용자가 직접 정한 톤).

[[project_retool_table_status_row_rollout]] · [[project_retool_thread_publish_hygiene]]
