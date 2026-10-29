import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, Loader2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { serverTargetLabel, useServerName } from '../lib/serverNames'
import { platformLabel } from '../lib/platforms'
import { WhiteCard } from '../components/ui/Card'
import { inputCls } from '../components/ui/Input'
import { TableFooter } from '../components/ui/TableFooter'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { formatDateTime, formatCount, formatTotal, itemsText } from '../components/ui/format'
import { Button } from '../components/ui/Button'
import { Tabs } from '../components/ui/Tabs'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { InfoList } from '../components/ui/InfoList'

type MailRow = {
  id: string
  account_id: string | null
  user_id: string | null
  display_name: string | null
  category: string | null
  title: string | null
  content: string | null
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
  platform: string | null; open_until: string | null; recalled_at?: string | null
}
type BatchData = { rows: BatchRow[]; total: number; pageSize: number }
type CategoryRow = { key: string; display_name: string }
type GameItem = { key: string; display_name: string }
type BatchDetail = {
  batch: BatchRow & { recalled_at: string | null; recalled_count: number }
  stats: { claimed_count: number; deleted_count: number; unclaimed_count: number }
}

const MODE_LABEL: Record<string, string> = { all: '전체', server: '서버', players: '플레이어' }
const STATUS_OPTIONS = ['전체', '미수령', '수령', '삭제']

function statusOf(r: MailRow): { label: string; cls: string } {
  if (r.deleted_at) return { label: '삭제', cls: 'bg-neutral-200 text-neutral-600' }
  if (r.items_claimed_at) return { label: '수령', cls: 'bg-emerald-100 text-emerald-700' }
  return { label: '미수령', cls: 'bg-brand-soft text-[#1677ff]' }
}

