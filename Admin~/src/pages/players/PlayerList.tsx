import { useEffect, useRef, useState, useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Check } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { platformLabel } from '../../lib/platforms'
import { WhiteCard } from '../../components/ui/Card'
import { inputCls } from '../../components/ui/Input'
import { TableFooter } from '../../components/ui/TableFooter'
import { formatDateTime, formatKRW, formatTotal } from '../../components/ui/format'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { TableCard, type Column } from '../../components/ui/DataTable'
import { PageHeader } from '../../components/ui/PageHeader'
import { FilterBar, FilterField, RefreshButton, SearchActions } from '../../components/ui/FilterBar'
import { AccountIdCell } from '../../components/ui/PlayerLink'
import { Tabs } from '../../components/ui/Tabs'

type PlayerRow = {
  account_id: string
  display_name: string | null
  is_guest: boolean | null
  platform: string | null
  country_code: string | null
  total_paid_krw: number | string | null
  created_at: string | null
  last_activity_at: string | null
}

type ListResult = { rows: PlayerRow[]; total: number; pageSize: number }
type Tab = 'all' | 'banned'

export default function PlayerList({
  target,
  onUnauthenticated,
  onSelect,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  onSelect: (accountId: string, displayName: string) => void
}) {
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'banned' ? 'banned' : 'all'
  const search = params.get('q') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [input, setInput] = useState(search)

  const setListState = (next: { tab?: Tab; search?: string; page?: number }) =>
    setParams(
      (p) => {
        const t = next.tab ?? tab
        const q = next.search ?? search
        const pg = next.page ?? page
        if (t === 'banned') p.set('tab', t)
        else p.delete('tab')
        if (q) p.set('q', q)
        else p.delete('q')
        if (pg > 1) p.set('page', String(pg))
        else p.delete('page')
        return p
      },
      { replace: true },
    )
  const setPage = (pg: number) => setListState({ page: pg })
  const [data, setData] = useState<ListResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)

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

  // 쪽을 빠르게 넘기면 늦게 온 앞 쪽 응답이 지금 쪽을 덮는다. 마지막 요청만 반영한다.
  const loadReq = useRef(0)
  const load = useCallback(async () => {
    const req = ++loadReq.current
    setLoading(true)
    setError('')
    setFailed(false)
    try {
      const res = await callAdmin<ListResult>(target, 'players.list', {
        search,
        page,
        bannedOnly: tab === 'banned',
      })
      if (req === loadReq.current) setData(res)
    } catch (e: unknown) {
      if (req !== loadReq.current) return
      report(e, '불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      if (req === loadReq.current) setLoading(false)
    }
  }, [target, search, page, tab, report])

  useEffect(() => {
    void load()
  }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  // 주소로 끝 쪽을 넘겨 들어오면(저장한 링크 등) 빈 표 대신 마지막 쪽을 보인다.
  useEffect(() => {
    if (data && page > totalPages) setPage(totalPages)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data])

  // 플레이어 메뉴의 표는 같은 순서다 — 정렬 기준 시각 · 플레이어 · 계정 ID · 나머지(이 목록은 마지막 활동 순).
  const columns: Column<PlayerRow>[] = [
    { key: 'active', header: '마지막 활동', kind: 'datetime', cell: (r) => r.last_activity_at && formatDateTime(r.last_activity_at) },
    {
      key: 'name', header: '플레이어', kind: 'name', cell: (r) => r.account_id && (
        <button onClick={() => onSelect(r.account_id, r.display_name ?? '')} className="text-[#1677ff] hover:underline">
          {r.display_name || <span className="font-mono text-xs">{r.account_id.slice(0, 8)}</span>}
        </button>
      ),
    },
    { key: 'id', header: '계정 ID', kind: 'name', cell: (r) => r.account_id && <AccountIdCell accountId={r.account_id} /> },
    { key: 'guest', header: '게스트', kind: 'status', cell: (r) => r.is_guest && <Check className="w-4 h-4 text-emerald-600" aria-label="게스트" /> },
    { key: 'platform', header: '플랫폼', kind: 'name', cell: (r) => platformLabel(r.platform) },
    { key: 'country', header: '국가', kind: 'name', cell: (r) => r.country_code },
    { key: 'paid', header: '총 결제금액', kind: 'number', cell: (r) => formatKRW(r.total_paid_krw) },
    { key: 'created', header: '가입 시각', kind: 'datetime', cell: (r) => r.created_at && formatDateTime(r.created_at) },
  ]

  const submitSearch = () => setListState({ search: input, page: 1 })

  const resetSearch = () => {
    setInput('')
    setListState({ search: '', page: 1 })
  }

  return (
    <div className="space-y-4">
      <PageHeader title="플레이어 목록" description="플레이어를 찾아 계정 정보를 봅니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <Tabs
        value={tab}
        onChange={(k) => setListState({ tab: k, page: 1 })}
        tabs={[['all', '전체 플레이어'], ['banned', '차단 플레이어']]}
      />

      <WhiteCard className="p-4">
        <FilterBar>
          <FilterField label="플레이어" className="flex-1 min-w-[260px]">
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') submitSearch()
              }}
              placeholder="닉네임 / 계정 ID"
              className={inputCls + ' w-full'}
            />
          </FilterField>
          <SearchActions onSearch={submitSearch} onReset={resetSearch} loading={loading} />
          <RefreshButton className="ml-auto" onClick={() => void load()} loading={loading} />
        </FilterBar>
      </WhiteCard>

      <TableCard
        wide
        columns={columns}
        rows={rows}
        rowKey={(r, i) => r.account_id ?? `row-${i}`}
        loading={loading}
        failed={failed}
        emptyText="결과가 없습니다."
        footer={<TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />}
      />
    </div>
  )
}
