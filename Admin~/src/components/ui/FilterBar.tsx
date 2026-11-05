import type { ReactNode } from 'react'
import { Loader2, RefreshCw, Search } from 'lucide-react'
import { Button } from './Button'

/** 검색·거르기 줄. 이름표는 칸 위에 둔다 — 옆에 두면 선택 안 된 버튼처럼 보인다(.claude/rules/console.md 「거르기」). */
export function FilterBar({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap items-end gap-3">{children}</div>
}

export function FilterField({ label, className = '', children }: { label: string; className?: string; children: ReactNode }) {
  return (
    <div className={className}>
      <div className="text-xs font-medium text-neutral-500 mb-1">{label}</div>
      {children}
    </div>
  )
}

/** 검색 줄 끝의 검색·초기화. */
export function SearchActions({
  onSearch,
  onReset,
  loading = false,
  disabled = false,
}: {
  onSearch: () => void
  onReset: () => void
  loading?: boolean
  disabled?: boolean
}) {
  return (
    <>
      <Button variant="primary" size="compact" onClick={onSearch} disabled={loading || disabled}>
        {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
      </Button>
      <Button variant="outline" size="compact" onClick={onReset}>초기화</Button>
    </>
  )
}

/** 검색 줄이 있으면 그 줄 오른쪽 끝(className="ml-auto"), 없으면 페이지 제목 오른쪽에 둔다. */
export function RefreshButton({ onClick, loading = false, className = '' }: { onClick: () => void; loading?: boolean; className?: string }) {
  return (
    <Button variant="outline" size="compact" onClick={onClick} disabled={loading} className={className}>
      <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />새로고침
    </Button>
  )
}
