import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight, RefreshCw, Save } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
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
  max_friends: number; max_pending_sent: number; request_cooldown_seconds: number
  updated_at: string; updated_by: string | null
}

// 서버(ts_admin_friend_settings_update)가 막는 범위와 같게 둔다. 서버만 막으면 운영자가
// 저장을 누른 뒤에야 영문 오류를 보게 되고, 화면만 막으면 검증이 없는 것과 같다.
const LIMITS = {
  maxFriends: { min: 1, max: 1000 },
  maxPendingSent: { min: 1, max: 500 },
  cooldown: { min: 0, max: 300 },
} as const

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
  const [current, setCurrent] = useState<FriendSettings | null>(null)
  const [maxFriends, setMaxFriends] = useState('')
  const [maxPendingSent, setMaxPendingSent] = useState('')
  const [cooldown, setCooldown] = useState('')
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [confirming, setConfirming] = useState(false)

  const apply = (s: FriendSettings) => {
    setCurrent(s)
    setMaxFriends(String(s.max_friends))
    setMaxPendingSent(String(s.max_pending_sent))
    setCooldown(String(s.request_cooldown_seconds))
  }

  const load = useCallback(async () => {
    setLoading(true)
    try {
      apply(await callAdmin<FriendSettings>(target, 'friends.settingsGet'))
    } catch (e: unknown) {
      setCurrent(null)
      report(e, '설정을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  // 빈 칸은 Number('') === 0 이라 그냥 두면 0 이 저장된다. 간격은 0 이 "제한 없음"이라
  // DB CHECK 도 안 걸려, 칸을 지운 실수가 도배 방지를 끄는 것으로 이어진다.
  const parsed = {
    maxFriends: parseField(maxFriends, LIMITS.maxFriends),
    maxPendingSent: parseField(maxPendingSent, LIMITS.maxPendingSent),
    cooldown: parseField(cooldown, LIMITS.cooldown),
  }
  const invalid = Object.values(parsed).some((v) => v === null)
  const changed = !!current && (
    parsed.maxFriends !== current.max_friends
    || parsed.maxPendingSent !== current.max_pending_sent
    || parsed.cooldown !== current.request_cooldown_seconds
  )

  const edit = (set: (v: string) => void) => (v: string) => { set(v); setSaved(false) }

  const save = async () => {
    if (invalid) return
    setSaving(true)
    setSaved(false)
    try {
      apply(await callAdmin<FriendSettings>(target, 'friends.settingsUpdate', {
        maxFriends: parsed.maxFriends,
        maxPendingSent: parsed.maxPendingSent,
        requestCooldownSeconds: parsed.cooldown,
      }))
      setSaved(true)
      setConfirming(false)
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
        hint={`한 계정이 가질 수 있는 친구의 최대 수. 상한에 닿으면 요청도 수락도 막힙니다. ${LIMITS.maxFriends.min}~${LIMITS.maxFriends.max} 사이.`}
        value={maxFriends}
        onChange={edit(setMaxFriends)}
        disabled={loading || !current}
        limits={LIMITS.maxFriends}
      />
      <Field
        label="보낸 요청 대기 상한"
        hint={`아직 상대가 응답하지 않은 요청의 최대 개수. 무차별 요청을 막습니다. ${LIMITS.maxPendingSent.min}~${LIMITS.maxPendingSent.max} 사이.`}
        value={maxPendingSent}
        onChange={edit(setMaxPendingSent)}
        disabled={loading || !current}
        limits={LIMITS.maxPendingSent}
      />
      <Field
        label="연속 요청 최소 간격"
        hint={`같은 사람이 요청을 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 ${LIMITS.cooldown.max}까지, 0이면 제한 없음.`}
        value={cooldown}
        onChange={edit(setCooldown)}
        disabled={loading || !current}
        limits={LIMITS.cooldown}
      />

      <div className="flex items-center gap-3 pt-1">
        <button
          onClick={() => setConfirming(true)}
          disabled={saving || loading || !current || invalid || !changed}
          className="h-9 px-4 inline-flex items-center gap-1.5 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-40 whitespace-nowrap"
        >
          <Save className="w-3.5 h-3.5" />
          저장
        </button>
        {saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
        {current?.updated_at && (
          <span className="text-xs text-neutral-400">
            마지막 수정 {formatDateTime(current.updated_at)}
            {current.updated_by ? ` · ${current.updated_by}` : ''}
          </span>
        )}
      </div>

      <ConfirmDialog
        open={confirming}
        title="제한값을 바꿀까요?"
        description="모든 플레이어에게 곧바로 적용됩니다."
        confirmLabel="저장"
        busy={saving}
        onConfirm={() => void save()}
        onCancel={() => setConfirming(false)}
      >
        {current && (
          <ul className="space-y-1 text-sm text-neutral-700">
            <ChangeRow label="친구 수 상한" before={current.max_friends} after={parsed.maxFriends} />
            <ChangeRow label="보낸 요청 대기 상한" before={current.max_pending_sent} after={parsed.maxPendingSent} />
            <ChangeRow label="연속 요청 최소 간격" before={current.request_cooldown_seconds} after={parsed.cooldown} />
          </ul>
        )}
      </ConfirmDialog>
    </WhiteCard>
  )
}

/** 빈 칸·범위 밖이면 null. 저장 버튼은 null 이 하나라도 있으면 눌리지 않는다. */
function parseField(raw: string, limits: { min: number; max: number }): number | null {
  if (raw.trim() === '') return null
  const n = Number(raw)
  if (!Number.isInteger(n) || n < limits.min || n > limits.max) return null
  return n
}

function ChangeRow({ label, before, after }: { label: string; before: number; after: number | null }) {
  const same = before === after
  return (
    <li className="flex items-center gap-2 whitespace-nowrap">
      <span className="text-neutral-500">{label}</span>
      <span className={same ? 'text-neutral-400' : 'text-neutral-400 line-through'}>{before}</span>
      {!same && <span className="text-neutral-900 font-medium">→ {after}</span>}
    </li>
  )
}

function Field({
  label,
  hint,
  value,
  onChange,
  disabled,
  limits,
}: {
  label: string
  hint: string
  value: string
  onChange: (v: string) => void
  disabled: boolean
  limits: { min: number; max: number }
}) {
  const bad = !disabled && parseField(value, limits) === null
  return (
    <div className="space-y-1">
      <label className="block text-sm font-medium text-neutral-800">{label}</label>
      <input
        type="number"
        min={limits.min}
        max={limits.max}
        className={`${inputCls} w-40 ${bad ? 'border-red-400 focus:border-red-400 focus:ring-red-200' : ''}`}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
      />
      <p className={`text-xs ${bad ? 'text-red-600' : 'text-neutral-500'}`}>
        {bad ? `${limits.min}에서 ${limits.max} 사이의 정수를 넣어 주세요.` : hint}
      </p>
    </div>
  )
}
