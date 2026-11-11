import type { ReactNode } from 'react'
import { TONE_TEXT, type Tone } from './StatusText'

export type Pair = { label: ReactNode; value: ReactNode; tone?: Tone }

/**
 * 칸 하나에 든 "이름 값" 여러 쌍 — 바뀐 데이터, 자동 작업 숫자, 게임이 담아 둔 자유 칸.
 * 이름은 옅게, 값은 진하게, 쌍 사이는 간격으로만 가른다. 쌍점·"="·쉼표를 끼우거나 칸 이름을 되풀이하는 말("이전값")을
 * 붙이지 않는다 — "gold: 이전값 900 · level: 이전값 11"은 어디까지가 이름이고 어디부터 값인지 한눈에 안 갈렸다
 * (2026-11-10 사용자 지적).
 * 기본은 한 줄로 이어 표의 긴 글(text) 칸에서 "…"로 잘리고, wrap 이면 여러 줄로 감긴다(자동 작업 상태 줄).
 */
export function Pairs({ items, wrap = false }: { items: Pair[]; wrap?: boolean }) {
  return (
    <span className={wrap ? 'flex flex-wrap gap-x-5 gap-y-1' : ''}>
      {items.map((p, i) => (
        <span key={i} className={`whitespace-nowrap ${wrap ? '' : 'mr-4 last:mr-0'}`}>
          <span className="text-neutral-500">{p.label}</span>{' '}
          <span className={`font-semibold tabular-nums ${TONE_TEXT[p.tone ?? 'plain']}`}>{p.value}</span>
        </span>
      ))}
    </span>
  )
}
