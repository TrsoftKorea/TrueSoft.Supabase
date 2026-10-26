import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, ChevronLeft, ChevronRight, Loader2, Download, RefreshCw } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { type ProjectTarget } from '../lib/projectTarget'
import { projectName } from '../lib/projectLook'
import { downloadCsv, todayStamp } from '../lib/csv'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { formatCount, formatDateTime, formatKRW, formatTotal } from '../components/ui/format'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'

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
  refunded_at: string | null
}
type PurchaseData = { rows: PurchaseRow[]; total: number; pageSize: number }

type RefundStatus = {
  store: string
  last_started_at: string | null
  last_finished_at: string | null
  last_ok: boolean | null
  last_message: string | null
  last_marked: number
}

// DB 는 apple_app_store 로 저장한다(install.sql purchases.store). app_store 는 예전 값이 남아 있을 때를 위해 둔다.
const STORE_LABEL: Record<string, string> = { google_play: 'Google Play', apple_app_store: 'App Store', app_store: 'App Store' }

function refundStatusText(store: string, s: RefundStatus | undefined): { text: string; bad: boolean } {
  if (store === 'apple_app_store') {
    // 애플은 환불이 생길 때만 알림이 온다 — 오래 비어 있어도 고장이 아니다.
    if (!s?.last_finished_at) return { text: '애플: 받은 환불 알림 없음', bad: false }
    return {
      text: `애플: 마지막 알림 ${formatDateTime(s.last_finished_at)}${s.last_ok ? '' : ' · 처리 실패'}`,
      bad: s.last_ok === false,
    }
  }
  if (!s?.last_finished_at) return { text: '구글: 아직 확인 안 함', bad: false }
  if (s.last_ok === false) {
    return { text: `구글: ${formatDateTime(s.last_finished_at)} 확인 실패 · ${refundFailText(s.last_message)}`, bad: true }
  }
  return { text: `구글: ${formatDateTime(s.last_finished_at)} 확인 · 새 환불 ${formatCount(s.last_marked)}건`, bad: false }
}

// 실패 사유는 엣지 함수가 남긴 원문이다(purchase-refund-sync-google). 운영자가 손쓸 수 있는 것만 풀어 쓴다.
function refundFailText(message: string | null | undefined): string {
  if (message === 'project_url_missing') return '설치가 끝나지 않음'
  if (message === 'GOOGLE_SERVICE_ACCOUNT_JSON 없음') return '설정 › 스토어 연결에 Google Play 서비스 계정 값이 없습니다.'
  return message ? `환불 확인에 실패했습니다. (${message})` : '환불 확인에 실패했습니다.'
}

