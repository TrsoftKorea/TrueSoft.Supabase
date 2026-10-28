// 입력칸·고르기 칸(select)과 여러 줄 입력칸(textarea)의 공용 꼴. 화면마다 정의하거나 직접 적으면 여백과 눌렀을 때의
// 테두리가 갈라진다(.claude/rules/console.md 검사 U7·U10). 폭(w-full·w-40 등)과 상태(비활성 등)는 쓰는 쪽이 뒤에 붙인다.
// 꼴은 2026-10-28 콘솔에서 가장 많이 쓰이던 것(눌렀을 때 연한 파란 테두리 2px)으로 정했다.
// 꼴에 이미 든 속성(높이·여백·글자 크기)을 뒤에 덧붙이면 이길지는 적은 순서가 아니라 Tailwind 가 만든 CSS 순서로 정해진다 —
// 재 보니 h-auto·text-xs·pl-9 는 이기고 h-8·h-10·text-base 는 졌다. 그래서 크기가 다른 칸은 꼴을 따로 두고(inputCompactCls),
// 덧붙여 바꿨으면 미리보기에서 실제 값을 잰다.
export const inputCls =
  'h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'

export const areaCls =
  'px-3 py-2 rounded-md border border-neutral-300 text-sm bg-white outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'

/** 표 안에서 바로 고치는 작은 입력칸(이름·수량 고치기 등). */
export const inputCompactCls =
  'h-8 px-2 rounded border border-neutral-300 text-sm bg-white outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'

/** 영구 삭제 전에 이름을 다시 쳐 넣게 하는 칸 — 누르면 빨갛게 테두리가 생겨 지우는 중임을 알린다. */
export const dangerInputCls =
  'h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white outline-none focus:ring-2 focus:ring-red-300 focus:border-red-400'
