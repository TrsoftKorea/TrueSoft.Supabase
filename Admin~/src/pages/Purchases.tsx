import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, Loader2, Download, RefreshCw, ChevronDown, ChevronUp } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { type ProjectTarget } from '../lib/projectTarget'
import { projectName } from '../lib/projectLook'
import { downloadCsv, todayStamp } from '../lib/csv'
import { WhiteCard } from '../components/ui/Card'
import { inputCls } from '../components/ui/Input'
import { TableFooter } from '../components/ui/TableFooter'
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
  is_test: boolean | null
  is_free: boolean | null
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

// 시작만 하고 끝을 못 남긴 채 10분이 지났는지 — 함수가 중간에 죽은 것. 도는 중(시작한 지 10분 안)은 멈춘 게 아니다.
const stalled = (s: RefundStatus | undefined) =>
  !!s?.last_started_at
  && Date.parse(s.last_started_at) > Date.parse(s.last_finished_at ?? '1970-01-01')
  && Date.now() - Date.parse(s.last_started_at) > 10 * 60_000

// 하루 한 번 도는 작업이 마지막으로 끝난 지 36시간이 넘었는지 — 한 번 거르면 12시간 안에 드러난다.
// 크론은 요청을 보내기만 하고 응답을 안 보므로, 함수가 아예 안 불려도(배포 안 됨·주소 틀림) 기록이 멈출 뿐 실패는 안 남는다.
const STALE_MS = 36 * 3600_000
const overdue = (s: RefundStatus) => Date.now() - Date.parse(s.last_finished_at!) > STALE_MS

// 상태 줄 하나. 평소엔 문제(short)만 보이고, 시각·건수 같은 세부(detail)는 "자세히"를 눌러야 보인다.
type JobState = { label: string; bad: boolean; short: string; detail: string }

// 매일 도는 작업(구글 환불 확인·원화 환산)에 공통인 판정 — 멈춤·한 번도 안 돎·하루 넘게 안 돎.
function dailyJobProblem(label: string, s: RefundStatus | undefined): JobState | null {
  if (stalled(s)) {
    return { label, bad: true, short: '중간에 멈춤', detail: `${formatDateTime(s!.last_started_at!)}에 시작한 작업이 끝나지 않았습니다.` }
  }
  if (!s?.last_finished_at) return { label, bad: true, short: '한 번도 안 돎', detail: '아직 한 번도 돌지 않았습니다. 설치가 끝났는지 확인해 주세요.' }
  if (s.last_message === 'project_url_missing') return { label, bad: true, short: '설치가 끝나지 않음', detail: '설치가 끝나지 않아 돌지 못했습니다.' }
  if (overdue(s)) {
    return { label, bad: true, short: '하루 넘게 안 돎', detail: `마지막 실행 ${formatDateTime(s.last_finished_at)}. 하루 한 번 도는 작업이 그 뒤로 돌지 않았습니다.` }
  }
  return null
}

function googleRefundState(s: RefundStatus | undefined): JobState {
  const label = '구글 환불 확인'
  const problem = dailyJobProblem(label, s)
  if (problem) return problem
  const recheck = testRecheckText(s!.last_message)
  if (s!.last_ok === false) {
    return { label, bad: true, short: '실패', detail: `${formatDateTime(s!.last_finished_at!)} 실패 · ${refundFailText(s!.last_message)}${recheck}` }
  }
  return { label, bad: false, short: '', detail: `${formatDateTime(s!.last_finished_at!)} 확인 · 새 환불 ${formatCount(s!.last_marked)}건${recheck}` }
}

// 애플은 환불이 생길 때만 알림이 온다 — 오래 비어 있어도 고장이 아니라 오래됐는지는 보지 않는다.
function appleRefundState(s: RefundStatus | undefined): JobState {
  const label = '애플 환불 알림'
  if (!s?.last_finished_at) return { label, bad: false, short: '', detail: '받은 알림 없음' }
  if (s.last_ok === false) return { label, bad: true, short: '처리 실패', detail: `마지막 알림 ${formatDateTime(s.last_finished_at)} · 처리 실패` }
  return { label, bad: false, short: '', detail: `마지막 알림 ${formatDateTime(s.last_finished_at)}` }
}

