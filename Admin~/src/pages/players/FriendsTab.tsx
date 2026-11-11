import { useEffect, useState, useCallback } from 'react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { InfoList } from '../../components/ui/InfoList'
import { CardStack, TableCard, type Column } from '../../components/ui/DataTable'
import { AccountIdCell, PlayerLink } from '../../components/ui/PlayerLink'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime } from '../../components/ui/format'
import { RefreshButton } from '../../components/ui/FilterBar'

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
  const [data, setData] = useState<Overview | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setData(await callAdmin<Overview>(target, 'friends.overview', { accountId }))
    } catch (e: unknown) {
      report(e, '친구 관계를 불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, accountId, report])

  useEffect(() => { void load() }, [load])

  const friends = data?.friends ?? []
  const incoming = data?.incoming ?? []
  const outgoing = data?.outgoing ?? []

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="flex items-center justify-between">
        <InfoList className="flex-1" items={[
          { label: '친구', value: data ? `${data.friend_count}명` : failed ? '불러오지 못함' : '…' },
          { label: '상한', value: data ? `${data.max_friends}명` : failed ? '불러오지 못함' : '…' },
        ]} />
        <RefreshButton onClick={() => void load()} loading={loading} />
      </div>

      <CardStack>
        <TableCard
          title="친구 목록"
          columns={FRIEND_COLUMNS}
          rows={friends}
          rowKey={(f) => f.account_id}
          loading={loading}
          failed={failed}
          emptyText="친구가 없습니다."
        />
        <TableCard
          title={<>받은 요청 <span className="text-neutral-400 font-normal">{incoming.length}</span></>}
          columns={REQUEST_COLUMNS}
          rows={incoming}
          rowKey={(r) => r.request_id}
          loading={loading}
          failed={failed}
          emptyText="대기 중인 요청이 없습니다."
        />
        <TableCard
          title={<>보낸 요청 <span className="text-neutral-400 font-normal">{outgoing.length}</span></>}
          columns={REQUEST_COLUMNS}
          rows={outgoing}
          rowKey={(r) => r.request_id}
          loading={loading}
          failed={failed}
          emptyText="대기 중인 요청이 없습니다."
        />
      </CardStack>
    </div>
  )
}

const FRIEND_COLUMNS: Column<FriendRow>[] = [
  { key: 'name', header: '닉네임', kind: 'name', cell: (f) => <PlayerLink accountId={f.account_id} name={f.display_name} /> },
  { key: 'id', header: '계정 ID', kind: 'name', cell: (f) => <AccountIdCell accountId={f.account_id} /> },
  { key: 'since', header: '친구가 된 시각', kind: 'datetime', cell: (f) => formatDateTime(f.since) },
]

const REQUEST_COLUMNS: Column<RequestRow>[] = [
  { key: 'name', header: '닉네임', kind: 'name', cell: (r) => <PlayerLink accountId={r.account_id} name={r.display_name} /> },
  { key: 'at', header: '보낸 시각', kind: 'datetime', cell: (r) => formatDateTime(r.created_at) },
]
