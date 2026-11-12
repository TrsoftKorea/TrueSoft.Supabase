import { useCallback, useEffect, useRef, useState } from 'react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { TableFooter } from '../components/ui/TableFooter'
import { PageHeader } from '../components/ui/PageHeader'
import { TableCard, type Column } from '../components/ui/DataTable'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime, formatCount, formatTotal } from '../components/ui/format'
import { inputCls } from '../components/ui/Input'
import { FilterBar, FilterField, RefreshButton } from '../components/ui/FilterBar'
import { PlayerLink } from '../components/ui/PlayerLink'
import { Pairs } from '../components/ui/Pairs'

type Meta = Record<string, unknown>
type LobbyMember = {
  account_id: string; display_name: string; status: string; metadata: Meta | null
  invited_at: string; responded_at: string | null
}
type LobbyRow = {
  lobby_id: string; game_code: string; name: string | null; metadata: Meta | null
  host_account_id: string; status: string
  max_members: number; created_at: string; started_at: string | null; ended_at: string | null
  expires_at: string; members: LobbyMember[]
}
type LobbyData = { rows: LobbyRow[]; total: number; pageSize: number }

const LOBBY_STATUS_LABEL: Record<string, string> = {
  open: '모집 중', started: '시작됨', cancelled: '취소됨', expired: '만료됨',
}
const MEMBER_STATUS_LABEL: Record<string, string> = {
  invited: '초대됨', accepted: '수락', declined: '거절', left: '나감',
}

/**
 * 게임이 담아 둔 자유 칸. 키 이름도 값도 게임마다 달라서 콘솔은 해석하지 않고 그대로 보여준다.
 * 비어 있으면 아무것도 그리지 않는다 — "없음"을 줄마다 찍으면 목록이 지저분해진다.
 */
function MetaText({ meta, label }: { meta: Meta | null; label?: string }) {
  const entries = Object.entries(meta ?? {})
  if (entries.length === 0) return null
  return (
    <div className="text-xs whitespace-nowrap">
      {label && <span className="mr-2 text-neutral-400">{label}</span>}
      <Pairs items={entries.map(([k, v]) => ({ label: k, value: typeof v === 'object' ? JSON.stringify(v) : String(v) }))} />
    </div>
  )
}

export default function Lobbies({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="space-y-4">
      <PageHeader title="매치 로비" description="플레이어가 모인 대기방의 현황을 확인합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <LobbyList target={target} report={report} />
    </div>
  )
}

function LobbyList({
  target,
  report,
}: {
  target: ProjectTarget
  report: (e: unknown, fallback: string) => void
}) {
  const [data, setData] = useState<LobbyData | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)

  // 쪽을 빠르게 넘기면 늦게 온 앞 쪽 응답이 지금 쪽을 덮는다. 마지막 요청만 반영한다.
  const loadReq = useRef(0)
  const load = useCallback(async () => {
    const req = ++loadReq.current
    setLoading(true)
    try {
      const res = await callAdmin<LobbyData>(target, 'matchLobby.list', { status, page })
      if (req !== loadReq.current) return
      setData(res)
      setFailed(false)
    } catch (e: unknown) {
      if (req !== loadReq.current) return
      report(e, '로비 목록을 불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      if (req === loadReq.current) setLoading(false)
    }
  }, [target, status, page, report])

  useEffect(() => { void load() }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  return (
    <>
      <WhiteCard className="p-4">
        <FilterBar>
          <FilterField label="상태">
            <select
              className={inputCls}
              value={status}
              onChange={(e) => { setStatus(e.target.value); setPage(1) }}
            >
              <option value="">모든 상태</option>
              <option value="open">모집 중</option>
              <option value="started">시작됨</option>
              <option value="cancelled">취소됨</option>
              <option value="expired">만료됨</option>
            </select>
          </FilterField>
          <RefreshButton className="ml-auto" onClick={() => void load()} loading={loading} />
        </FilterBar>
      </WhiteCard>

      <TableCard
        wide
        columns={[
          { key: 'created', header: '만든 시각', kind: 'datetime', cell: (l) => formatDateTime(l.created_at) },
          { key: 'name', header: '방', kind: 'name', cell: (l) => l.name },
          { key: 'game', header: '게임 코드', kind: 'mono', cell: (l) => l.game_code },
          { key: 'status', header: '상태', kind: 'status', cell: (l) => LOBBY_STATUS_LABEL[l.status] ?? l.status },
          {
            key: 'capacity', header: '참가·초대 / 최대', kind: 'number',
            cell: (l) => `${l.members.filter((m) => m.status === 'invited' || m.status === 'accepted').length} / ${l.max_members}`,
          },
          { key: 'members', header: '참가자', kind: 'number', cell: (l) => `${formatCount(l.members.length)}명` },
        ] satisfies Column<LobbyRow>[]}
        rows={rows}
        rowKey={(l) => l.lobby_id}
        loading={loading}
        failed={failed}
        emptyText="로비가 없습니다."
        expand={(l) => (l.members.length === 0 && Object.keys(l.metadata ?? {}).length === 0 ? null : (
          <div className="space-y-1">
            {l.members.map((m) => (
              <div key={m.account_id} className="flex items-center gap-2 text-xs">
                <PlayerLink accountId={m.account_id} name={m.display_name} />
                {m.account_id === l.host_account_id && <span className="text-neutral-400">방장</span>}
                <span className="text-neutral-500">{MEMBER_STATUS_LABEL[m.status] ?? m.status}</span>
                <MetaText meta={m.metadata} />
              </div>
            ))}
            <MetaText meta={l.metadata} label="방 정보" />
          </div>
        ))}
        footer={<TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />}
      />
    </>
  )
}