// 환율표 저장·빈 금액 채우기(purchase-price-sync)의 마지막 실행. 메모는 함수가 운영자용 문장으로 남긴다.
function priceSyncState(s: RefundStatus | undefined): JobState {
  const label = '원화 환산'
  const problem = dailyJobProblem(label, s)
  if (problem) return problem
  const memo = s!.last_message ? ` · ${s!.last_message}` : ''
  if (s!.last_ok === false) return { label, bad: true, short: '실패', detail: `${formatDateTime(s!.last_finished_at!)} 실패${memo}` }
  return { label, bad: false, short: '', detail: `${formatDateTime(s!.last_finished_at!)} 확인${memo}` }
}

// 같은 실행에서 시험 여부를 모르는 옛 결제를 구글에 다시 물은 결과(purchase-refund-sync-google 의 메모 꼴을 읽는다).
function testRecheckText(message: string | null | undefined): string {
  const m = /시험 결제 확인 (\d+)건\(그중 시험 (\d+)건\)(?:, 구글이 답하지 않은 (\d+)건)?/.exec(message ?? '')
  if (!m) return ''
  const [, filled, tests, unanswered] = m
  return ` · 옛 결제 시험 여부 ${filled}건 확인(시험 ${tests}건)${unanswered ? `, 구글이 답하지 않음 ${unanswered}건` : ''}`
}

