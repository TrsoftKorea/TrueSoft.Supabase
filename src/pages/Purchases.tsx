import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, ChevronLeft, ChevronRight, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { formatDateTime, formatKRW } from '../components/ui/format'

type PurchaseRow = {
  id: string
  account_id: string | null
  user_id: string | null
  product_id: string
  order_id: string | null
  package_name: string
  store: string
  price_amount: string | number | null
  price_currency: string | null
  price_amount_krw: string | number | null
  verified_at: string
}
type PurchaseData = { rows: PurchaseRow[]; total: number; pageSize: number }

const STORE_LABEL: Record<string, string> = { google_play: 'Google Play', app_store: 'App Store' }

export default function Purchases({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const navigate = useNavigate()
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PurchaseData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) {
        onUnauthenticated()
        return
      }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setData(await callAdmin<PurchaseData>(target, 'purchases.list', {
        search, startDate: dateFrom || null, endDate: dateTo || null, page,
      }))
    } catch (e: unknown) {
      report(e, '구매 내역을 불러오지 못했습니다.')
      setData(null)
    } finally {
      setLoading(false)
    }
  }, [target, search, dateFrom, dateTo, page, report])

  useEffect(() => { void load() }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const submitSearch = () => { setSearch(searchInput); setPage(1) }

  const goPlayer = (r: PurchaseRow) => {
    if (!r.account_id) return
    navigate(`/players?account=${encodeURIComponent(r.account_id)}&name=${encodeURIComponent(r.user_id ?? '')}`)
  }

  return (
    <div className="space-y-4">
      <PageHeader title="구매 내역" description="검증된 인앱 결제 내역을 조회합니다." />

      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
          {error}
        </div>
      )}

      <WhiteCard className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-neutral-500">결제 확인일</span>
          <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
            placeholder="상품 ID / 유저 ID / 주문번호"
            className="flex-1 min-w-[200px] h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
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
                <th className="text-left px-4 py-2.5 font-medium w-40">결제 확인일</th>
                <th className="text-left px-4 py-2.5 font-medium">플레이어</th>
                <th className="text-left px-4 py-2.5 font-medium">상품</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">스토어</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">금액</th>
                <th className="text-left px-4 py-2.5 font-medium">주문번호</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={6} emptyText="구매 내역이 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50/50">
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.verified_at)}</td>
                    <td className="px-4 py-3">
                      {r.account_id ? (
                        <button onClick={() => goPlayer(r)} className="text-[#1677ff] hover:underline">{r.user_id ?? '(이름 없음)'}</button>
                      ) : (
                        <span className="text-neutral-400">{r.user_id ?? '-'}</span>
                      )}
                    </td>
                    <td className="px-4 py-3 font-mono text-xs">{r.product_id}</td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{STORE_LABEL[r.store] ?? r.store}</td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      {r.price_amount_krw != null ? formatKRW(r.price_amount_krw) : `${r.price_amount ?? '-'} ${r.price_currency ?? ''}`}
                    </td>
                    <td className="px-4 py-3 text-neutral-600 font-mono text-xs">{r.order_id ?? '-'}</td>
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
