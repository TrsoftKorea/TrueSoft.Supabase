import { useEffect, useRef, useState, useCallback } from 'react'
import { ChevronDown, ChevronUp, Trash2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { serverTargetLabel, useServerName } from '../lib/serverNames'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { inputCls } from '../components/ui/Input'
import { TableFooter } from '../components/ui/TableFooter'
import { PageHeader } from '../components/ui/PageHeader'
import { TableCard } from '../components/ui/DataTable'
import { StatusText, Tag, Tags, type Tone } from '../components/ui/StatusText'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { formatDateTime, formatCount, formatTotal, itemLabels } from '../components/ui/format'
import { Button } from '../components/ui/Button'
import { FilterBar, FilterField, RefreshButton, SearchActions } from '../components/ui/FilterBar'
import { PlayerLink } from '../components/ui/PlayerLink'
import { Tabs } from '../components/ui/Tabs'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { InfoList } from '../components/ui/InfoList'
import { MailContent, type MailLocalized } from '../components/MailContent'
import { useDefaultLanguage } from '../lib/languages'

type MailRow = {
  id: string
  account_id: string | null
  user_id: string | null
  display_name: string | null
  category: string | null
  title: string | null
  content: string | null
  localized?: MailLocalized
  items: { key: string; count: number }[] | null
  items_claimed_at: string | null
  deleted_at: string | null
  expires_at: string
  created_at: string
  server_code: string | null
}
type MailData = { rows: MailRow[]; total: number; pageSize: number }

type BatchRow = {
  id: string; target_mode: string; title: string
  items: { key: string; count: number }[] | null; recipient_count: number
  category: string; expires_at: string; created_by: string | null; created_at: string; server_code: string | null
  open_until: string | null; recalled_at?: string | null
}
type BatchData = { rows: BatchRow[]; total: number; pageSize: number }
type CategoryRow = { key: string; display_name: string }
type GameItem = { key: string; display_name: string }
type BatchDetail = {
  batch: BatchRow & { recalled_at: string | null; recalled_count: number; content?: string | null; localized?: MailLocalized }
  stats: { claimed_count: number; deleted_count: number; unclaimed_count: number }
}

const MODE_LABEL: Record<string, string> = { all: '전체', server: '서버', players: '플레이어', operators: '시험 발송' }
// 값은 서버가 그대로 비교하는 글자다. 「삭제」는 플레이어가 자기 우편함에서 지운 것 — 운영자 「우편 삭제」와 갈리게 이름만 다르게 보인다.
const STATUS_OPTIONS: [string, string][] = [['전체', '전체'], ['미수령', '미수령'], ['수령', '수령'], ['삭제', '플레이어 삭제']]

function statusOf(r: MailRow): { label: string; tone: Tone } {
  if (r.deleted_at) return { label: '플레이어 삭제', tone: 'muted' }
  if (r.items_claimed_at) return { label: '수령', tone: 'ok' }
  return { label: '미수령', tone: 'plain' }
}

export default function MailRecords({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const serverName = useServerName(target)
  const defaultLang = useDefaultLanguage(target)
  const [tab, setTab] = useState<'batches' | 'records'>('batches')
  const [error, setError] = useState('')

  // 발송 단위(배치) 탭
  const [batchSearchInput, setBatchSearchInput] = useState('')
  const [batchSearch, setBatchSearch] = useState('')
  const [batchCategory, setBatchCategory] = useState('')
  const [batchPage, setBatchPage] = useState(1)
  const [batchData, setBatchData] = useState<BatchData | null>(null)
  const [batchLoading, setBatchLoading] = useState(false)
  const [batchFailed, setBatchFailed] = useState(false)

  // 개별 우편 탭
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [status, setStatus] = useState('전체')
  const [category, setCategory] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [recData, setRecData] = useState<MailData | null>(null)
  const [recLoading, setRecLoading] = useState(false)
  const [recFailed, setRecFailed] = useState(false)

  const [categories, setCategories] = useState<CategoryRow[]>([])
  const [catalog, setCatalog] = useState<GameItem[]>([])

  // 드릴다운: 선택된 배치(개별 우편을 이 배치로 필터)
  const [selectedBatch, setSelectedBatch] = useState<{ id: string; title: string } | null>(null)
  const [detail, setDetail] = useState<BatchDetail | null>(null)
  const [detailTick, setDetailTick] = useState(0)
  const [detailFailed, setDetailFailed] = useState(false)
  const [undo, setUndo] = useState<'recall' | 'close' | null>(null)
  const [undoBusy, setUndoBusy] = useState(false)
  const [undoError, setUndoError] = useState('')
  const [notice, setNotice] = useState('')
  const [showContent, setShowContent] = useState(false)

  // 개별 우편 지우기 — 고른 것은 지금 보이는 쪽 안에서만 유지한다(쪽·거르기가 바뀌면 비운다).
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [delOpen, setDelOpen] = useState(false)
  const [delBusy, setDelBusy] = useState(false)
  const [delError, setDelError] = useState('')

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

  useEffect(() => {
    void (async () => {
      try {
        const [res, items] = await Promise.all([
          callAdmin<{ rows: CategoryRow[] }>(target, 'mails.getCategories'),
          callAdmin<GameItem[]>(target, 'gameItems.list'),
        ])
        setCategories(res.rows)
        setCatalog(items)
      } catch (e: unknown) {
        report(e, '분류와 아이템 목록을 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  // 쪽을 빠르게 넘기면 늦게 온 앞 쪽 응답이 지금 쪽을 덮는다. 마지막 요청만 반영한다.
  const batchReq = useRef(0)
  const recReq = useRef(0)
  const loadBatches = useCallback(async () => {
    const req = ++batchReq.current
    setBatchLoading(true)
    // 시작할 때 지운다 — 성공했을 때 지우면 함께 나간 분류·아이템 조회의 실패 표시까지 사라진다.
    setError('')
    try {
      const res = await callAdmin<BatchData>(target, 'mails.getBatches', { page: batchPage, search: batchSearch, category: batchCategory })
      if (req !== batchReq.current) return
      setBatchData(res)
      setBatchFailed(false)
    } catch (e: unknown) {
      if (req !== batchReq.current) return
      report(e, '발송 단위를 불러오지 못했습니다.')
      setBatchData(null)
      setBatchFailed(true)
    } finally {
      if (req === batchReq.current) setBatchLoading(false)
    }
  }, [target, batchPage, batchSearch, batchCategory, report])
  useEffect(() => { if (tab === 'batches') void loadBatches() }, [tab, loadBatches])

  const loadRecords = useCallback(async () => {
    const req = ++recReq.current
    setRecLoading(true)
    setError('')
    try {
      const res = await callAdmin<MailData>(target, 'mails.getRecords', {
        search, status, category, startDate: dateFrom || null, endDate: dateTo || null, batchId: selectedBatch?.id ?? null, page,
      })
      if (req !== recReq.current) return
      setRecData(res)
      setRecFailed(false)
    } catch (e: unknown) {
      if (req !== recReq.current) return
      report(e, '우편 내역을 불러오지 못했습니다.')
      setRecData(null)
      setRecFailed(true)
    } finally {
      if (req === recReq.current) setRecLoading(false)
    }
  }, [target, search, status, category, dateFrom, dateTo, selectedBatch, page, report])
  useEffect(() => { if (tab === 'records') void loadRecords() }, [tab, loadRecords])
  useEffect(() => { setPicked(new Set()) }, [recData])

  const runDelete = async () => {
    if (delBusy || picked.size === 0) return
    setDelBusy(true); setDelError('')
    try {
      const asked = picked.size
      const res = await callAdmin<{ count: number }>(target, 'mails.deleteMails', { mailIds: [...picked] })
      const n = res?.count ?? 0
      setNotice(n < asked
        ? `${formatCount(n)}통 삭제했습니다. 받은 ${formatCount(asked - n)}통은 남았습니다.`
        : `${formatCount(n)}통 삭제했습니다.`)
      setDelOpen(false)
      if (selectedBatch) setDetailTick((t) => t + 1)
      void loadRecords()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDelError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setDelBusy(false)
    }
  }

  useEffect(() => {
    if (!selectedBatch) {
      setDetail(null)
      return
    }
    setDetailFailed(false)
    void (async () => {
      try {
        setDetail(await callAdmin<BatchDetail>(target, 'mails.getBatchDetail', { batchId: selectedBatch.id }))
      } catch (e: unknown) {
        setDetailFailed(true)
        report(e, '발송 단위 상세를 불러오지 못했습니다.')
      }
    })()
  }, [target, selectedBatch, report, detailTick])

  const detailPending = detailFailed ? '불러오지 못함' : '…'

  const runUndo = async () => {
    if (!selectedBatch || !undo || undoBusy) return
    setUndoBusy(true); setUndoError('')
    try {
      if (undo === 'recall') {
        const res = await callAdmin<{ count: number }>(target, 'mails.recallBatch', { batchId: selectedBatch.id })
        setNotice(`${formatCount(res?.count ?? 0)}통 취소했습니다.`)
      } else {
        await callAdmin(target, 'mails.closeBatch', { batchId: selectedBatch.id })
        setNotice('배달을 멈췄습니다.')
      }
      setUndo(null)
      setDetailTick((t) => t + 1)
      void loadRecords()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setUndoError(e instanceof Error ? e.message : '실패했습니다.')
    } finally {
      setUndoBusy(false)
    }
  }

  const stillOpen = !!detail?.batch.open_until && new Date(detail.batch.open_until).getTime() > Date.now()

  const batches = batchData?.rows ?? []
  const batchTotal = batchData?.total ?? 0
  const batchPageSize = batchData?.pageSize ?? 20
  const batchTotalPages = Math.max(1, Math.ceil(batchTotal / batchPageSize))

  const rows = recData?.rows ?? []
  const total = recData?.total ?? 0
  const pageSize = recData?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  // 분류는 코드 대신 등록된 표시명으로 노출. 미등록 분류는 코드를 그대로 보여준다.
  const catLabel = (key: string | null) => {
    if (!key) return '-'
    const c = categories.find((x) => x.key === key)
    // 기본 분류 행이 없어도 고르는 목록처럼 "기본"으로 보인다.
    return c?.display_name || (key === 'default' ? '기본' : key)
  }

  const openBatch = (b: BatchRow) => {
    setSelectedBatch({ id: b.id, title: b.title })
    setShowContent(false)
    setSearch(''); setSearchInput(''); setStatus('전체'); setCategory(''); setDateFrom(''); setDateTo(''); setPage(1)
    setTab('records')
  }
  const clearBatch = () => { setSelectedBatch(null); setPage(1); setNotice('') }

  const submitBatchSearch = () => { setBatchSearch(batchSearchInput); setBatchPage(1) }
  const resetBatchSearch = () => { setBatchSearchInput(''); setBatchSearch(''); setBatchCategory(''); setBatchPage(1) }
  const submitSearch = () => { setSearch(searchInput); setPage(1) }
  const resetSearch = () => {
    setSearchInput(''); setSearch(''); setDateFrom(''); setDateTo(''); setStatus('전체'); setCategory(''); setPage(1)
  }


  const renderCategoryOptions = () => (
    <>
      <option value="">전체 분류</option>
      {!categories.some((c) => c.key === 'default') && <option value="default">기본</option>}
      {categories.map((c) => (
        <option key={c.key} value={c.key}>{c.display_name || c.key}</option>
      ))}
    </>
  )

  return (
    <div className="space-y-4">
      <PageHeader title="우편 내역" description="발송한 우편 내역을 조회합니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <Tabs value={tab} onChange={setTab} tabs={[['batches', '발송 단위'], ['records', '개별 우편']]} />

      {tab === 'batches' && (
        <>
          <WhiteCard className="p-4">
            <FilterBar>
              <FilterField label="분류">
                <select value={batchCategory} onChange={(e) => { setBatchCategory(e.target.value); setBatchPage(1) }} className={inputCls + ' w-40'}>
                  {renderCategoryOptions()}
                </select>
              </FilterField>
              <FilterField label="제목" className="flex-1 min-w-[200px]">
                <input value={batchSearchInput} onChange={(e) => setBatchSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitBatchSearch()} className={inputCls + ' w-full'} />
              </FilterField>
              <SearchActions onSearch={submitBatchSearch} onReset={resetBatchSearch} loading={batchLoading} />
              <RefreshButton className="ml-auto" onClick={() => void loadBatches()} loading={batchLoading} />
            </FilterBar>
          </WhiteCard>

          <TableCard
            wide
            columns={[
              {
                key: 'target', header: '대상', kind: 'name', cell: (b) => (
                  <>
                    <span className="inline-flex items-center gap-1.5">
                      {b.recalled_at && <Tag>취소</Tag>}
                      {b.server_code ? serverTargetLabel(serverName(b.server_code)) : MODE_LABEL[b.target_mode] ?? b.target_mode}
                    </span>
                    {b.open_until && <div className="text-xs text-neutral-500">나중에 들어온 플레이어도</div>}
                  </>
                ),
              },
              { key: 'category', header: '분류', kind: 'name', cell: (b) => (b.category ? catLabel(b.category) : null) },
              { key: 'title', header: '제목', kind: 'text', cardTitle: true, cell: (b) => b.title || null },
              { key: 'items', header: '보상', kind: 'name', cell: (b) => (b.items?.length ? <Tags items={itemLabels(b.items, catalog)} /> : null) },
              { key: 'recipients', header: '수신자', kind: 'number', cell: (b) => formatCount(b.recipient_count) },
              {
                // 운영자는 보낸 시각 아래 둘째 줄로 — 따로 칸을 두면 제목·보상 칸과 함께 1280px 에서 표가 넘친다.
                key: 'at', header: '보낸 시각', kind: 'datetime', cell: (b) => (
                  <>
                    <div>{formatDateTime(b.created_at)}</div>
                    {b.created_by && <div className="text-xs text-neutral-500">{b.created_by}</div>}
                  </>
                ),
              },
            ]}
            rows={batches}
            rowKey={(b) => b.id}
            loading={batchLoading}
            failed={batchFailed}
            emptyText="발송 이력이 없습니다."
            onRowClick={openBatch}
            footer={<TableFooter summary={formatTotal(batchTotal)} page={batchPage} totalPages={batchTotalPages} onPageChange={setBatchPage} />}
          />
        </>
      )}

      {tab === 'records' && (
        <>
          {selectedBatch && (
            <div className="flex items-center gap-4 rounded-md bg-brand-soft border border-[#1677ff]/20 px-3 py-2 text-sm">
              <InfoList className="flex-1 min-w-0" items={[
                { label: '발송', value: <span className="text-[#1677ff] font-medium">{selectedBatch.title}</span> },
                { label: '대상', value: detail?.stats ? `${formatCount(detail.batch?.recipient_count ?? 0)}명` : detailPending },
                { label: '수령', value: detail?.stats ? formatCount(detail.stats.claimed_count) : detailPending },
                { label: '플레이어 삭제', value: detail?.stats ? formatCount(detail.stats.deleted_count) : detailPending },
                { label: '취소한 우편', value: !detail ? detailPending : detail.batch.recalled_at ? `${formatCount(detail.batch.recalled_count)}통` : null },
              ]} />
              <div className="ml-auto flex items-center gap-1.5 shrink-0">
                {stillOpen && (
                  <Button variant="outline" size="compact" onClick={() => { setUndoError(''); setUndo('close') }}>
                    배달 멈춤
                  </Button>
                )}
                {(detail?.stats.unclaimed_count ?? 0) > 0 && (
                  <Button variant="danger" size="compact" onClick={() => { setUndoError(''); setUndo('recall') }}>
                    우편 취소
                  </Button>
                )}
                <button onClick={clearBatch} className="text-neutral-400 hover:text-neutral-700" title="이 발송 단위만 보기 해제" aria-label="이 발송 단위만 보기 해제"><X className="w-4 h-4" /></button>
              </div>
            </div>
          )}
          {selectedBatch && (
            // 본문이 길면 아래 우편 목록을 밀어내므로 접어 둔다.
            <WhiteCard className="p-5 space-y-4">
              <CardHeader title="우편 내용" actions={
                <Button variant="outline" size="compact" onClick={() => setShowContent((v) => !v)}>
                  {showContent ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}{showContent ? '접기' : '펼치기'}
                </Button>
              } />
              {showContent && (detail
                ? <MailContent title={detail.batch.title} content={detail.batch.content} localized={detail.batch.localized} items={detail.batch.items} catalog={catalog} defaultLang={defaultLang} />
                : <div className="text-sm text-neutral-400">{detailPending}</div>)}
            </WhiteCard>
          )}
          {notice && <div className="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">{notice}</div>}

          <ConfirmDialog
            open={undo !== null}
            title={undo === 'recall' ? '우편을 취소할까요?' : '배달을 멈출까요?'}
            confirmLabel={undo === 'recall' ? '우편 취소' : '배달 멈춤'}
            danger={undo === 'recall'}
            busy={undoBusy}
            error={undoError || undefined}
            onConfirm={() => { void runUndo() }}
            onCancel={() => setUndo(null)}
            description={undo === 'recall'
              ? '안 받은 우편이 사라지고 더 배달되지 않습니다. 되돌릴 수 없습니다.'
              : '새로 들어온 플레이어에게 더 보내지 않습니다.'}
          >
            <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-2">
              <div className="text-neutral-800">{selectedBatch?.title}</div>
              {undo === 'recall' && detail && (
                <InfoList items={[
                  { label: '취소할 우편', value: `${formatCount(detail.stats.unclaimed_count)}통`, tone: 'danger' },
                  { label: '받음', value: `${formatCount(detail.stats.claimed_count)}통` },
                ]} />
              )}
            </div>
          </ConfirmDialog>

          <ConfirmDialog
            open={delOpen}
            title={`우편 ${formatCount(picked.size)}통을 삭제할까요?`}
            confirmLabel="삭제"
            danger
            busy={delBusy}
            error={delError || undefined}
            onConfirm={() => { void runDelete() }}
            onCancel={() => setDelOpen(false)}
            description="되돌릴 수 없습니다. 받은 우편은 남습니다."
          />

          <WhiteCard className="p-4">
            <FilterBar>
              <FilterField label="기간">
                <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
              </FilterField>
              <FilterField label="상태">
                <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }} className={inputCls}>
                  {STATUS_OPTIONS.map(([v, label]) => (<option key={v} value={v}>{label}</option>))}
                </select>
              </FilterField>
              <FilterField label="분류">
                <select value={category} onChange={(e) => { setCategory(e.target.value); setPage(1) }} className={inputCls + ' w-40'}>
                  {renderCategoryOptions()}
                </select>
              </FilterField>
              <FilterField label="플레이어" className="flex-1 min-w-[200px]">
                <input value={searchInput} onChange={(e) => setSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitSearch()} placeholder="닉네임 / 계정 ID" className={inputCls + ' w-full'} />
              </FilterField>
              <SearchActions onSearch={submitSearch} onReset={resetSearch} loading={recLoading} />
              <RefreshButton className="ml-auto" onClick={() => void loadRecords()} loading={recLoading} />
            </FilterBar>
          </WhiteCard>

          <TableCard
            wide
            columns={[
              {
                // 받은 시각은 "수령" 아래 둘째 줄로 — 따로 칸을 두면 제목·보상 칸과 함께 1280px 에서 표가 넘친다.
                key: 'status', header: '상태', kind: 'status', cell: (r) => {
                  const st = statusOf(r)
                  return (
                    <>
                      <StatusText tone={st.tone}>{st.label}</StatusText>
                      {r.items_claimed_at && <div className="text-xs text-neutral-500 tabular-nums">{formatDateTime(r.items_claimed_at)}</div>}
                    </>
                  )
                },
              },
              { key: 'player', header: '플레이어', kind: 'name', cell: (r) => <PlayerLink accountId={r.account_id} name={r.display_name} userId={r.user_id} /> },
              // 본문·언어별 글은 줄을 펼쳐서 본다. 보상은 짧아 내용 폭만 쓴다.
              {
                // 분류는 기본이 아닐 때만 제목 앞 표시로 — 따로 칸을 두면 1280px 에서 표가 넘친다. 분류로 거르기는 위 검색 줄이 한다.
                key: 'title', header: '제목', kind: 'text', cardTitle: true,
                full: (r) => r.title ?? '',
                cell: (r) => (!r.title ? null : (
                  <span className="inline-flex items-center gap-1.5 max-w-full">
                    {r.category && r.category !== 'default' && <Tag>{catLabel(r.category)}</Tag>}
                    <span className="truncate">{r.title}</span>
                  </span>
                )),
              },
              { key: 'items', header: '보상', kind: 'name', cell: (r) => (r.items?.length ? <Tags items={itemLabels(r.items, catalog)} /> : null) },
              {
                // 만료 시각은 둘째 줄로 — 따로 칸을 두면 고르기 칸과 함께 1280px 에서 표가 넘친다.
                key: 'created', header: '등록 시각', kind: 'datetime', cell: (r) => (
                  <>
                    <div>{formatDateTime(r.created_at)}</div>
                    <div className="text-xs text-neutral-500">만료 {formatDateTime(r.expires_at)}</div>
                  </>
                ),
              },
            ]}
            rows={rows}
            rowKey={(r) => r.id}
            loading={recLoading}
            failed={recFailed}
            emptyText="우편 내역이 없습니다."
            // 받은 우편은 지급 기록이라 서버가 지우지 않는다 — 고를 수도 없게 막는다.
            select={{ picked, onChange: setPicked, can: (r) => !r.items_claimed_at, blockedTitle: '받은 우편' }}
            actions={
              <>
                {picked.size > 0 && <span className="text-sm text-neutral-500">{formatCount(picked.size)}통</span>}
                <Button variant="dangerOutline" size="compact" disabled={picked.size === 0} onClick={() => { setDelError(''); setDelOpen(true) }}>
                  <Trash2 className="w-4 h-4" />우편 삭제
                </Button>
              </>
            }
            expand={(r) => <MailContent title={r.title} content={r.content} localized={r.localized} items={r.items} catalog={catalog} defaultLang={defaultLang} />}
            footer={<TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />}
          />
        </>
      )}
    </div>
  )
}