// 실패 사유는 엣지 함수가 남긴 원문이다(purchase-refund-sync-google). 운영자가 손쓸 수 있는 것만 풀어 쓴다.
function refundFailText(raw: string | null | undefined): string {
  // 재확인 결과 메모는 testRecheckText 가 따로 보인다.
  const message = raw?.replace(/\s*(\/\s*)?시험 결제 확인 \d+건[\s\S]*$/, '').trim() || null
  if (message === 'project_url_missing') return '설치가 끝나지 않음'
  if (message === 'GOOGLE_SERVICE_ACCOUNT_JSON 없음') return '설정 › 서비스 연결 › 스토어에 Google Play 서비스 계정 값이 없습니다.'
  const noApp = /^(\S+): HTTP 404[\s\S]*No application was found/.exec(message ?? '')
  if (noApp) return `결제 기록의 앱 ${noApp[1]} 이 Google Play 에 없습니다. 시험용으로 넣은 결제일 수 있습니다.`
  if (message?.includes('권한 없음으로 거절')) return 'Google Play 서비스 계정에 결제 조회 권한이 없습니다. Play Console의 사용자 및 권한을 확인하세요.'
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
  // 시험 결제 보기: all 전부 · exclude 시험 빼고 · only 시험만
  const [test, setTest] = useState<'all' | 'exclude' | 'only'>('all')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PurchaseData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [loadFailed, setLoadFailed] = useState(false)
  const [refundStatus, setRefundStatus] = useState<RefundStatus[]>([])
  const [refundStatusState, setRefundStatusState] = useState<'loading' | 'ok' | 'failed'>('loading')
  const [jobsOpen, setJobsOpen] = useState(false)
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
        search, startDate: dateFrom || null, endDate: dateTo || null, page, refundedOnly, test,
      }))
    } catch (e: unknown) {
      report(e, '구매 내역을 불러오지 못했습니다.')
      setData(null)
      setLoadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, search, dateFrom, dateTo, page, refundedOnly, test, report])

  const loadRefundStatus = useCallback(async () => {
    try {
      const res = await callAdmin<{ rows: RefundStatus[] }>(target, 'purchases.getRefundStatus', {})
      setRefundStatus(res.rows)
      setRefundStatusState('ok')
    } catch (e: unknown) {
      setRefundStatusState('failed')
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
        search, startDate: dateFrom || null, endDate: dateTo || null, refundedOnly, test, all: true,
      })
      downloadCsv(
        `구매내역_${projectName(target)}_${todayStamp()}.csv`,
        ['결제 확인일', '스토어', '상품 ID', '주문번호', '플레이어', '계정 ID', '금액', '통화', '원화 환산', '환불일', '시험 결제'],
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
          r.is_test == null ? null : r.is_test ? '예' : '아니요',
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
  // 상태를 못 불러왔으면(빈 목록) "한 번도 안 돎"·"모두 정상" 어느 쪽으로도 판정하지 않는다.
  const jobs = refundStatusState === 'ok'
    ? [googleRefundState(statusByStore.get('google_play')), appleRefundState(statusByStore.get('apple_app_store')), priceSyncState(statusByStore.get('price_sync'))]
    : []
  const problems = jobs.filter((j) => j.bad)

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
            className={inputCls + ' flex-1 min-w-[200px]'}
          />
          <label className="flex items-center gap-1.5 text-xs text-neutral-600 cursor-pointer whitespace-nowrap">
            <input type="checkbox" checked={refundedOnly} onChange={(e) => { setRefundedOnly(e.target.checked); setPage(1) }} className="rounded" />
            환불만
          </label>
          <select value={test} onChange={(e) => { setTest(e.target.value as typeof test); setPage(1) }}
            className={inputCls}
            title="시험 결제는 애플 샌드박스(TestFlight·앱 심사 포함)와 구글 라이선스 테스트 계정 결제입니다. 매출에 넣지 않습니다">
            <option value="all">시험 결제 포함</option>
            <option value="exclude">시험 결제 빼기</option>
            <option value="only">시험 결제만</option>
          </select>
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
        <div className="border-t border-neutral-100 pt-3 text-xs">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            {refundStatusState === 'loading' && <span className="text-neutral-400">결제 자동 작업 상태 확인 중…</span>}
            {refundStatusState === 'failed' && <span className="text-red-600">결제 자동 작업 상태를 불러오지 못했습니다.</span>}
            {refundStatusState === 'ok' && (problems.length === 0
              ? <span className="text-neutral-500">결제 자동 작업 모두 정상</span>
              : problems.map((j) => <span key={j.label} className="text-red-600">{j.label}: {j.short}</span>))}
            {refundStatusState === 'ok' && (
              <button type="button" onClick={() => setJobsOpen((v) => !v)}
                className="inline-flex items-center gap-0.5 text-[#1677ff] hover:underline">
                {jobsOpen ? '접기' : '자세히'}
                {jobsOpen ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
              </button>
            )}
            <Button size="sm" className="ml-auto" onClick={() => void syncRefunds()} disabled={syncing} title="구글에 환불된 결제가 있는지 지금 확인합니다. 하루 한 번 자동으로도 확인합니다">
              {syncing ? <Loader2 className="w-3 h-3 animate-spin" /> : <RefreshCw className="w-3 h-3" />}구글 환불 지금 확인
            </Button>
          </div>
          {jobsOpen && (
            <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              {jobs.map((j) => (
                <div key={j.label} className="contents">
                  <dt className="text-neutral-500 whitespace-nowrap">{j.label}</dt>
                  <dd className={j.bad ? 'text-red-600' : 'text-neutral-600'}>{j.detail}</dd>
                </div>
              ))}
            </dl>
          )}
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
                        {r.is_test && (
                          <span className="ml-2 inline-flex items-center rounded bg-neutral-100 px-1.5 py-0.5 text-[11px] text-neutral-600 whitespace-nowrap" title="시험 결제 — 매출에 넣지 않습니다">
                            테스트
                          </span>
                        )}
                        {r.is_free && (
                          <span className="ml-2 inline-flex items-center rounded bg-neutral-100 px-1.5 py-0.5 text-[11px] text-neutral-600 whitespace-nowrap" title="프로모션 코드·광고 보상으로 받은 무료 결제 — 매출에 넣지 않습니다">
                            무료
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
        <TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />
      </WhiteCard>
    </div>
  )
}
