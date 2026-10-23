import { useEffect, useState, useCallback } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime } from '../../components/ui/format'

type MailRow = {
  id: string
  category: string
  title: string
  items: { key: string; count: number }[] | null
  items_claimed_at: string | null
  deleted_at: string | null
  expires_at: string
  created_at: string
}
type MailData = { rows: MailRow[]; total: number; pageSize: number }

// 우편 내역 화면의 상태 구분에 "기한 만료"를 더한다 — 문의 대응에서는 "왜 못 받았나"가 가장 먼저 나온다.
function statusOf(r: MailRow): { label: string; cls: string } {
  if (r.deleted_at) return { label: '삭제', cls: 'bg-neutral-200 text-neutral-600' }
  if (r.items_claimed_at) return { label: '수령', cls: 'bg-emerald-100 text-emerald-700' }
  if (new Date(r.expires_at).getTime() <= Date.now()) return { label: '기한 만료', cls: 'bg-amber-100 text-amber-700' }
  return { label: '미수령', cls: 'bg-[#e8f0fe] text-[#1677ff]' }
}

export default function MailsTab({
  target,
  onUnauthenticated,
  accountId,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
}) {
  const [page, setPage] = useState(1)
  const [data, setData] = useState<MailData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      // 검색어 칸이 계정 ID 도 찾는다. 전체 ID 를 넣으므로 이 사람 것만 걸린다.
      setData(await callAdmin<MailData>(target, 'mails.getRecords', { search: accountId, page }))
    } catch (e: unknown) {
      report(e, '받은 우편을 불러오지 못했습니다.')
      setData(null)
    } finally {
      setLoading(false)
    }
  }, [target, accountId, page, report])

  useEffect(() => { void load() }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / (data?.pageSize ?? 20)))

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">받은 시각</th>
                <th className="text-left px-4 py-2.5 font-medium">제목</th>
                <th className="text-left px-4 py-2.5 font-medium">보상</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">수령 기한</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={5} emptyText="받은 우편이 없습니다." />
              ) : (
                rows.map((r) => {
                  const st = statusOf(r)
                  return (
                    <tr key={r.id} className="border-t border-neutral-100">
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.created_at)}</td>
                      <td className="px-4 py-3 text-neutral-800">{r.title}</td>
                      <td className="px-4 py-3 text-neutral-600 text-xs">
                        {r.items && r.items.length > 0 ? r.items.map((it) => `${it.key}×${it.count}`).join(', ') : '-'}
                      </td>
                      <td className="px-4 py-3">
                        <span className={`inline-block whitespace-nowrap rounded px-2 py-0.5 text-xs ${st.cls}`}>{st.label}</span>
                      </td>
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.expires_at)}</td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
        <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          <span>{total}건 · 보관 기간이 지나 정리된 우편은 나오지 않습니다</span>
          <div className="flex items-center gap-1">
            <button disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronLeft className="w-3.5 h-3.5" /></button>
            <span className="px-2">{page} / {totalPages}</span>
            <button disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronRight className="w-3.5 h-3.5" /></button>
          </div>
        </div>
      </WhiteCard>
    </div>
  )
}
