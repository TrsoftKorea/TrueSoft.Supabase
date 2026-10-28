import { useEffect, useState, useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Search, Copy, Check, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { inputCls } from '../../components/ui/Input'
import { TableFooter } from '../../components/ui/TableFooter'
import { formatDateTime, formatKRW, formatTotal } from '../../components/ui/format'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { PageHeader } from '../../components/ui/PageHeader'
import { Button } from '../../components/ui/Button'
import { Tabs } from '../../components/ui/Tabs'

type PlayerRow = {
  account_id: string
  display_name: string
  is_guest: boolean | null
  platform: string | null
  country_code: string | null
  total_paid_krw: number | string | null
  created_at: string | null
  last_activity_at: string | null
}

type ListResult = { rows: PlayerRow[]; total: number; pageSize: number }
type Tab = 'all' | 'banned'

function legacyCopy(text: string): boolean {
  const el = document.createElement('input')
  el.value = text
  el.style.cssText = 'position:fixed;top:0;left:0;width:1px;height:1px;opacity:0;'
  document.body.appendChild(el)
  el.focus()
  el.select()
  el.setSelectionRange(0, text.length)
  const ok = document.execCommand('copy')
  document.body.removeChild(el)
  return ok
}

async function copyToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // fall through to legacy
    }
  }
  return legacyCopy(text)
}

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
  const [copiedId, setCopiedId] = useState<string | null>(null)
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

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    setFailed(false)
    try {
      setData(
        await callAdmin<ListResult>(target, 'players.list', {
          search,
          page,
          bannedOnly: tab === 'banned',
        }),
      )
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, search, page, tab, report])

  useEffect(() => {
    void load()
  }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const submitSearch = () => setListState({ search: input, page: 1 })

  const resetSearch = () => {
    setInput('')
    setListState({ search: '', page: 1 })
  }

  const copyId = async (e: React.MouseEvent, id: string) => {
    e.stopPropagation()
    const ok = await copyToClipboard(id)
    if (ok) {
      setCopiedId(id)
      setTimeout(() => setCopiedId(null), 1500)
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="플레이어" description="플레이어를 찾아 계정 정보를 봅니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <Tabs
        value={tab}
        onChange={(k) => setListState({ tab: k, page: 1 })}
        tabs={[['all', '전체 플레이어'], ['banned', '차단 플레이어']]}
      />

      <WhiteCard className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-neutral-500">닉네임 검색</span>
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') submitSearch()
            }}
            placeholder="닉네임"
            className={inputCls + ' flex-1 min-w-[260px]'}
          />
          <Button variant="primary" size="compact"
            onClick={submitSearch}
            disabled={loading}
          >
            {loading ? (
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Search className="w-3.5 h-3.5" />
            )}
            검색
          </Button>
          <Button variant="outline" size="compact"
            onClick={resetSearch}
          >
            초기화
          </Button>
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium">ID</th>
                <th className="text-left px-4 py-2.5 font-medium">닉네임</th>
                <th className="text-center px-4 py-2.5 font-medium w-16">게스트</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">플랫폼</th>
                <th className="text-left px-4 py-2.5 font-medium w-16">국가</th>
                <th className="text-right px-4 py-2.5 font-medium w-32">총 결제금액</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">생성일자</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">최근 활동시간</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={8} emptyText="결과가 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr
                    key={r.account_id ?? Math.random()}
                    className="border-t border-neutral-100 hover:bg-neutral-50/50"
                  >
                    <td className="px-4 py-3 font-mono text-xs text-neutral-600">
                      {r.account_id ? (
                        <div className="flex items-center gap-1.5">
                          <span className="truncate max-w-[140px]">{r.account_id}</span>
                          <button
                            onClick={(e) => copyId(e, r.account_id)}
                            className={[
                              'flex-shrink-0 transition-colors',
                              copiedId === r.account_id
                                ? 'text-emerald-500'
                                : 'text-neutral-400 hover:text-neutral-700',
                            ].join(' ')}
                            aria-label="ID 복사"
                            title="ID 복사"
                          >
                            {copiedId === r.account_id ? (
                              <Check className="w-3 h-3" />
                            ) : (
                              <Copy className="w-3 h-3" />
                            )}
                          </button>
                        </div>
                      ) : (
                        <span className="text-neutral-400">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {r.account_id ? (
                        <button
                          onClick={() => onSelect(r.account_id, r.display_name)}
                          className="text-[#1677ff] hover:underline"
                        >
                          {r.display_name}
                        </button>
                      ) : (
                        <span className="text-neutral-400">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-center">
                      {r.is_guest ? <Check className="w-4 h-4 inline text-emerald-600" /> : <span className="text-neutral-300">-</span>}
                    </td>
                    <td className="px-4 py-3 text-neutral-700">{r.platform ?? '-'}</td>
                    <td className="px-4 py-3 text-neutral-700">{r.country_code ?? '-'}</td>
                    <td className="px-4 py-3 text-right text-neutral-800">
                      {formatKRW(r.total_paid_krw)}
                    </td>
                    <td className="px-4 py-3 text-neutral-600">{formatDateTime(r.created_at)}</td>
                    <td className="px-4 py-3 text-neutral-600">
                      {formatDateTime(r.last_activity_at)}
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