// price_amount 는 1/1,000,000 단위(micros)로 저장돼 있다.
const priceFromMicros = (v: string | number | null) => (v != null ? Number(v) / 1_000_000 : null)

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
  const [refundedOnly, setRefundedOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PurchaseData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [loadFailed, setLoadFailed] = useState(false)
  const [refundStatus, setRefundStatus] = useState<RefundStatus[]>([])
  const [syncing, setSyncing] = useState(false)
  const [notice, setNotice] = useState<{ text: string; ok: boolean } | null>(null)

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
    setLoadFailed(false)
    try {
      setData(await callAdmin<PurchaseData>(target, 'purchases.list', {
        search, startDate: dateFrom || null, endDate: dateTo || null, page, refundedOnly,
      }))
    } catch (e: unknown) {
      report(e, '구매 내역을 불러오지 못했습니다.')
      setData(null)
      setLoadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, search, dateFrom, dateTo, page, refundedOnly, report])

  const loadRefundStatus = useCallback(async () => {
    try {
      const res = await callAdmin<{ rows: RefundStatus[] }>(target, 'purchases.getRefundStatus', {})
      setRefundStatus(res.rows)
    } catch (e: unknown) {
      report(e, '환불 확인 상태를 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])
  useEffect(() => { void loadRefundStatus() }, [loadRefundStatus])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const submitSearch = () => { setSearch(searchInput); setPage(1) }

  const syncRefunds = async () => {
    setSyncing(true)
    setNotice(null)
    try {
      const res = await callAdmin<{ ok?: boolean; skipped?: string; message?: string; reason?: string; marked?: number }>(
        target, 'purchases.syncRefunds', {},
      )
      if (res.skipped === 'recently_run') setNotice({ text: '방금 확인했습니다. 10분 뒤에 다시 확인할 수 있습니다.', ok: false })
      else if (res.ok) setNotice({ text: `구글 환불을 확인했습니다. 새 환불 ${formatCount(res.marked ?? 0)}건.`, ok: true })
      else setError(refundFailText(res.message ?? res.reason))
      await Promise.all([loadRefundStatus(), load()])
    } catch (e: unknown) {
      report(e, '환불을 확인하지 못했습니다.')
    } finally {
      setSyncing(false)
    }
  }

  const [exporting, setExporting] = useState(false)
  const exportCsv = async () => {
    setExporting(true)
    try {
      const res = await callAdmin<{ rows: PurchaseRow[]; truncated: boolean }>(target, 'purchases.list', {
        search, startDate: dateFrom || null, endDate: dateTo || null, refundedOnly, all: true,
      })
      downloadCsv(
        `구매내역_${projectName(target)}_${todayStamp()}.csv`,
        ['결제 확인일', '스토어', '상품 ID', '주문번호', '플레이어', '계정 ID', '금액', '통화', '원화 환산', '환불일'],
        res.rows.map((r) => [
          formatDateTime(r.verified_at),
          STORE_LABEL[r.store] ?? r.store,
          r.product_id,
          r.order_id,
          r.user_id,
          r.account_id,
          priceFromMicros(r.price_amount),
          r.price_currency,
          r.price_amount_krw,
          r.refunded_at ? formatDateTime(r.refunded_at) : null,
        ]),
      )
      if (res.truncated) setError('결제가 너무 많아 앞의 10만 건만 담았습니다. 기간을 나눠 내려받으세요.')
    } catch (e: unknown) {
      report(e, '내려받지 못했습니다.')
    } finally {
      setExporting(false)
    }
  }

  const goPlayer = (r: PurchaseRow) => {
    if (!r.account_id) return
    navigate(`/players?account=${encodeURIComponent(r.account_id)}&name=${encodeURIComponent(r.user_id ?? '')}`)
  }

  const statusByStore = new Map(refundStatus.map((s) => [s.store, s]))

  return (
    <div className="space-y-4">
      <PageHeader title="구매 내역" description="검증된 인앱 결제 내역을 조회합니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {notice && (
        <div className={notice.ok
          ? 'rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700'
          : 'rounded-md border border-neutral-200 bg-neutral-50 px-4 py-2.5 text-sm text-neutral-600'}
        >
          {notice.text}
        </div>
      )}

      <WhiteCard className="p-4 space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-neutral-500">결제 확인일</span>
          <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
            placeholder="상품 ID / 플레이어 / 주문번호"
            className="flex-1 min-w-[200px] h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
          />
          <label className="flex items-center gap-1.5 text-xs text-neutral-600 cursor-pointer whitespace-nowrap">
            <input type="checkbox" checked={refundedOnly} onChange={(e) => { setRefundedOnly(e.target.checked); setPage(1) }} className="rounded" />
            환불만
          </label>
          <Button variant="primary" size="compact"
            onClick={submitSearch}
            disabled={loading}
          >
            {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
          </Button>
          <Button size="compact" onClick={() => void exportCsv()} disabled={exporting || total === 0} title="지금 검색 조건에 맞는 결제 전체를 내려받습니다">
            {exporting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Download className="w-3.5 h-3.5" />}CSV 내려받기
          </Button>
        </div>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-neutral-100 pt-3 text-xs">
          {(['google_play', 'apple_app_store'] as const).map((store) => {
            const { text, bad } = refundStatusText(store, statusByStore.get(store))
            return <span key={store} className={bad ? 'text-red-600' : 'text-neutral-500'}>{text}</span>
          })}
          <Button size="sm" className="ml-auto" onClick={() => void syncRefunds()} disabled={syncing} title="구글에 환불된 결제가 있는지 지금 확인합니다. 하루 한 번 자동으로도 확인합니다">
            {syncing ? <Loader2 className="w-3 h-3 animate-spin" /> : <RefreshCw className="w-3 h-3" />}구글 환불 지금 확인
          </Button>
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
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={6} emptyText="구매 내역이 없습니다." failed={loadFailed} />
              ) : (
                rows.map((r) => {
                  const refunded = !!r.refunded_at
                  const strike = refunded ? 'line-through text-neutral-400' : ''
                  return (
                    <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50/50">
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.verified_at)}</td>
                      <td className="px-4 py-3">
                        {r.account_id ? (
                          <button onClick={() => goPlayer(r)} className="text-[#1677ff] hover:underline">{r.user_id ?? '(이름 없음)'}</button>
                        ) : (
                          <span className="text-neutral-400">{r.user_id ?? '-'}</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <span className={`font-mono text-xs ${strike}`}>{r.product_id}</span>
                        {refunded && (
                          <span className="ml-2 inline-flex items-center rounded bg-red-50 px-1.5 py-0.5 text-[11px] text-red-600 whitespace-nowrap" title={`환불 ${formatDateTime(r.refunded_at)}`}>
                            환불
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{STORE_LABEL[r.store] ?? r.store}</td>
                      <td className={`px-4 py-3 whitespace-nowrap ${strike}`}>
                        {r.price_amount_krw != null
                          ? formatKRW(r.price_amount_krw)
                          : r.price_amount != null
                            ? `${(priceFromMicros(r.price_amount) ?? 0).toLocaleString('ko-KR', { maximumFractionDigits: 2 })} ${r.price_currency ?? ''}`
                            : '-'}
                      </td>
                      <td className="px-4 py-3 text-neutral-600 font-mono text-xs">{r.order_id ?? '-'}</td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
        <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          <span>{formatTotal(total)}</span>
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