export default function MailRecords({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const navigate = useNavigate()
  const serverName = useServerName(target)
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

  const loadBatches = useCallback(async () => {
    setBatchLoading(true)
    // 시작할 때 지운다 — 성공했을 때 지우면 함께 나간 분류·아이템 조회의 실패 표시까지 사라진다.
    setError('')
    try {
      const res = await callAdmin<BatchData>(target, 'mails.getBatches', { page: batchPage, search: batchSearch, category: batchCategory })
      setBatchData(res)
      setBatchFailed(false)
    } catch (e: unknown) {
      report(e, '발송 단위를 불러오지 못했습니다.')
      setBatchData(null)
      setBatchFailed(true)
    } finally {
      setBatchLoading(false)
    }
  }, [target, batchPage, batchSearch, batchCategory, report])
  useEffect(() => { if (tab === 'batches') void loadBatches() }, [tab, loadBatches])

  const loadRecords = useCallback(async () => {
    setRecLoading(true)
    setError('')
    try {
      const res = await callAdmin<MailData>(target, 'mails.getRecords', {
        search, status, category, startDate: dateFrom || null, endDate: dateTo || null, batchId: selectedBatch?.id ?? null, page,
      })
      setRecData(res)
      setRecFailed(false)
    } catch (e: unknown) {
      report(e, '우편 내역을 불러오지 못했습니다.')
      setRecData(null)
      setRecFailed(true)
    } finally {
      setRecLoading(false)
    }
  }, [target, search, status, category, dateFrom, dateTo, selectedBatch, page, report])
  useEffect(() => { if (tab === 'records') void loadRecords() }, [tab, loadRecords])

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
        setNotice(`안 받은 우편 ${formatCount(res?.count ?? 0)}통을 거뒀습니다.`)
      } else {
        await callAdmin(target, 'mails.closeBatch', { batchId: selectedBatch.id })
        setNotice('이 우편은 이제 새로 들어온 플레이어에게 가지 않습니다.')
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
    return c?.display_name || key
  }

  const openBatch = (b: BatchRow) => {
    setSelectedBatch({ id: b.id, title: b.title })
    setSearch(''); setSearchInput(''); setStatus('전체'); setCategory(''); setDateFrom(''); setDateTo(''); setPage(1)
    setTab('records')
  }
  const clearBatch = () => { setSelectedBatch(null); setPage(1); setNotice('') }

  const submitBatchSearch = () => { setBatchSearch(batchSearchInput); setBatchPage(1) }
  const submitSearch = () => { setSearch(searchInput); setPage(1) }

  const goPlayer = (r: MailRow) => {
    if (!r.account_id) return
    navigate(`/players?account=${encodeURIComponent(r.account_id)}&name=${encodeURIComponent(r.display_name ?? '')}`)
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
            <div className="flex flex-wrap items-center gap-3">
              <select value={batchCategory} onChange={(e) => { setBatchCategory(e.target.value); setBatchPage(1) }} className={inputCls + ' w-40'}>
                {renderCategoryOptions()}
              </select>
              <input value={batchSearchInput} onChange={(e) => setBatchSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitBatchSearch()} placeholder="제목 검색" className={inputCls + ' flex-1 min-w-[200px]'} />
              <Button variant="primary" size="compact" onClick={submitBatchSearch} disabled={batchLoading}>
                {batchLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
              </Button>
            </div>
          </WhiteCard>

          <WhiteCard>
            <div className="overflow-auto">
              <table className="w-full text-sm">
                <thead className="bg-neutral-50 text-neutral-500 text-xs">
                  <tr>
                    <th className="text-left px-4 py-2.5 font-medium w-20">대상</th>
                    <th className="text-left px-4 py-2.5 font-medium w-28">분류</th>
                    <th className="text-left px-4 py-2.5 font-medium">제목</th>
                    <th className="text-left px-4 py-2.5 font-medium w-20">수신자</th>
                    <th className="text-left px-4 py-2.5 font-medium">아이템</th>
                    <th className="text-left px-4 py-2.5 font-medium w-24">운영자</th>
                    <th className="text-left px-4 py-2.5 font-medium w-40">발송일</th>
                  </tr>
                </thead>
                <tbody>
                  {batchLoading || batches.length === 0 ? (
                    <TableStatusRow loading={batchLoading} empty={batches.length === 0} failed={batchFailed} colSpan={7} emptyText="발송 이력이 없습니다." />
                  ) : (
                    batches.map((b) => (
                      <tr key={b.id} className="border-t border-neutral-100 hover:bg-neutral-50 cursor-pointer" onClick={() => openBatch(b)}>
                        <td className="px-4 py-3 whitespace-nowrap">{b.server_code ? serverTargetLabel(serverName(b.server_code)) : MODE_LABEL[b.target_mode] ?? b.target_mode}{b.platform ? ` · ${platformLabel(b.platform)}` : ''}{b.open_until ? ' · 나중에 들어온 플레이어도' : ''}{b.recalled_at ? ' · 거둠' : ''}</td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{catLabel(b.category)}</td>
                        <td className="px-4 py-3">{b.title}</td>
                        <td className="px-4 py-3 whitespace-nowrap">{formatCount(b.recipient_count)}</td>
                        <td className="px-4 py-3 text-neutral-600">{itemsText(b.items, catalog)}</td>
                        <td className="px-4 py-3 text-neutral-600">{b.created_by ?? '-'}</td>
                        <td className="px-4 py-3 text-neutral-600">{formatDateTime(b.created_at)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            <TableFooter summary={formatTotal(batchTotal)} page={batchPage} totalPages={batchTotalPages} onPageChange={setBatchPage} />
          </WhiteCard>
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
                { label: '삭제', value: detail?.stats ? formatCount(detail.stats.deleted_count) : detailPending },
                { label: '거둔 우편', value: !detail ? detailPending : detail.batch.recalled_at ? `${formatCount(detail.batch.recalled_count)}통` : null },
              ]} />
              <div className="ml-auto flex items-center gap-1.5 shrink-0">
                {stillOpen && (
                  <Button variant="outline" size="compact" onClick={() => { setUndoError(''); setUndo('close') }}>
                    새로 들어온 플레이어에게 그만 보내기
                  </Button>
                )}
                {(detail?.stats.unclaimed_count ?? 0) > 0 && (
                  <Button variant="danger" size="compact" onClick={() => { setUndoError(''); setUndo('recall') }}>
                    안 받은 우편 거두기
                  </Button>
                )}
                <button onClick={clearBatch} className="text-neutral-400 hover:text-neutral-700" title="이 발송 단위만 보기 해제" aria-label="이 발송 단위만 보기 해제"><X className="w-4 h-4" /></button>
              </div>
            </div>
          )}
          {notice && <div className="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">{notice}</div>}

          <ConfirmDialog
            open={undo !== null}
            title={undo === 'recall' ? '안 받은 우편 거두기' : '새로 들어온 플레이어에게 그만 보내기'}
            confirmLabel={undo === 'recall' ? '거두기' : '그만 보내기'}
            danger={undo === 'recall'}
            busy={undoBusy}
            error={undoError || undefined}
            onConfirm={() => { void runUndo() }}
            onCancel={() => setUndo(null)}
            description={undo === 'recall'
              ? '아직 받지 않은 플레이어의 우편함에서 이 우편이 사라집니다. 이미 받은 플레이어의 우편과 아이템은 그대로입니다. 새로 들어온 플레이어에게도 더 가지 않습니다.'
              : '이미 받은 플레이어의 우편은 그대로 둡니다.'}
          >
            <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-2">
              <div className="text-neutral-800">{selectedBatch?.title}</div>
              {undo === 'recall' && detail && (
                <InfoList items={[
                  { label: '거둘 우편', value: `${formatCount(detail.stats.unclaimed_count)}통`, tone: 'danger' },
                  { label: '이미 받음', value: `${formatCount(detail.stats.claimed_count)}통` },
                ]} />
              )}
            </div>
          </ConfirmDialog>

          <WhiteCard className="p-4">
            <div className="flex flex-wrap items-center gap-3">
              <span className="text-xs text-neutral-500">등록기간</span>
              <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
              <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }} className={inputCls}>
                {STATUS_OPTIONS.map((o) => (<option key={o} value={o}>{o}</option>))}
              </select>
              <select value={category} onChange={(e) => { setCategory(e.target.value); setPage(1) }} className={inputCls + ' w-40'}>
                {renderCategoryOptions()}
              </select>
              <input value={searchInput} onChange={(e) => setSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitSearch()} placeholder="닉네임 / 계정 ID" className={inputCls + ' flex-1 min-w-[200px]'} />
              <Button variant="primary" size="compact" onClick={submitSearch} disabled={recLoading}>
                {recLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
              </Button>
            </div>
          </WhiteCard>

          <WhiteCard>
            <div className="overflow-auto">
              <table className="w-full text-sm">
                <thead className="bg-neutral-50 text-neutral-500 text-xs">
                  <tr>
                    <th className="text-left px-4 py-2.5 font-medium w-20">상태</th>
                    <th className="text-left px-4 py-2.5 font-medium w-24">분류</th>
                    <th className="text-left px-4 py-2.5 font-medium">플레이어</th>
                    <th className="text-left px-4 py-2.5 font-medium">아이템</th>
                    <th className="text-left px-4 py-2.5 font-medium">메시지</th>
                    <th className="text-left px-4 py-2.5 font-medium w-40">수령 일자</th>
                    <th className="text-left px-4 py-2.5 font-medium w-40">만료 일자</th>
                    <th className="text-left px-4 py-2.5 font-medium w-40">등록 일자</th>
                  </tr>
                </thead>
                <tbody>
                  {recLoading || rows.length === 0 ? (
                    <TableStatusRow loading={recLoading} empty={rows.length === 0} failed={recFailed} colSpan={8} emptyText="우편 내역이 없습니다." />
                  ) : (
                    rows.map((r) => {
                      const st = statusOf(r)
                      return (
                        <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50/50">
                          <td className="px-4 py-3 whitespace-nowrap"><span className={'inline-flex items-center px-2 py-0.5 rounded text-xs ' + st.cls}>{st.label}</span></td>
                          <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{catLabel(r.category)}</td>
                          <td className="px-4 py-3">
                            {r.account_id ? (
                              <button onClick={() => goPlayer(r)} className="text-[#1677ff] hover:underline">{r.display_name ?? '(이름 없음)'}</button>
                            ) : (<span className="text-neutral-400">{r.user_id ?? '-'}</span>)}
                          </td>
                          <td className="px-4 py-3 text-neutral-600">{itemsText(r.items, catalog)}</td>
                          <td className="px-4 py-3 text-neutral-700">{r.title || '-'}</td>
                          <td className="px-4 py-3 text-neutral-600">{r.items_claimed_at ? formatDateTime(r.items_claimed_at) : '-'}</td>
                          <td className="px-4 py-3 text-neutral-600">{formatDateTime(r.expires_at)}</td>
                          <td className="px-4 py-3 text-neutral-600">{formatDateTime(r.created_at)}</td>
                        </tr>
                      )
                    })
                  )}
                </tbody>
              </table>
            </div>
            <TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />
          </WhiteCard>
        </>
      )}
    </div>
  )
}
