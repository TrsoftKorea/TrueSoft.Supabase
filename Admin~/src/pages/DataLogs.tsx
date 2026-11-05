import { useEffect, useRef, useState, useCallback } from 'react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { inputCls } from '../components/ui/Input'
import { TableFooter } from '../components/ui/TableFooter'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime, diffSummary, formatTotal } from '../components/ui/format'
import { Button } from '../components/ui/Button'
import { FilterBar, FilterField, RefreshButton, SearchActions } from '../components/ui/FilterBar'
import { PlayerFilterChip, PlayerLink, playerLabel, useFilterPlayer, useOnChange } from '../components/ui/PlayerLink'
import { RestoreSaveModal } from './players/RestoreSaveModal'

type LogRow = { id: number; account_id: string; display_name: string | null; diff: Record<string, unknown>; source: string | null; created_at: string }
type LogData = { rows: LogRow[]; total: number; pageSize: number; nameSearchTruncated?: boolean }

export default function DataLogs({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const filter = useFilterPlayer()
  const [playerInput, setPlayerInput] = useState('')
  const [player, setPlayer] = useState('')
  const [sourceInput, setSourceInput] = useState('')
  const [source, setSource] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [page, setPage] = useState(1)
  useOnChange(filter.accountId, () => setPage(1))
  const [data, setData] = useState<LogData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)
  const [restoreRow, setRestoreRow] = useState<LogRow | null>(null)
  const [notice, setNotice] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  // 조건을 빠르게 바꾸면 늦게 온 옛 응답이 새 목록을 덮는다 — 마지막 요청만 반영한다.
  const loadReq = useRef(0)
  const load = useCallback(async () => {
    const req = ++loadReq.current
    setLoading(true)
    setFailed(false)
    try {
      const res = await callAdmin<LogData>(target, 'dataLogs.list', {
        accountId: filter.accountId || null, search: filter.accountId ? '' : player, source, startDate: dateFrom || null, endDate: dateTo || null, page,
      })
      if (req === loadReq.current) setData(res)
    } catch (e: unknown) {
      if (req !== loadReq.current) return
      report(e, '데이터 로그를 불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      if (req === loadReq.current) setLoading(false)
    }
  }, [target, filter.accountId, player, source, dateFrom, dateTo, page, report])

  useEffect(() => { void load() }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 30
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const submitSearch = () => { setPlayer(playerInput.trim()); setSource(sourceInput.trim()); setPage(1) }
  const resetSearch = () => {
    setPlayerInput(''); setPlayer(''); setSourceInput(''); setSource(''); setDateFrom(''); setDateTo(''); setPage(1)
    filter.clear()
  }
  const clearPlayer = () => { filter.clear(); setPage(1) }

  return (
    <div className="space-y-4">
      <PageHeader title="데이터 로그" description="플레이어 세이브 데이터가 언제 어떻게 바뀌었는지 계정을 가로질러 조회합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {notice && <div className="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">{notice}</div>}

      {restoreRow && (
        <RestoreSaveModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={restoreRow.account_id}
          logId={restoreRow.id}
          loggedAt={restoreRow.created_at}
          playerName={playerLabel(restoreRow.display_name, restoreRow.account_id)}
          onClose={() => setRestoreRow(null)}
          onRestored={() => {
            setNotice(`${playerLabel(restoreRow.display_name, restoreRow.account_id)}의 데이터를 ${formatDateTime(restoreRow.created_at)} 변경 전으로 되돌렸습니다.`)
            setRestoreRow(null)
            void load()
          }}
        />
      )}

      <WhiteCard className="p-4">
        <FilterBar>
          <FilterField label="기간">
            <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
          </FilterField>
          {filter.accountId ? (
            <FilterField label="플레이어">
              <PlayerFilterChip accountId={filter.accountId} name={filter.name} onClear={clearPlayer} />
            </FilterField>
          ) : (
            <FilterField label="플레이어" className="flex-1 min-w-[200px]">
              <input
                value={playerInput}
                onChange={(e) => setPlayerInput(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
                placeholder="닉네임 / 계정 ID"
                className={inputCls + ' w-full'}
              />
            </FilterField>
          )}
          <FilterField label="출처">
            <input
              value={sourceInput}
              onChange={(e) => setSourceInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
              className={inputCls + ' w-48'}
            />
          </FilterField>
          <SearchActions onSearch={submitSearch} onReset={resetSearch} loading={loading} />
          <RefreshButton className="ml-auto" onClick={() => void load()} loading={loading} />
        </FilterBar>
        {data?.nameSearchTruncated && (
          <p className="mt-2 text-xs text-neutral-500">닉네임이 맞는 플레이어가 많아 앞 100명의 기록만 찾았습니다.</p>
        )}
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">시각</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">플레이어</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">출처</th>
                <th className="text-left px-4 py-2.5 font-medium">바뀌기 전 값</th>
                <th className="w-28" />
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={5} emptyText="데이터 로그가 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50/50">
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.created_at)}</td>
                    <td className="px-4 py-3">
                      <PlayerLink accountId={r.account_id} name={r.display_name} />
                    </td>
                    <td className="px-4 py-3 text-neutral-600">{r.source ?? '-'}</td>
                    <td className="px-4 py-3 text-neutral-700 max-w-[480px] truncate" title={diffSummary(r.diff)}>
                      {diffSummary(r.diff)}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Button variant="outline" size="sm" onClick={() => setRestoreRow(r)}>이 변경 전으로</Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />
      </WhiteCard>
    </div>
  )
}
