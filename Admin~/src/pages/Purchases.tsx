import { useEffect, useRef, useState, useCallback } from 'react'
import { Loader2, Download, RefreshCw, ChevronDown, ChevronUp } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { type ProjectTarget } from '../lib/projectTarget'
import { projectName } from '../lib/projectLook'
import { downloadCsv, todayStamp } from '../lib/csv'
import { WhiteCard } from '../components/ui/Card'
import { inputCls } from '../components/ui/Input'
import { TableFooter } from '../components/ui/TableFooter'
import { PageHeader } from '../components/ui/PageHeader'
import { TableCard, type Column } from '../components/ui/DataTable'
import { StatusRows, type StatusFact, type StatusRowItem } from '../components/ui/StatusRows'
import { StatusText, Tag } from '../components/ui/StatusText'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { formatCount, formatDateTime, formatKRW, formatTotal } from '../components/ui/format'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { Segmented } from '../components/ui/Segmented'
import { FilterBar, FilterField, RefreshButton, SearchActions } from '../components/ui/FilterBar'
import { AccountIdCell, PlayerFilterChip, PlayerLink, useFilterPlayer, useOnChange } from '../components/ui/PlayerLink'

type PurchaseRow = {
  id: string
  account_id: string | null
  user_id: string | null
  display_name: string | null
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
type PurchaseData = { rows: PurchaseRow[]; total: number; pageSize: number; nameSearchTruncated?: boolean }

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

// 상태 줄 하나. 평소엔 문제(short)만 보이고, 시각·건수 같은 세부는 "자세히"를 눌러야 보인다.
// 펼친 모습은 작업 이름 · 상태 · 마지막 시각 · 숫자들이 줄마다 같은 자리에 오는 칸(StatusRows) — 문장으로 이어 붙이지 않는다.
type JobState = StatusRowItem & { bad: boolean; short: string }

const problemState = (label: string, short: string, at: string | null, note?: string): JobState =>
  ({ label, bad: true, short, tone: 'bad', state: short, at: at ? formatDateTime(at) : null, ...(note ? { note } : {}) })

// 매일 도는 작업(구글 환불 확인·원화 환산)에 공통인 판정 — 멈춤·한 번도 안 돎·하루 넘게 안 돎.
function dailyJobProblem(label: string, s: RefundStatus | undefined): JobState | null {
  if (stalled(s)) return problemState(label, '중간에 멈춤', s!.last_started_at, '시작한 작업이 끝나지 않았습니다.')
  if (!s?.last_finished_at) return problemState(label, '한 번도 안 돎', null, '설치가 끝났는지 확인해 주세요.')
  if (s.last_message === 'project_url_missing') return problemState(label, '설치가 끝나지 않음', s.last_finished_at)
  if (overdue(s)) return problemState(label, '하루 넘게 안 돎', s.last_finished_at, '하루 한 번 도는 작업이 그 뒤로 돌지 않았습니다.')
  return null
}

function googleRefundState(s: RefundStatus | undefined): JobState {
  const label = '구글 환불 확인'
  const problem = dailyJobProblem(label, s)
  if (problem) return problem
  const recheck = testRecheckFacts(s!.last_message)
  if (s!.last_ok === false) return { ...problemState(label, '실패', s!.last_finished_at, refundFailText(s!.last_message)), facts: recheck }
  return {
    label, bad: false, short: '', tone: 'ok', state: '정상', at: formatDateTime(s!.last_finished_at!),
    facts: [{ label: '새 환불', value: `${formatCount(s!.last_marked)}건` }, ...recheck],
  }
}

// 애플은 환불이 생길 때만 알림이 온다 — 오래 비어 있어도 고장이 아니라 오래됐는지는 보지 않는다. 시각은 마지막 알림.
function appleRefundState(s: RefundStatus | undefined): JobState {
  const label = '애플 환불 알림'
  if (!s?.last_finished_at) return { label, bad: false, short: '', tone: 'muted', state: '받은 알림 없음', at: null }
  if (s.last_ok === false) return problemState(label, '처리 실패', s.last_finished_at)
  return { label, bad: false, short: '', tone: 'ok', state: '정상', at: formatDateTime(s.last_finished_at) }
}

// 환율표 저장·빈 금액 채우기(purchase-price-sync)의 마지막 실행. 메모는 함수가 운영자용 문장으로 남긴다.
function priceSyncState(s: RefundStatus | undefined): JobState {
  const label = '원화 환산'
  const problem = dailyJobProblem(label, s)
  if (problem) return problem
  const { facts, note } = memoFacts(s!.last_message)
  const base = s!.last_ok === false
    ? problemState(label, '실패', s!.last_finished_at)
    : { label, bad: false, short: '', tone: 'ok' as const, state: '정상', at: formatDateTime(s!.last_finished_at!) }
  return { ...base, facts, ...(note ? { note } : {}) }
}

// 같은 실행에서 시험 여부를 모르는 옛 결제를 구글에 다시 물은 결과(purchase-refund-sync-google 의 메모 꼴을 읽는다).
function testRecheckFacts(message: string | null | undefined): StatusFact[] {
  const m = /시험 결제 확인 (\d+)건\(그중 시험 (\d+)건\)(?:, 구글이 답하지 않은 (\d+)건)?/.exec(message ?? '')
  if (!m) return []
  const [, filled, tests, unanswered] = m
  return [
    { label: '옛 결제 시험 여부 확인', value: `${filled}건` },
    { label: '그중 시험', value: `${tests}건` },
    ...(unanswered ? [{ label: '구글이 답하지 않음', value: `${unanswered}건`, tone: 'warn' as const }] : []),
  ]
}

// 원화 환산 메모("환율 170개 통화 / 애플 결제 금액 확인 1건, 애플에 없는 거래 2건 / …")를 이름·숫자 칸으로 나눈다.
// 숫자로 끝나지 않는 조각(오류 문장 등)은 나누지 않고 한마디로 둔다.
function memoFacts(message: string | null | undefined): { facts: StatusFact[]; note: string } {
  const facts: StatusFact[] = []
  const rest: string[] = []
  for (const part of (message ?? '').split(/\s*\/\s*|,\s*/)) {
    if (!part.trim()) continue
    const m = /^([^:\d]+?)\s+(\d[\d,]*(?:개 통화|건)(?: 이상)?)$/.exec(part.trim())
    if (m) facts.push({ label: m[1]!, value: m[2]! })
    else rest.push(part.trim())
  }
  return { facts, note: rest.join(' · ') }
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

// 환불·테스트·무료 표시는 상품 ID 앞에 둔다 — 뒤에 두면 ID 길이에 따라 줄마다 자리가 달라진다.
const COLUMNS: Column<PurchaseRow>[] = [
  { key: 'at', header: '결제 확인 시각', kind: 'datetime', cell: (r) => formatDateTime(r.verified_at) },
  { key: 'player', header: '플레이어', kind: 'name', cell: (r) => <PlayerLink accountId={r.account_id} name={r.display_name} userId={r.user_id} /> },
  { key: 'account', header: '계정 ID', kind: 'name', cell: (r) => r.account_id && <AccountIdCell accountId={r.account_id} /> },
  {
    key: 'product', header: '상품', kind: 'text', full: (r) => r.product_id, cell: (r) => (
      <>
        {r.refunded_at && <span className="mr-1.5"><Tag tone="bad" title={`환불 ${formatDateTime(r.refunded_at)}`}>환불</Tag></span>}
        {r.is_test && <span className="mr-1.5"><Tag title="시험 결제 — 매출에 넣지 않습니다">테스트</Tag></span>}
        {r.is_free && <span className="mr-1.5"><Tag title="프로모션 코드·광고 보상으로 받은 무료 결제 — 매출에 넣지 않습니다">무료</Tag></span>}
        <span className={`font-mono text-[13px] ${r.refunded_at ? 'line-through text-neutral-400' : ''}`}>{r.product_id}</span>
      </>
    ),
  },
  { key: 'store', header: '스토어', kind: 'name', cell: (r) => STORE_LABEL[r.store] ?? r.store },
  {
    key: 'price', header: '금액', kind: 'number', cell: (r) => {
      const text = r.price_amount_krw != null
        ? formatKRW(r.price_amount_krw)
        : r.price_amount != null
          ? `${(priceFromMicros(r.price_amount) ?? 0).toLocaleString('ko-KR', { maximumFractionDigits: 2 })} ${r.price_currency ?? ''}`
          : null
      return text && <span className={r.refunded_at ? 'line-through text-neutral-400' : ''}>{text}</span>
    },
  },
  { key: 'order', header: '주문번호', kind: 'mono', cell: (r) => r.order_id },
]

export default function Purchases({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const filter = useFilterPlayer()
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [refundedOnly, setRefundedOnly] = useState(false)
  // 라이브 결제(exclude: 시험이 아닌 것 — 무료·시험 여부를 모르는 옛 결제 포함) 또는 테스트 결제(only)만 본다.
  // 둘을 섞어 보는 "전체"는 두지 않는다 — 테스트 결제는 따로 보는 것이 의도다(2026-10-29 사용자 결정).
  const [test, setTest] = useState<'exclude' | 'only'>('exclude')
  const [page, setPage] = useState(1)
  useOnChange(filter.accountId, () => setPage(1))
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

  // 조건을 빠르게 바꾸면 늦게 온 옛 응답이 새 목록을 덮는다 — 마지막 요청만 반영한다.
  const loadReq = useRef(0)
  const load = useCallback(async () => {
    const req = ++loadReq.current
    setLoading(true)
    setLoadFailed(false)
    try {
      const res = await callAdmin<PurchaseData>(target, 'purchases.list', {
        search, accountId: filter.accountId || null, startDate: dateFrom || null, endDate: dateTo || null, page, refundedOnly, test,
      })
      if (req === loadReq.current) setData(res)
    } catch (e: unknown) {
      if (req !== loadReq.current) return
      report(e, '구매 내역을 불러오지 못했습니다.')
      setData(null)
      setLoadFailed(true)
    } finally {
      if (req === loadReq.current) setLoading(false)
    }
  }, [target, search, filter.accountId, dateFrom, dateTo, page, refundedOnly, test, report])

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
  const resetSearch = () => {
    setSearchInput(''); setSearch(''); setDateFrom(''); setDateTo(''); setRefundedOnly(false); setTest('exclude'); setPage(1)
    filter.clear()
  }
  const clearPlayer = () => { filter.clear(); setPage(1) }

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
      const res = await callAdmin<{ rows: PurchaseRow[]; truncated: boolean; nameSearchTruncated?: boolean }>(target, 'purchases.list', {
        search, accountId: filter.accountId || null, startDate: dateFrom || null, endDate: dateTo || null, refundedOnly, test, all: true,
      })
      downloadCsv(
        `구매내역_${projectName(target)}_${todayStamp()}.csv`,
        ['결제 확인 시각', '스토어', '상품 ID', '주문번호', '닉네임', '유저 ID', '계정 ID', '금액', '통화', '원화 환산', '환불일', '시험 결제', '무료'],
        res.rows.map((r) => [
          formatDateTime(r.verified_at),
          STORE_LABEL[r.store] ?? r.store,
          r.product_id,
          r.order_id,
          r.display_name,
          r.user_id,
          r.account_id,
          priceFromMicros(r.price_amount),
          r.price_currency,
          r.price_amount_krw,
          r.refunded_at ? formatDateTime(r.refunded_at) : null,
          r.is_test == null ? null : r.is_test ? '예' : '아니요',
          // 무료 결제(프로모션·광고 보상)도 원화 금액이 적혀 있어, 이 칸 없이 원화를 더하면 매출 화면보다 커진다.
          r.is_free ? '예' : null,
        ]),
      )
      if (res.truncated) setError('결제가 너무 많아 앞의 10만 건만 담았습니다. 기간을 나눠 내려받으세요.')
      else if (res.nameSearchTruncated) setError('닉네임이 맞는 플레이어가 많아 앞 100명의 결제만 담았습니다.')
    } catch (e: unknown) {
      report(e, '내려받지 못했습니다.')
    } finally {
      setExporting(false)
    }
  }

  const statusByStore = new Map(refundStatus.map((s) => [s.store, s]))
  // 상태를 못 불러왔으면(빈 목록) "한 번도 안 돎"·"모두 정상" 어느 쪽으로도 판정하지 않는다.
  const jobs = refundStatusState === 'ok'
    ? [googleRefundState(statusByStore.get('google_play')), appleRefundState(statusByStore.get('apple_app_store')), priceSyncState(statusByStore.get('price_sync'))]
    : []
  const problems = jobs.filter((j) => j.bad)

  return (
    <div className="space-y-4">
      {/* 내려받기는 화면 전체에 대한 일이라 제목 오른쪽에 — 검색 줄에 두면 줄이 넘쳐 새로고침이 둘째 줄로 떨어졌다. */}
      <PageHeader
        title="구매 내역"
        description="검증된 인앱 결제 내역을 조회합니다."
        actions={
          <Button variant="outline" onClick={() => void exportCsv()} disabled={exporting || total === 0} title="지금 검색 조건에 맞는 결제 전체를 내려받습니다">
            {exporting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Download className="w-4 h-4" />}CSV 내려받기
          </Button>
        }
      />

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
        <FilterBar>
          <FilterField label="결제 종류">
            <Segmented value={test} onChange={(v) => { setTest(v); setPage(1) }} options={[
              { v: 'exclude', label: '라이브' },
              { v: 'only', label: '테스트' },
            ]} />
          </FilterField>
          <FilterField label="기간">
            <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
          </FilterField>
          {filter.accountId && (
            <FilterField label="플레이어">
              <PlayerFilterChip accountId={filter.accountId} name={filter.name} onClear={clearPlayer} />
            </FilterField>
          )}
          <FilterField label="검색" className="flex-1 min-w-[200px]">
            <input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
              placeholder="상품 ID / 플레이어 / 주문번호"
              className={inputCls + ' w-full'}
            />
          </FilterField>
          <label className="flex h-9 items-center gap-1.5 text-xs text-neutral-600 cursor-pointer whitespace-nowrap">
            <input type="checkbox" checked={refundedOnly} onChange={(e) => { setRefundedOnly(e.target.checked); setPage(1) }} className="rounded" />
            환불만
          </label>
          <SearchActions onSearch={submitSearch} onReset={resetSearch} loading={loading} />
          <RefreshButton className="ml-auto" onClick={() => void load()} loading={loading} />
        </FilterBar>
        {test === 'only' && (
          <div className="text-xs text-neutral-500">애플 샌드박스와 구글 라이선스 테스트 계정의 결제입니다. 매출에 넣지 않습니다.</div>
        )}
        {data?.nameSearchTruncated && (
          <div className="text-xs text-neutral-500">닉네임이 맞는 플레이어가 많아 앞 100명의 결제만 찾았습니다.</div>
        )}
        <div className="border-t border-neutral-100 pt-3 text-sm">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            {refundStatusState === 'loading' && <span className="text-neutral-400">결제 자동 작업 상태 확인 중…</span>}
            {refundStatusState === 'failed' && <span className="text-red-600">결제 자동 작업 상태를 불러오지 못했습니다.</span>}
            {refundStatusState === 'ok' && (problems.length === 0
              ? <StatusText tone="ok">결제 자동 작업 모두 정상</StatusText>
              : problems.map((j) => <span key={j.label} className="text-red-600">{j.label}: {j.short}</span>))}
            {refundStatusState === 'ok' && (
              <button type="button" onClick={() => setJobsOpen((v) => !v)}
                className="inline-flex items-center gap-0.5 text-[#1677ff] hover:underline">
                {jobsOpen ? '접기' : '자세히'}
                {jobsOpen ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
              </button>
            )}
            <Button size="sm" className="ml-auto" onClick={() => void syncRefunds()} disabled={syncing} title="구글에 환불된 결제가 있는지 지금 확인합니다. 하루 한 번 자동으로도 확인합니다">
              {syncing ? <Loader2 className="w-3 h-3 animate-spin" /> : <RefreshCw className="w-3 h-3" />}구글 환불 지금 확인
            </Button>
          </div>
          {jobsOpen && <div className="mt-3"><StatusRows items={jobs} /></div>}
        </div>
      </WhiteCard>

      <TableCard
        wide
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.id}
        loading={loading}
        failed={loadFailed}
        emptyText="구매 내역이 없습니다."
        footer={<TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />}
      />
    </div>
  )
}
