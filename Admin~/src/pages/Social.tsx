import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight, RefreshCw, Save } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime } from '../components/ui/format'

type LobbyMember = {
  account_id: string; display_name: string; status: string; role_tag: string | null
  invited_at: string; responded_at: string | null
}
type LobbyRow = {
  lobby_id: string; game_code: string; host_account_id: string; status: string
  max_members: number; created_at: string; started_at: string | null; ended_at: string | null
  expires_at: string; members: LobbyMember[]
}
type LobbyData = { rows: LobbyRow[]; total: number; pageSize: number }
type FriendSettings = {
  max_friends: number; max_pending_sent: number; request_cooldown_seconds: number; updated_at: string
}

const LOBBY_STATUS_LABEL: Record<string, string> = {
  open: '모집 중', started: '시작됨', cancelled: '취소됨', expired: '만료됨',
}
const MEMBER_STATUS_LABEL: Record<string, string> = {
  invited: '초대됨', accepted: '수락', declined: '거절', left: '나감',
}

const inputCls = 'h-9 px-3 rounded-md border border-neutral-200 text-sm outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'

export default function Social({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [tab, setTab] = useState<'lobbies' | 'settings'>('lobbies')
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const tabCls = (active: boolean) =>
    ['h-8 px-3 rounded-md text-sm transition-colors', active ? 'bg-white shadow-sm text-neutral-900' : 'text-neutral-600 hover:text-neutral-900'].join(' ')

  return (
    <div className="space-y-4">
      <PageHeader title="친구·로비" description="매치 로비 현황을 확인하고 친구 기능 제한값을 조정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
        <button className={tabCls(tab === 'lobbies')} onClick={() => setTab('lobbies')}>매치 로비</button>
        <button className={tabCls(tab === 'settings')} onClick={() => setTab('settings')}>친구 설정</button>
      </div>

      {tab === 'lobbies'
        ? <LobbyList target={target} report={report} />
        : <FriendSettingsForm target={target} report={report} />}
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
                <th className="text-left px-4 py-2.5 font-medium w-32">게임 코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">인원</th>
                <th className="text-left px-4 py-2.5 font-medium">참가자</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={5} emptyText="로비가 없습니다." />
              ) : (
                rows.map((l) => {
                  const active = l.members.filter((m) => m.status === 'invited' || m.status === 'accepted').length
                  const expanded = openId === l.lobby_id
                  return (
                    <tr key={l.lobby_id} className="border-t border-neutral-100 align-top">
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(l.created_at)}</td>
                      <td className="px-4 py-3 text-neutral-700">{l.game_code}</td>
                      <td className="px-4 py-3 text-neutral-600">{LOBBY_STATUS_LABEL[l.status] ?? l.status}</td>
                      <td className="px-4 py-3 text-neutral-600">{active} / {l.max_members}</td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => setOpenId(expanded ? null : l.lobby_id)}
                          className="text-[#1677ff] hover:underline text-xs"
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
                                {m.role_tag && <span className="text-neutral-400">역할 {m.role_tag}</span>}
                              </div>
                            ))}
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

function FriendSettingsForm({
  target,
  report,
}: {
  target: ProjectTarget
  report: (e: unknown, fallback: string) => void
}) {
  const [maxFriends, setMaxFriends] = useState('')
  const [maxPendingSent, setMaxPendingSent] = useState('')
  const [cooldown, setCooldown] = useState('')
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)

  const apply = (s: FriendSettings) => {
    setMaxFriends(String(s.max_friends))
    setMaxPendingSent(String(s.max_pending_sent))
    setCooldown(String(s.request_cooldown_seconds))
    setUpdatedAt(s.updated_at ?? null)
  }

  const load = useCallback(async () => {
    setLoading(true)
    try {
      apply(await callAdmin<FriendSettings>(target, 'friends.settingsGet'))
    } catch (e: unknown) {
      report(e, '설정을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const save = async () => {
    setSaving(true)
    setSaved(false)
    try {
      apply(await callAdmin<FriendSettings>(target, 'friends.settingsUpdate', {
        maxFriends: Number(maxFriends),
        maxPendingSent: Number(maxPendingSent),
        requestCooldownSeconds: Number(cooldown),
      }))
      setSaved(true)
    } catch (e: unknown) {
      report(e, '설정을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-4 max-w-xl">
      <Field
        label="친구 수 상한"
        hint="한 계정이 가질 수 있는 친구의 최대 수. 상한에 닿으면 요청도 수락도 막힙니다."
        value={maxFriends}
        onChange={setMaxFriends}
        disabled={loading}
      />
      <Field
        label="보낸 요청 대기 상한"
        hint="아직 상대가 응답하지 않은 요청의 최대 개수. 무차별 요청을 막습니다."
        value={maxPendingSent}
        onChange={setMaxPendingSent}
        disabled={loading}
      />
      <Field
        label="연속 요청 최소 간격"
        hint="같은 사람이 요청을 연달아 보낼 때 기다려야 하는 시간(초). 0이면 제한 없음."
        value={cooldown}
        onChange={setCooldown}
        disabled={loading}
      />

      <div className="flex items-center gap-3 pt-1">
        <button
          onClick={() => void save()}
          disabled={saving || loading}
          className="h-9 px-4 inline-flex items-center gap-1.5 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-40"
        >
          <Save className="w-3.5 h-3.5" />
          저장
        </button>
        {saved && <span className="text-sm text-green-600">저장했습니다.</span>}
        {updatedAt && <span className="text-xs text-neutral-400">마지막 수정 {formatDateTime(updatedAt)}</span>}
      </div>
    </WhiteCard>
  )
}

function Field({
  label,
  hint,
  value,
  onChange,
  disabled,
}: {
  label: string
  hint: string
  value: string
  onChange: (v: string) => void
  disabled: boolean
}) {
  return (
    <div className="space-y-1">
      <label className="block text-sm font-medium text-neutral-800">{label}</label>
      <input
        type="number"
        min={0}
        className={`${inputCls} w-40`}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
      />
      <p className="text-xs text-neutral-500">{hint}</p>
    </div>
  )
}
