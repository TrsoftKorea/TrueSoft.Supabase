import type { ReactNode } from 'react'

/**
 * 한 줄에 놓는 값 여럿(필드 이름과 타입, 플레이어와 회차 등) — 부호 없이 간격으로만 가른다. 빈 값(null·false·'')은 뺀다.
 * 넘치면 다음 줄로 감긴다.
 */
export function Spaced({ items, className = '' }: { items: ReactNode[]; className?: string }) {
  return (
    <span className={`inline-flex flex-wrap gap-x-3 ${className}`}>
      {items.filter((x) => x !== null && x !== undefined && x !== false && x !== '').map((x, i) => <span key={i}>{x}</span>)}
    </span>
  )
}
