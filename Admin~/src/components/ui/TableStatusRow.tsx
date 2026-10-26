import { Loader2 } from 'lucide-react'

export function TableStatusRow({
  loading,
  empty,
  colSpan,
  emptyText = '결과가 없습니다.',
  loadingText = '불러오는 중…',
  failed = false,
}: {
  loading: boolean
  empty: boolean
  colSpan: number
  emptyText?: string
  loadingText?: string
  // 불러오기가 실패했으면 "없습니다" 대신 실패를 알린다 — 빈 목록과 구분이 안 되면 운영자가 정말 없다고 믿는다.
  failed?: boolean
}) {
  if (failed && !loading) {
    return (
      <tr>
        <td colSpan={colSpan} className="px-5 py-10 text-center text-sm text-red-600">
          목록을 불러오지 못했습니다. 새로고침해 주세요.
        </td>
      </tr>
    )
  }
  if (loading) {
    return (
      <tr>
        <td colSpan={colSpan} className="px-5 py-14 text-center">
          <span className="inline-flex items-center gap-2 text-sm text-neutral-400">
            <Loader2 className="w-4 h-4 animate-spin" />
            {loadingText}
          </span>
        </td>
      </tr>
    )
  }
  if (empty) {
    return (
      <tr>
        <td colSpan={colSpan} className="px-5 py-10 text-center text-sm text-neutral-500">
          {emptyText}
        </td>
      </tr>
    )
  }
  return null
}
