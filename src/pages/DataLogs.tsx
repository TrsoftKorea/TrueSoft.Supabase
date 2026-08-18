import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, ChevronLeft, ChevronRight, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime, diffSummary } from '../components/ui/format'

type LogRow = { id: number; account_id: string; diff: Record<string, unknown>; source: string | null; created_at: string }
type LogData = { rows: LogRow[]; total: number; pageSize: number }

export default function DataLogs({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const navigate = useNavigate()
  const [accountIdInput, setAccountIdInput] = useState('')
  const [accountId, setAccountId] = useState('')
  const [sourceInput, setSourceInput] = useState('')
  const [source, setSource] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<LogData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setData(await callAdmin<LogData>(target, 'dataLogs.list', {
        accountId, source, startDate: dateFrom || null, endDate: dateTo || null, page,
      }))
    } catch (e: unknown) {
      report(e, '데이터 로그를 불러오지 못했습니다.')
      setData(null)
    } finally {
      setLoading(false)
    }
  }, [target, accountId, source, dateFrom, dateTo, page, report])

  useEffect(() => { void load() }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 30
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const submitSearch = () => { setAccountId(accountIdInput.trim()); setSource(sourceInput.trim()); setPage(1) }

  const goPlayer = (id: string) => navigate(`/players?account=${encodeURIComponent(id)}&name=`)

  return (
    <div className="space-y-4">
      <PageHeader title="데이터 로그" description="플레이어 세이브 데이터가 언제 어떻게 바뀌었는지 계정을 가로질러 조회합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-neutral-500">변경 시각</span>
          <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
          <input
            value={accountIdInput}
            onChange={(e) => setAccountIdInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
            placeholder="계정 ID"
            className="h-9 px-3 rounded-md border border-neutral-300 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff] w-64"
          />
          <input
            value={sourceInput}
            onChange={(e) => setSourceInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
            placeholder="출처 (예: admin)"
            className="h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff] w-48"
          />
          <button
            onClick={submitSearch}
            disabled={loading}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
          </button>
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">시각</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">계정</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">출처</th>
                <th className="text-left px-4 py-2.5 font-medium">바뀐 값</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={4} emptyText="데이터 로그가 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50/50">
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.created_at)}</td>
                    <td className="px-4 py-3">
                      <button onClick={() => goPlayer(r.account_id)} className="text-[#1677ff] hover:underline font-mono text-xs">
                        {r.account_id.slice(0, 8)}
                      </button>
                    </td>
                    <td className="px-4 py-3 text-neutral-600">{r.source ?? '-'}</td>
                    <td className="px-4 py-3 text-neutral-700 max-w-[480px] truncate" title={diffSummary(r.diff)}>
                      {diffSummary(r.diff)}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          <span>{total} result{total === 1 ? '' : 's'}</span>
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
