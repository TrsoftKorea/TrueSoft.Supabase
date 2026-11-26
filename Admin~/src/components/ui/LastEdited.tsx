import { formatDateTime } from './format'

/** 설정 카드의 "마지막 수정 시각" — 고친 운영자는 둘째 줄. 한 줄에 부호로 이으면 시각과 이름이 안 갈린다. */
export function LastEdited({ at, by }: { at: string | null | undefined; by?: string | null | undefined }) {
  if (!at) return null
  return (
    <span className="inline-block text-xs text-neutral-400">
      <span className="block whitespace-nowrap">마지막 수정 {formatDateTime(at)}</span>
      {by && <span className="block">{by}</span>}
    </span>
  )
}
