import type { ReactNode } from 'react'

/**
 * 페이지를 가르는 큰 탭 줄. 카드 안에서 가를 때는 작은 탭 Segmented 를 쓴다(.claude/rules/console.md 「배치」).
 * 탭 오른쪽에 붙는 버튼(공지 보내기 등)은 actions 에 넣는다.
 */
export function Tabs<T extends string>({
  value,
  onChange,
  tabs,
  actions,
}: {
  value: T
  onChange: (v: T) => void
  tabs: readonly (readonly [T, string])[]
  actions?: ReactNode
}) {
  return (
    <div className="border-b border-neutral-200 flex items-end gap-1" role="tablist">
      {tabs.map(([key, label]) => (
        <button
          key={key}
          type="button"
          role="tab"
          aria-selected={value === key}
          onClick={() => onChange(key)}
          className={[
            'h-10 px-4 text-sm border-b-2 -mb-px transition-colors whitespace-nowrap',
            value === key
              ? 'border-[#1677ff] text-[#1677ff] font-medium'
              : 'border-transparent text-neutral-600 hover:text-neutral-900',
          ].join(' ')}
        >
          {label}
        </button>
      ))}
      {actions && <div className="ml-auto pb-1.5">{actions}</div>}
    </div>
  )
}
