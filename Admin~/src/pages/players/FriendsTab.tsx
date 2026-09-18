import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { RefreshCw } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime } from '../../components/ui/format'

type FriendRow = { account_id: string; display_name: string; since: string | null }
type RequestRow = { request_id: string; account_id: string; display_name: string; created_at: string }
type Overview = {
  account_id: string
  display_name: string
  friend_count: number
  max_friends: number
  friends: FriendRow[]
  incoming: RequestRow[]
  outgoing: RequestRow[]
}

export default function FriendsTab({
  target,
  onUnauthenticated,
  accountId,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
}) {
  const navigate = useNavigate()
  const [data, setData] = useState<Overview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setData(await callAdmin<Overview>(target, 'friends.overview', { accountId }))
    } catch (e: unknown) {
      report(e, '친구 관계를 불러오지 못했습니다.')
      setData(null)
    } finally {
      setLoading(false)
    }
  }, [target, accountId, report])

  useEffect(() => { void load() }, [load])

  const goPlayer = (id: string, name: string) => {
    navigate(`/players?account=${encodeURIComponent(id)}&name=${encodeURIComponent(name)}`)
  }

  const friends = data?.friends ?? []
  const incoming = data?.incoming ?? []
  const outgoing = data?.outgoing ?? []

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="flex items-center justify-between">
        <div className="text-sm text-neutral-600">
          친구 {data?.friend_count ?? 0}명
          {data ? <span className="text-neutral-400"> / 상한 {data.max_friends}명</span> : null}
        </div>
        <button
          onClick={() => void load()}
          disabled={loading}
          className="h-9 px-3 inline-flex items-center gap-1.5 rounded-md border border-neutral-200 text-sm text-neutral-700 hover:bg-neutral-50 disabled:opacity-40"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          새로고침
        </button>
      </div>

      <WhiteCard>
        <div className="px-5 py-3 border-b border-neutral-100 text-sm font-medium text-neutral-800">친구 목록</div>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium">닉네임</th>
                <th className="text-left px-4 py-2.5 font-medium w-72">계정 ID</th>
                <th className="text-left px-4 py-2.5 font-medium w-44">친구가 된 시각</th>
              </tr>
            </thead>
            <tbody>
              {loading || friends.length === 0 ? (
                <TableStatusRow loading={loading} empty={friends.length === 0} colSpan={3} emptyText="친구가 없습니다." />
              ) : (
                friends.map((f) => (
                  <tr key={f.account_id} className="border-t border-neutral-100">
                    <td className="px-4 py-3">
                      <button onClick={() => goPlayer(f.account_id, f.display_name)} className="text-[#1677ff] hover:underline">
                        {f.display_name || '(이름 없음)'}
                      </button>
                    </td>
                    <td className="px-4 py-3 text-neutral-500 font-mono text-xs">{f.account_id}</td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(f.since)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <PendingCard title="받은 요청" rows={incoming} loading={loading} onPlayer={goPlayer} />
        <PendingCard title="보낸 요청" rows={outgoing} loading={loading} onPlayer={goPlayer} />
      </div>
    </div>
  )
}

function PendingCard({
  title,
  rows,
  loading,
  onPlayer,
}: {
  title: string
  rows: RequestRow[]
  loading: boolean
  onPlayer: (id: string, name: string) => void
}) {
  return (
    <WhiteCard>
      <div className="px-5 py-3 border-b border-neutral-100 text-sm font-medium text-neutral-800">
        {title} <span className="text-neutral-400 font-normal">{rows.length}</span>
      </div>
      <div className="overflow-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-4 py-2.5 font-medium">닉네임</th>
              <th className="text-left px-4 py-2.5 font-medium w-44">보낸 시각</th>
            </tr>
          </thead>
          <tbody>
            {loading || rows.length === 0 ? (
              <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={2} emptyText="대기 중인 요청이 없습니다." />
            ) : (
              rows.map((r) => (
                <tr key={r.request_id} className="border-t border-neutral-100">
                  <td className="px-4 py-3">
                    <button onClick={() => onPlayer(r.account_id, r.display_name)} className="text-[#1677ff] hover:underline">
                      {r.display_name || '(이름 없음)'}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.created_at)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </WhiteCard>
  )
}
