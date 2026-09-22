import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime } from '../components/ui/format'
import { LimitsForm, inputCls, type LimitField } from './social/LimitsForm'

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

const LOBBY_FIELDS: LimitField[] = [
  {
    param: 'maxPendingInvitesReceived', field: 'max_pending_invites_received', min: 1, max: 200,
    label: '받는 쪽 대기 초대 상한',
    hint: '한 사람이 동시에 받아 둘 수 있는 초대의 최대 개수. 넘으면 그 사람에게는 초대가 들어가지 않습니다. 1~200 사이.',
  },
  {
    param: 'inviteCooldownSeconds', field: 'invite_cooldown_seconds', min: 0, max: 300,
    label: '연속 초대 최소 간격',
    hint: '같은 사람이 초대를 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 300까지, 0이면 제한 없음.',
  },
  {
    param: 'lobbyExpireMinutes', field: 'lobby_expire_minutes', min: 1, max: 1440,
    label: '방이 닫히기까지의 시간',
    hint: '아무도 시작하지 않은 방이 저절로 닫히기까지의 시간. 분 단위로 최대 1440까지.',
  },
]

/**
 * 게임이 담아 둔 자유 칸. 키 이름도 값도 게임마다 달라서 콘솔은 해석하지 않고 그대로 보여준다.
 * 비어 있으면 아무것도 그리지 않는다 — "없음"을 줄마다 찍으면 목록이 지저분해진다.
 */
function MetaText({ meta, label }: { meta: Meta | null; label?: string }) {
  const entries = Object.entries(meta ?? {})
  if (entries.length === 0) return null
  return (
    <div className="text-xs text-neutral-400 whitespace-nowrap">
      {label ? `${label} · ` : ''}
      {entries.map(([k, v]) => `${k}=${typeof v === 'object' ? JSON.stringify(v) : String(v)}`).join(', ')}
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
  const [tab, setTab] = useState<'list' | 'settings'>('list')
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const tabCls = (active: boolean) =>
    ['h-8 px-3 rounded-md text-sm transition-colors', active ? 'bg-white shadow-sm text-neutral-900' : 'text-neutral-600 hover:text-neutral-900'].join(' ')

  return (
    <div className="space-y-4">
      <PageHeader title="매치 로비" description="플레이어가 모인 대기방의 현황을 확인하고 초대·만료 기준을 조정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
        <button className={tabCls(tab === 'list')} onClick={() => setTab('list')}>로비 목록</button>
        <button className={tabCls(tab === 'settings')} onClick={() => setTab('settings')}>로비 설정</button>
      </div>

      {tab === 'list'
        ? <LobbyList target={target} report={report} />
        : (
          <LimitsForm
            target={target}
            report={report}
            title="로비 설정"
            getAction="matchLobby.settingsGet"
            updateAction="matchLobby.settingsUpdate"
            fields={LOBBY_FIELDS}
          />
        )}
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
  const navigate = useNavigate()
  const [data, setData] = useState<LobbyData | null>(null)
  const [loading, setLoading] = useState(false)
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setData(await callAdmin<LobbyData>(target, 'matchLobby.list', { status, page }))
    } catch (e: unknown) {
      report(e, '로비 목록을 불러오지 못했습니다.')
      setData(null)
    } finally {
      setLoading(false)
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
        <div className="flex items-center gap-2">
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
          <button
            onClick={() => void load()}
            disabled={loading}
            className="h-9 px-3 inline-flex items-center gap-1.5 rounded-md border border-neutral-200 text-sm text-neutral-700 hover:bg-neutral-50 disabled:opacity-40"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            새로고침
          </button>
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">만든 시각</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">방</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">게임 코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">인원</th>
                <th className="text-left px-4 py-2.5 font-medium">참가자</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={6} emptyText="로비가 없습니다." />
              ) : (
                rows.map((l) => {
                  const active = l.members.filter((m) => m.status === 'invited' || m.status === 'accepted').length
                  const expanded = openId === l.lobby_id
                  return (
                    <tr key={l.lobby_id} className="border-t border-neutral-100 align-top">
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(l.created_at)}</td>
                      <td className="px-4 py-3 text-neutral-700">{l.name || <span className="text-neutral-400">이름 없음</span>}</td>
                      <td className="px-4 py-3 text-neutral-700">{l.game_code}</td>
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{LOBBY_STATUS_LABEL[l.status] ?? l.status}</td>
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{active} / {l.max_members}</td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => setOpenId(expanded ? null : l.lobby_id)}
                          className="text-[#1677ff] hover:underline text-xs whitespace-nowrap"
                        >
                          {expanded ? '접기' : `참가자 ${l.members.length}명 보기`}
                        </button>
                        {expanded && (
                          <div className="mt-2 space-y-1">
                            {l.members.map((m) => (
                              <div key={m.account_id} className="flex items-center gap-2 text-xs">
                                <button
                                  onClick={() => navigate(`/players?account=${encodeURIComponent(m.account_id)}&name=${encodeURIComponent(m.display_name)}`)}
                                  className="text-[#1677ff] hover:underline"
                                >
                                  {m.display_name || m.account_id.slice(0, 8)}
                                </button>
                                {m.account_id === l.host_account_id && <span className="text-neutral-400">방장</span>}
                                <span className="text-neutral-500">{MEMBER_STATUS_LABEL[m.status] ?? m.status}</span>
                                <MetaText meta={m.metadata} />
                              </div>
                            ))}
                            <MetaText meta={l.metadata} label="방 정보" />
                          </div>
                        )}
                      </td>
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
            <button disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronLeft className="w-3.5 h-3.5" /></button>
            <span className="px-2">{page} / {totalPages}</span>
            <button disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronRight className="w-3.5 h-3.5" /></button>
          </div>
        </div>
      </WhiteCard>
    </>
  )
}
