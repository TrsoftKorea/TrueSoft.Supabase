import type { ReactNode } from 'react'
import { StatusText, TONE_TEXT, type Tone } from './StatusText'

export type StatusFact = { label: string; value: ReactNode; tone?: Tone }

export type StatusRowItem = {
  label: string
  tone: Tone
  /** 상태 한마디 — "정상"·"실패"·"하루 넘게 안 돎". */
  state: string
  /** 마지막으로 돈 시각(이미 형식을 맞춘 글자). 없으면 회색 "-". */
  at: string | null
  /** 이번 실행의 숫자들. 이름은 옅게, 숫자는 굵게 — 문장으로 이어 붙이지 않는다. */
  facts?: StatusFact[]
}

/**
 * 자동 작업 상태 여러 줄. 작업 이름 · 상태 · 마지막 시각 · 숫자들이 줄마다 같은 자리에 오게 칸을 맞춘다.
 * "2026. 11. 10. 13:00 확인 · 새 환불 1건 · 옛 결제 …"처럼 한 문장으로 이어 붙이면 어디까지가 무엇인지 안 보인다
 * (2026-11-10 사용자 지적).
 */
export function StatusRows({ items }: { items: StatusRowItem[] }) {
  return (
    // 좁은 화면에서는 칸을 맞추지 않고 작업마다 한 묶음으로 쌓는다 — 네 칸을 한 줄에 두면 옆으로 넘친다.
    <div className="space-y-3 text-sm md:grid md:grid-cols-[max-content_max-content_max-content_1fr] md:items-baseline md:gap-x-6 md:gap-y-2.5 md:space-y-0">
      {items.map((i) => (
        <div key={i.label} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 md:contents">
          <span className="font-medium text-neutral-900 whitespace-nowrap">{i.label}</span>
          <StatusText tone={i.tone}>{i.state}</StatusText>
          <span className="tabular-nums text-neutral-600 whitespace-nowrap">{i.at ?? <span className="text-neutral-400">-</span>}</span>
          <span className="flex flex-wrap gap-x-5 gap-y-1">
            {(i.facts ?? []).map((f) => (
              <span key={f.label} className="whitespace-nowrap">
                <span className="text-neutral-500">{f.label}</span>{' '}
                <span className={`font-semibold tabular-nums ${TONE_TEXT[f.tone ?? 'plain']}`}>{f.value}</span>
              </span>
            ))}
          </span>
        </div>
      ))}
    </div>
  )
}
