import type { ReactNode } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'

const PAGE_BUTTON = 'w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50'

/**
 * 목록 표 아래 바닥 줄 — 왼쪽은 건수 같은 요약(summary), 오른쪽은 쪽 넘김.
 * 화면마다 같은 줄을 따로 짜던 것(14곳)을 하나로 모았다(.claude/rules/console.md 검사 U5).
 */
export function TableFooter({
  summary,
  page,
  totalPages,
  onPageChange,
}: {
  summary: ReactNode
  page: number
  totalPages: number
  onPageChange: (page: number) => void
}) {
  return (
    <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
      <span>{summary}</span>
      <div className="flex items-center gap-1">
        <button type="button" aria-label="이전 쪽" disabled={page <= 1} onClick={() => onPageChange(Math.max(1, page - 1))} className={PAGE_BUTTON}>
          <ChevronLeft className="w-3.5 h-3.5" />
        </button>
        <span className="px-2">{page} / {totalPages}</span>
        <button type="button" aria-label="다음 쪽" disabled={page >= totalPages} onClick={() => onPageChange(Math.min(totalPages, page + 1))} className={PAGE_BUTTON}>
          <ChevronRight className="w-3.5 h-3.5" />
        </button>
      </div>
    </div>
  )
}
