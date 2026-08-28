import { useEffect, useRef, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, Trash2, ShieldOff, ChevronLeft, ChevronRight, X, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime } from '../components/ui/format'

type ChannelRow = { id: string; kind: string; code: string; display_name: string; is_active: boolean }
type MessageRow = {
  id: number; channel_id: string; account_id: string | null; user_id: string; display_name: string
  content: string; created_at: string; deleted_at: string | null; deleted_by: string | null
}
type MessageData = { rows: MessageRow[]; total: number; pageSize: number }
type MuteRow = { id: number; account_id: string; channel_id: string | null; until: string; reason: string; created_by: string | null; created_at: string }
type MuteData = { rows: MuteRow[]; total: number; pageSize: number }
type PlayerRow = { account_id: string; display_name: string }

const KIND_LABEL: Record<string, string> = { global: '전체', server: '서버', group: '그룹', direct: '귓속말' }

function MuteModal({
  target,
  onUnauthenticated,
  channels,
  fixedAccount,
  onClose,
  onSaved,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  channels: ChannelRow[]
  fixedAccount: { accountId: string; displayName: string } | null
  onClose: () => void
  onSaved: () => void
}) {
  const [accountId, setAccountId] = useState(fixedAccount?.accountId ?? '')
  const [accountLabel, setAccountLabel] = useState(fixedAccount?.displayName ?? '')
  const [playerQuery, setPlayerQuery] = useState('')
  const [playerRows, setPlayerRows] = useState<PlayerRow[]>([])
  const reqRef = useRef(0)
  const [channelId, setChannelId] = useState('')
  const [minutes, setMinutes] = useState(60)
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (fixedAccount) return
    const q = playerQuery.trim()
    if (!q) { setPlayerRows([]); return }
    const reqId = ++reqRef.current
    const timer = setTimeout(() => {
      void (async () => {
        try {
          const res = await callAdmin<{ rows: PlayerRow[] }>(target, 'players.list', { search: q, page: 1, bannedOnly: false })
          if (reqId !== reqRef.current) return
          setPlayerRows(res.rows)
        } catch { /* 검색 실패는 조용히 무시 — 다시 입력하면 재시도된다 */ }
      })()
    }, 300)
    return () => clearTimeout(timer)
  }, [target, playerQuery, fixedAccount])

  const canSubmit = accountId.trim() !== '' && minutes > 0

  const submit = async () => {
    if (!canSubmit || loading) return
    setLoading(true); setError('')
    try {
      await callAdmin(target, 'chat.mute', { accountId, channelId: channelId || null, minutes, reason })
      onSaved()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">채팅 차단</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          {fixedAccount ? (
            <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
              <div className="text-neutral-800">{accountLabel}</div>
              <div className="text-xs text-neutral-500 mt-0.5 font-mono">{accountId}</div>
            </div>
          ) : (
            <div>
              <label className="block text-xs text-neutral-500 mb-1">플레이어</label>
              {accountId ? (
                <div className="flex items-center gap-2 rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
                  <span className="flex-1">{accountLabel}</span>
                  <button onClick={() => { setAccountId(''); setAccountLabel('') }} className="text-neutral-400 hover:text-neutral-700"><X className="w-3.5 h-3.5" /></button>
                </div>
              ) : (
                <>
                  <input
                    value={playerQuery}
                    onChange={(e) => setPlayerQuery(e.target.value)}
                    className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
                  />
                  {playerRows.length > 0 && (
                    <div className="mt-1.5 max-h-32 overflow-auto border border-neutral-200 rounded-md divide-y divide-neutral-100">
                      {playerRows.map((p) => (
                        <button
                          key={p.account_id}
                          onClick={() => { setAccountId(p.account_id); setAccountLabel(p.display_name) }}
                          className="w-full text-left px-3 py-1.5 text-sm hover:bg-neutral-50"
                        >
                          {p.display_name} <span className="text-xs text-neutral-400 font-mono">{p.account_id.slice(0, 8)}</span>
                        </button>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
          )}
          <div>
            <label className="block text-xs text-neutral-500 mb-1">채널</label>
            <select value={channelId} onChange={(e) => setChannelId(e.target.value)} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]">
              <option value="">전체 채널</option>
              {channels.map((c) => (<option key={c.id} value={c.id}>{c.display_name || c.code} ({KIND_LABEL[c.kind] ?? c.kind})</option>))}
            </select>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">차단 시간(분)</label>
            <input type="number" min={1} value={minutes} onChange={(e) => setMinutes(Number(e.target.value))} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">사유</label>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
          </div>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button onClick={() => { void submit() }} disabled={loading || !canSubmit} className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60">
            {loading ? '처리 중…' : '차단'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function ChatModeration({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const navigate = useNavigate()
  const [tab, setTab] = useState<'messages' | 'mutes'>('messages')
  const [channels, setChannels] = useState<ChannelRow[]>([])
  const [error, setError] = useState('')

  const [channelId, setChannelId] = useState('')
  const [includeDeleted, setIncludeDeleted] = useState(false)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [msgData, setMsgData] = useState<MessageData | null>(null)
  const [msgLoading, setMsgLoading] = useState(false)
  const [delTarget, setDelTarget] = useState<MessageRow | null>(null)
  const [deleting, setDeleting] = useState(false)

  const [mutePage, setMutePage] = useState(1)
  const [muteData, setMuteData] = useState<MuteData | null>(null)
  const [mutesLoading, setMutesLoading] = useState(false)
  const [unmuteTarget, setUnmuteTarget] = useState<MuteRow | null>(null)
  const [unmuting, setUnmuting] = useState(false)

  const [muteModal, setMuteModal] = useState<{ accountId: string; displayName: string } | null>(null)
  const [showMuteAdd, setShowMuteAdd] = useState(false)

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  useEffect(() => {
    void (async () => {
      try {
        setChannels((await callAdmin<{ rows: ChannelRow[] }>(target, 'chat.channels')).rows)
      } catch (e: unknown) {
        report(e, '채널을 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  const loadMessages = useCallback(async () => {
    setMsgLoading(true)
    try {
      setMsgData(await callAdmin<MessageData>(target, 'chat.messages', { channelId, includeDeleted, search, page }))
    } catch (e: unknown) {
      report(e, '메시지를 불러오지 못했습니다.')
      setMsgData(null)
    } finally {
      setMsgLoading(false)
    }
  }, [target, channelId, includeDeleted, search, page, report])
  useEffect(() => { if (tab === 'messages') void loadMessages() }, [tab, loadMessages])

  const loadMutes = useCallback(async (pageOverride?: number) => {
    setMutesLoading(true)
    try {
      setMuteData(await callAdmin<MuteData>(target, 'chat.mutes', { page: pageOverride ?? mutePage }))
    } catch (e: unknown) {
      report(e, '차단 목록을 불러오지 못했습니다.')
    } finally {
      setMutesLoading(false)
    }
  }, [target, mutePage, report])
  useEffect(() => { if (tab === 'mutes') void loadMutes() }, [tab, loadMutes])

  const rows = msgData?.rows ?? []
  const total = msgData?.total ?? 0
  const pageSize = msgData?.pageSize ?? 30
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const mutes = muteData?.rows ?? []
  const muteTotal = muteData?.total ?? 0
  const mutePageSize = muteData?.pageSize ?? 30
  const muteTotalPages = Math.max(1, Math.ceil(muteTotal / mutePageSize))

  const submitSearch = () => { setSearch(searchInput); setPage(1) }

  const channelLabel = (id: string) => {
    const c = channels.find((x) => x.id === id)
    return c ? (c.display_name || c.code) : '-'
  }

  const goPlayer = (accountId: string | null, name: string) => {
    if (!accountId) return
    navigate(`/players?account=${encodeURIComponent(accountId)}&name=${encodeURIComponent(name)}`)
  }

  const confirmDeleteMessage = async () => {
    if (!delTarget) return
    setDeleting(true)
    try {
      await callAdmin(target, 'chat.deleteMessage', { id: delTarget.id })
      const deletedId = delTarget.id
      setDelTarget(null)
      setMsgData((d) => {
        if (!d) return d
        if (includeDeleted) {
          const now = new Date().toISOString()
          return { ...d, rows: d.rows.map((m) => (m.id === deletedId ? { ...m, deleted_at: now } : m)) }
        }
        return { ...d, rows: d.rows.filter((m) => m.id !== deletedId), total: Math.max(0, d.total - 1) }
      })
    } catch (e: unknown) {
      report(e, '삭제에 실패했습니다.')
    } finally {
      setDeleting(false)
    }
  }

  const confirmUnmute = async () => {
    if (!unmuteTarget) return
    setUnmuting(true)
    try {
      await callAdmin(target, 'chat.unmute', { id: unmuteTarget.id })
      const unmutedId = unmuteTarget.id
      setUnmuteTarget(null)
      setMuteData((d) => (d ? { ...d, rows: d.rows.filter((m) => m.id !== unmutedId), total: Math.max(0, d.total - 1) } : d))
    } catch (e: unknown) {
      report(e, '해제에 실패했습니다.')
    } finally {
      setUnmuting(false)
    }
  }

  const tabCls = (on: boolean) => ['px-4 py-1.5 rounded-md text-sm transition-colors', on ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'].join(' ')
  const inputCls = 'h-9 px-2.5 rounded-md border border-neutral-300 text-sm bg-white'

  return (
    <div className="space-y-4">
      <PageHeader title="채팅 관리" description="채팅 메시지를 검토하고, 문제가 있는 플레이어를 차단합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
        <button type="button" onClick={() => setTab('messages')} className={tabCls(tab === 'messages')}>메시지</button>
        <button type="button" onClick={() => setTab('mutes')} className={tabCls(tab === 'mutes')}>차단 목록</button>
      </div>

      {tab === 'messages' && (
        <>
          <WhiteCard className="p-4">
            <div className="flex flex-wrap items-center gap-3">
              <select value={channelId} onChange={(e) => { setChannelId(e.target.value); setPage(1) }} className={inputCls + ' w-48'}>
                <option value="">전체 채널</option>
                {channels.map((c) => (<option key={c.id} value={c.id}>{c.display_name || c.code}</option>))}
              </select>
              <input value={searchInput} onChange={(e) => setSearchInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submitSearch()} placeholder="닉네임 / 내용 / 유저 ID" className="flex-1 min-w-[200px] h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
              <label className="flex items-center gap-1.5 text-xs text-neutral-600 cursor-pointer">
                <input type="checkbox" checked={includeDeleted} onChange={(e) => { setIncludeDeleted(e.target.checked); setPage(1) }} className="rounded" />
                삭제된 메시지 포함
              </label>
              <button onClick={submitSearch} disabled={msgLoading} className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60">
                {msgLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
              </button>
            </div>
          </WhiteCard>

          <WhiteCard>
            <div className="overflow-auto">
              <table className="w-full text-sm">
                <thead className="bg-neutral-50 text-neutral-500 text-xs">
                  <tr>
                    <th className="text-left px-4 py-2.5 font-medium w-40">시각</th>
                    <th className="text-left px-4 py-2.5 font-medium w-28">채널</th>
                    <th className="text-left px-4 py-2.5 font-medium">닉네임</th>
                    <th className="text-left px-4 py-2.5 font-medium">내용</th>
                    <th className="px-3 py-2.5 w-24" />
                  </tr>
                </thead>
                <tbody>
                  {msgLoading || rows.length === 0 ? (
                    <TableStatusRow loading={msgLoading} empty={rows.length === 0} colSpan={5} emptyText="메시지가 없습니다." />
                  ) : (
                    rows.map((m) => (
                      <tr key={m.id} className={`border-t border-neutral-100 ${m.deleted_at ? 'bg-neutral-50/50' : ''}`}>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(m.created_at)}</td>
                        <td className="px-4 py-3 text-neutral-600">{channelLabel(m.channel_id)}</td>
                        <td className="px-4 py-3">
                          {m.account_id ? (
                            <button onClick={() => goPlayer(m.account_id, m.display_name)} className="text-[#1677ff] hover:underline">{m.display_name}</button>
                          ) : (m.display_name || '-')}
                        </td>
                        <td className="px-4 py-3 text-neutral-700 max-w-[420px] truncate" title={m.content}>
                          {m.deleted_at ? <span className="text-neutral-400 line-through">{m.content}</span> : m.content}
                        </td>
                        <td className="px-3 py-3">
                          <div className="flex items-center justify-end gap-1">
                            {m.account_id && (
                              <button onClick={() => setMuteModal({ accountId: m.account_id!, displayName: m.display_name })} title="차단" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                                <ShieldOff className="w-3.5 h-3.5" />
                              </button>
                            )}
                            {!m.deleted_at && (
                              <button onClick={() => setDelTarget(m)} title="삭제" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500">
                                <Trash2 className="w-3.5 h-3.5" />
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))
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

      {tab === 'mutes' && (
        <>
          <div className="flex justify-end">
            <button onClick={() => setShowMuteAdd(true)} className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90">
              <ShieldOff className="w-4 h-4" />차단 추가
            </button>
          </div>
          <WhiteCard>
            <div className="overflow-auto">
              <table className="w-full text-sm">
                <thead className="bg-neutral-50 text-neutral-500 text-xs">
                  <tr>
                    <th className="text-left px-5 py-2.5 font-medium">계정</th>
                    <th className="text-left px-4 py-2.5 font-medium w-28">채널</th>
                    <th className="text-left px-4 py-2.5 font-medium w-40">해제 시각</th>
                    <th className="text-left px-4 py-2.5 font-medium">사유</th>
                    <th className="text-left px-4 py-2.5 font-medium w-24">등록자</th>
                    <th className="px-3 py-2.5 w-20" />
                  </tr>
                </thead>
                <tbody>
                  {mutesLoading || mutes.length === 0 ? (
                    <TableStatusRow loading={mutesLoading} empty={mutes.length === 0} colSpan={6} emptyText="차단된 플레이어가 없습니다." />
                  ) : (
                    mutes.map((m) => (
                      <tr key={m.id} className="border-t border-neutral-100">
                        <td className="px-5 py-3">
                          <button onClick={() => goPlayer(m.account_id, '')} className="text-[#1677ff] hover:underline font-mono text-xs">{m.account_id.slice(0, 8)}</button>
                        </td>
                        <td className="px-4 py-3 text-neutral-600">{m.channel_id ? channelLabel(m.channel_id) : '전체'}</td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(m.until)}</td>
                        <td className="px-4 py-3 text-neutral-700">{m.reason || '-'}</td>
                        <td className="px-4 py-3 text-neutral-600">{m.created_by ?? '-'}</td>
                        <td className="px-3 py-3 text-right">
                          <button onClick={() => setUnmuteTarget(m)} className="text-xs px-2 py-1 rounded border border-neutral-200 text-neutral-600 hover:bg-neutral-50">해제</button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
              <span>{muteTotal} result{muteTotal === 1 ? '' : 's'}</span>
              <div className="flex items-center gap-1">
                <button disabled={mutePage <= 1} onClick={() => setMutePage((p) => Math.max(1, p - 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronLeft className="w-3.5 h-3.5" /></button>
                <span className="px-2">{mutePage} / {muteTotalPages}</span>
                <button disabled={mutePage >= muteTotalPages} onClick={() => setMutePage((p) => Math.min(muteTotalPages, p + 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronRight className="w-3.5 h-3.5" /></button>
              </div>
            </div>
          </WhiteCard>
        </>
      )}

      {muteModal && (
        <MuteModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          channels={channels}
          fixedAccount={muteModal}
          onClose={() => setMuteModal(null)}
          onSaved={() => { setMuteModal(null); setMutePage(1); if (tab === 'mutes') void loadMutes(1) }}
        />
      )}
      {showMuteAdd && (
        <MuteModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          channels={channels}
          fixedAccount={null}
          onClose={() => setShowMuteAdd(false)}
          onSaved={() => { setShowMuteAdd(false); setMutePage(1); void loadMutes(1) }}
        />
      )}

      <ConfirmDialog
        open={!!delTarget}
        title="메시지 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        onConfirm={() => void confirmDeleteMessage()}
        onCancel={() => setDelTarget(null)}
        description="삭제된 메시지는 플레이어 화면에서 사라집니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.display_name}</div>
            <div className="text-xs text-neutral-500 mt-1">{delTarget.content}</div>
          </div>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!unmuteTarget}
        title="차단 해제"
        confirmLabel="해제"
        busy={unmuting}
        onConfirm={() => void confirmUnmute()}
        onCancel={() => setUnmuteTarget(null)}
        description="바로 다시 채팅할 수 있게 됩니다."
      >
        {unmuteTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm font-mono">{unmuteTarget.account_id}</div>
        )}
      </ConfirmDialog>
    </div>
  )
}
