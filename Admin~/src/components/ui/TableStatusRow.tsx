import { Loader2 } from 'lucide-react'

export function TableStatusRow({
  loading,
  empty,
  colSpan,
  emptyText = '결과가 없습니다.',
  loadingText = '불러오는 중…',
}: {
  loading: boolean
  empty: boolean
  colSpan: number
  emptyText?: string
  loadingText?: string
}) {
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
