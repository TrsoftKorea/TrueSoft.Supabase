import type { ReactNode } from 'react'

export type InfoItem = {
  label: ReactNode
  value: ReactNode
  /** 눈여겨봐야 할 값(되돌릴 수 없는 대상 수 등) — 빨갛게. */
  tone?: 'danger'
  /** 긴 글(메시지 등) — 한 줄을 다 쓴다. 좁은 칸에 넣으면 몇 글자마다 줄이 꺾인다. */
  wide?: boolean
}

/**
 * 이름·값 여러 개를 보여 주는 목록. 이름은 위에 작고 옅게, 값은 아래에 진하게 둔다.
 * "문의 시각 … 플랫폼 android 앱 버전 1.4.2"처럼 한 줄에 같은 글씨로 이어 붙이면 어디까지가 이름이고 어디부터
 * 값인지 안 갈려 읽기 어렵다(2026-10-29 사용자 지적) — 이름·값이 짝인 항목이 둘 이상이면 이 부품을 쓴다.
 * 값이 비어 있어도 항목을 빼지 않고 회색 "없음"으로 둔다 — 빼면 대상마다 같은 항목의 자리가 달라진다(2026-10-29 사용자 지적).
 * 그 상태에서 아예 해당이 없는 항목(차단 안 된 플레이어의 종료 기한 등)은 부르는 화면이 목록에서 뺀다.
 * 불러오는 중인 값은 비우지 말고 '…'를 넘긴다("없음"으로 보이지 않게).
 */
export function InfoList({ items, className = '' }: { items: InfoItem[]; className?: string }) {
  return (
    <dl className={`grid grid-cols-[repeat(auto-fill,minmax(8.5rem,1fr))] gap-x-6 gap-y-2 ${className}`}>
      {items.map((i, n) => {
        const empty = i.value === null || i.value === undefined || i.value === false || i.value === ''
        return (
          <div key={n} className={`min-w-0 ${i.wide ? 'col-span-full' : ''}`}>
            <dt className="text-xs text-neutral-500">{i.label}</dt>
            <dd className={`text-sm break-words ${empty ? 'text-neutral-400' : i.tone === 'danger' ? 'text-red-600 font-semibold' : 'text-neutral-900'}`}>
              {empty ? '없음' : i.value}
            </dd>
          </div>
        )
      })}
    </dl>
  )
}
