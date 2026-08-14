import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, ChevronLeft, ChevronRight, Loader2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { formatDateTime } from '../components/ui/format'

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
}
type BatchData = { rows: BatchRow[]; total: number; pageSize: number }
type CategoryRow = { key: string; display_name: string }
type BatchDetail = { batch: BatchRow; stats: { claimed_count: number; deleted_count: number } }

const MODE_LABEL: Record<string, string> = { all: '전체', server: '서버', players: '플레이어' }
const STATUS_OPTIONS = ['전체', '미수령', '수령', '삭제']

function statusOf(r: MailRow): { label: string; cls: string } {
  if (r.deleted_at) return { label: '삭제', cls: 'bg-neutral-200 text-neutral-600' }
  if (r.items_claimed_at) return { label: '수령', cls: 'bg-emerald-100 text-emerald-700' }
  return { label: '미수령', cls: 'bg-[#e8f0fe] text-[#1677ff]' }
}

export default function MailRecords({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const navigate = useNavigate()
  const [tab, setTab] = useState<'batches' | 'records'>('batches')
  const [error, setError] = useState('')

  // 발송 단위(배치) 탭
  const [batchSearchInput, setBatchSearchInput] = useState('')
  const [batchSearch, setBatchSearch] = useState('')
  const [batchCategory, setBatchCategory] = useState('')
  const [batchPage, setBatchPage] = useState(1)
  const [batchData, setBatchData] = useState<BatchData | null>(null)
  const [batchLoading, setBatchLoading] = useState(false)

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

  const [categories, setCategories] = useState<CategoryRow[]>([])

  // 드릴다운: 선택된 배치(개별 우편을 이 배치로 필터)
  const [selectedBatch, setSelectedBatch] = useState<{ id: string; title: string } | null>(null)
  const [detail, setDetail] = useState<BatchDetail | null>(null)

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
        const res = await callAdmin<{ rows: CategoryRow[] }>(target, 'mails.getCategories')
        setCategories(res.rows)
      } catch (e: unknown) {
        report(e, '분류를 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  const loadBatches = useCallback(async () => {
    setBatchLoading(true)
    try {
      const res = await callAdmin<BatchData>(target, 'mails.getBatches', { page: batchPage, search: batchSearch, category: batchCategory })
      setBatchData(res)
    } catch (e: unknown) {
      report(e, '발송 단위를 불러오지 못했습니다.')
      setBatchData(null)
    } finally {
      setBatchLoading(false)
    }
  }, [target, batchPage, batchSearch, batchCategory, report])
  useEffect(() => { if (tab === 'batches') void loadBatches() }, [tab, loadBatches])

  const loadRecords = useCallback(async () => {
    setRecLoading(true)
    try {
      const res = await callAdmin<MailData>(target, 'mails.getRecords', {
        search, status, category, startDate: dateFrom || null, endDate: dateTo || null, batchId: selectedBatch?.id ?? null, page,
      })
      setRecData(res)
    } catch (e: unknown) {
      report(e, '우편 내역을 불러오지 못했습니다.')
      setRecData(null)
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
    void (async () => {
      try {
        setDetail(await callAdmin<BatchDetail>(target, 'mails.getBatchDetail', { batchId: selectedBatch.id }))
      } catch (e: unknown) {
        report(e, '배치 상세를 불러오지 못했습니다.')
      }
    })()
  }, [target, selectedBatch, report])

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
  const clearBatch = () => { setSelectedBatch(null); setPage(1) }

  const submitBatchSearch = () => { setBatchSearch(batchSearchInput); setBatchPage(1) }
  const submitSearch = () => { setSearch(searchInput); setPage(1) }

  const goPlayer = (r: MailRow) => {
    if (!r.account_id) return
    navigate(`/players?account=${encodeURIComponent(r.account_id)}&name=${encodeURIComponent(r.display_name ?? '')}`)
  }

  const inputCls = 'h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white'
  const tabCls = (on: boolean) => ['px-4 py-1.5 rounded-md text-sm transition-colors', on ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'].join(' ')

  const renderCategoryOptions = () => (
    <>
      <option value="">전체 분류</option>
      {!categories.some((c) => c.key === 'default') && <option value="default">기본 (default)</option>}
      {categories.map((c) => (
        <option key={c.key} value={c.key}>{c.display_name ? `${c.display_name} (${c.key})` : c.key}</option>
      ))}
    </>
  )

  return (
    <div className="space-y-4">
      <PageHeader title="우편 내역" description="발송한 우편 내역을 조회합니다." />

      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
        <button type="button" onClick={() => setTab('batches')} className={tabCls(tab === 'batches')}>발송 단위</button>
        <button type="button" onClick={() => setTab('records')} className={tabCls(tab === 'records')}>개별 우편</button>
      </div>

      {tab === 'batches' && (
        <>
          <WhiteCard className="p-4">
            <div className="flex flex-wrap items-center gap-3">
              <select value={batchCategory} onChange={(e) => { setBatchCategory(e.target.value); setBatchPage(1) }} className={inputCls + ' w-40'}>
                {renderCategoryOptions()}
              </select>
              <input value={batchSearchInput} onChange={(e) => setBatchSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitBatchSearch()} placeholder="제목 검색" className="flex-1 min-w-[200px] h-9 px-3 rounded-md border border-neutral-300 text-sm" />
              <button onClick={submitBatchSearch} disabled={batchLoading} className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-60">
                {batchLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
              </button>
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
                    <TableStatusRow loading={batchLoading} empty={batches.length === 0} colSpan={7} emptyText="발송 이력이 없습니다." />
                  ) : (
                    batches.map((b) => (
                      <tr key={b.id} className="border-t border-neutral-100 hover:bg-neutral-50 cursor-pointer" onClick={() => openBatch(b)}>
                        <td className="px-4 py-3 whitespace-nowrap">{MODE_LABEL[b.target_mode] ?? b.target_mode}{b.server_code ? ` (${b.server_code})` : ''}</td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{catLabel(b.category)}</td>
                        <td className="px-4 py-3">{b.title}</td>
                        <td className="px-4 py-3">{b.recipient_count}</td>
                        <td className="px-4 py-3 text-neutral-600">{(b.items ?? []).map((it) => `${it.key}×${it.count}`).join(', ') || '-'}</td>
                        <td className="px-4 py-3 text-neutral-600">{b.created_by ?? '-'}</td>
                        <td className="px-4 py-3 text-neutral-600">{formatDateTime(b.created_at)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
              <span>{batchTotal} result{batchTotal === 1 ? '' : 's'}</span>
              <div className="flex items-center gap-1">
                <button disabled={batchPage <= 1} onClick={() => setBatchPage((p) => Math.max(1, p - 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronLeft className="w-3.5 h-3.5" /></button>
                <span className="px-2">{batchPage} / {batchTotalPages}</span>
                <button disabled={batchPage >= batchTotalPages} onClick={() => setBatchPage((p) => Math.min(batchTotalPages, p + 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronRight className="w-3.5 h-3.5" /></button>
              </div>
            </div>
          </WhiteCard>
        </>
      )}

      {tab === 'records' && (
        <>
          {selectedBatch && (
            <div className="flex items-center gap-2 rounded-md bg-[#e8f0fe] border border-[#1677ff]/20 px-3 py-2 text-sm">
              <span className="text-[#1677ff] font-medium truncate">발송: {selectedBatch.title}</span>
              {detail?.stats && (
                <span className="text-neutral-600 shrink-0">· 대상 {detail.batch?.recipient_count ?? 0}명 · 수령 {detail.stats.claimed_count} · 삭제 {detail.stats.deleted_count}</span>
              )}
              <button onClick={clearBatch} className="ml-auto text-neutral-400 hover:text-neutral-700 shrink-0" title="배치 필터 해제"><X className="w-4 h-4" /></button>
            </div>
          )}

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
              <input value={searchInput} onChange={(e) => setSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitSearch()} placeholder="닉네임 / 계정 ID" className="flex-1 min-w-[200px] h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
              <button onClick={submitSearch} disabled={recLoading} className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60">
                {recLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
              </button>
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
                    <TableStatusRow loading={recLoading} empty={rows.length === 0} colSpan={8} emptyText="우편 내역이 없습니다." />
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
                          <td className="px-4 py-3 text-neutral-600">{(r.items ?? []).map((it) => `${it.key}×${it.count}`).join(', ') || '-'}</td>
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
            <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
              <span>{total} result{total === 1 ? '' : 's'}</span>
              <div className="flex items-center gap-1">
                <button disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronLeft className="w-3.5 h-3.5" /></button>
                <span className="px-2">{page} / {totalPages}</span>
                <button disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronRight className="w-3.5 h-3.5" /></button>
              </div>
            </div>
          </WhiteCard>
        </>
      )}
    </div>
  )
}
