import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Loader2, Pencil, Plus, RefreshCw, RotateCcw, Search, Send, UserRound, XCircle } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { Tabs } from '../components/ui/Tabs'
import { Segmented } from '../components/ui/Segmented'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { TableFooter } from '../components/ui/TableFooter'
import { EditDialog } from '../components/ui/EditDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { Switch } from '../components/ui/Switch'
import { areaCls, inputCls } from '../components/ui/Input'
import { formatCount, formatDateTime } from '../components/ui/format'

type Status = 'open' | 'answered' | 'closed'
type StatusFilter = Status | 'all'

const STATUS_LABEL: Record<Status, string> = { open: '답변 대기', answered: '답변 완료', closed: '종료' }
const STATUS_CLS: Record<Status, string> = {
  open: 'bg-amber-100 text-amber-800',
  answered: 'bg-green-100 text-green-800',
  closed: 'bg-neutral-100 text-neutral-600',
}

// 기기 정보 칸 이름. SDK 가 보내는 키 — support-api 의 META_KEYS 와 같다.
const META_LABEL: Record<string, string> = {
  platform: '플랫폼', app_version: '앱 버전', os: 'OS', device: '기기', language: '언어', server_id: '서버', sdk_version: 'SDK',
}

const PAGE_SIZE = 50

type TicketRow = {
  id: string
  account_id: string | null
  user_id: string
  display_name: string | null
  category: string | null
  status: Status
  was_banned: boolean
  preview: string | null
  created_at: string
  last_message_at: string
}

type ListResponse = { total: number; counts: Record<Status, number>; rows: TicketRow[] }

type Message = {
  id: number
  sender: 'player' | 'admin'
  body: string
  attachments: { path: string; url: string | null }[]
  admin_email: string | null
  created_at: string
}

type Ticket = TicketRow & { meta: Record<string, string>; messages: Message[] }

type Category = { id: number; name: string; localized: Record<string, string>; sort_order: number; is_active: boolean; tickets: number }

export default function Support({ target, onUnauthenticated }: { target: ProjectTarget; onUnauthenticated: () => void }) {
  const [tab, setTab] = useState<'tickets' | 'categories'>('tickets')
  const [error, setError] = useState<string | null>(null)

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="space-y-5">
      <PageHeader title="고객센터" description="플레이어 문의를 보고 답합니다." />
      <ErrorBanner message={error} onDismiss={() => setError(null)} />
      <Tabs value={tab} onChange={setTab} tabs={[['tickets', '문의'], ['categories', '분류']] as const} />
      {tab === 'tickets' ? <TicketsTab target={target} report={report} /> : <CategoriesTab target={target} report={report} />}
    </div>
  )
}

function StatusBadge({ status }: { status: Status }) {
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[11px] font-medium whitespace-nowrap ${STATUS_CLS[status]}`}>
      {STATUS_LABEL[status]}
    </span>
  )
}

function TicketsTab({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [status, setStatus] = useState<StatusFilter>('open')
  const [categoryId, setCategoryId] = useState('')
  const [categories, setCategories] = useState<Category[]>([])
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<ListResponse | null>(null)
  const [failed, setFailed] = useState(false)
  const [openId, setOpenId] = useState<string | null>(null)

  const load = useCallback(async () => {
    setData(null)
    setFailed(false)
    try {
      setData(await callAdmin<ListResponse>(target, 'support.list', {
        status: status === 'all' ? null : status,
        categoryId: categoryId ? Number(categoryId) : null,
        search,
        limit: PAGE_SIZE,
        offset: (page - 1) * PAGE_SIZE,
      }))
    } catch (e: unknown) {
      setFailed(true)
      report(e, '문의를 불러오지 못했습니다.')
    }
  }, [target, status, categoryId, search, page, report])

  useEffect(() => { void load() }, [load])

  // 창에 넘기는 함수는 그릴 때마다 새로 만들지 않는다 — 바뀌면 창이 같은 문의를 다시 불러온다.
  const closeDialog = useCallback(() => setOpenId(null), [])
  const reloadList = useCallback(() => { void load() }, [load])

  useEffect(() => {
    void (async () => {
      try {
        setCategories(await callAdmin<Category[]>(target, 'supportCategories.list'))
      } catch (e: unknown) {
        report(e, '분류를 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  const counts = data?.counts
  const statusOptions: { v: StatusFilter; label: string }[] = [
    { v: 'open', label: counts ? `답변 대기 ${formatCount(counts.open)}` : '답변 대기' },
    { v: 'answered', label: '답변 완료' },
    { v: 'closed', label: '종료' },
    { v: 'all', label: '전체' },
  ]
  const rows = data?.rows ?? []
  const totalPages = Math.max(1, Math.ceil((data?.total ?? 0) / PAGE_SIZE))

  return (
    <WhiteCard>
      <div className="p-5 pb-3 space-y-4">
        <CardHeader
          title="문의 목록"
          description="문의를 누르면 대화를 보고 답할 수 있습니다."
          actions={
            <Button size="compact" onClick={() => void load()} disabled={data === null && !failed}>
              <RefreshCw className={`w-3.5 h-3.5 ${data === null && !failed ? 'animate-spin' : ''}`} />
              새로고침
            </Button>
          }
        />
        <div className="flex flex-wrap items-end gap-4">
          <div>
            <div className="text-xs font-medium text-neutral-500 mb-1">상태</div>
            <Segmented value={status} onChange={(v) => { setStatus(v); setPage(1) }} options={statusOptions} />
          </div>
          <div>
            <div className="text-xs font-medium text-neutral-500 mb-1">분류</div>
            <select className={`${inputCls} w-36`} value={categoryId} onChange={(e) => { setCategoryId(e.target.value); setPage(1) }}>
              <option value="">전체</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          <form
            className="flex items-end gap-2"
            onSubmit={(e) => { e.preventDefault(); setSearch(searchInput.trim()); setPage(1) }}
          >
            <div>
              <div className="text-xs font-medium text-neutral-500 mb-1">플레이어</div>
              <input className={`${inputCls} w-56`} value={searchInput} onChange={(e) => setSearchInput(e.target.value)}
                placeholder="닉네임·유저 ID" />
            </div>
            <Button type="submit"><Search className="w-3.5 h-3.5" />찾기</Button>
          </form>
        </div>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="px-5 py-2 text-left font-medium whitespace-nowrap">상태</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">분류</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">플레이어</th>
              <th className="px-3 py-2 text-left font-medium min-w-[16rem]">내용</th>
              <th className="px-5 py-2 text-left font-medium whitespace-nowrap">마지막 대화</th>
            </tr>
          </thead>
          <tbody>
            {data === null || rows.length === 0 ? (
              <TableStatusRow loading={data === null && !failed} empty={data !== null && rows.length === 0} failed={failed}
                colSpan={5} emptyText="문의가 없습니다." />
            ) : rows.map((r) => (
              <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50 cursor-pointer" onClick={() => setOpenId(r.id)}>
                <td className="px-5 py-2 whitespace-nowrap"><StatusBadge status={r.status} /></td>
                <td className="px-3 py-2 text-neutral-700 whitespace-nowrap">{r.category ?? '없음'}</td>
                <td className="px-3 py-2 text-neutral-800 whitespace-nowrap">
                  {r.display_name || r.user_id}
                  {r.was_banned && <span className="ml-1.5 text-[11px] text-red-600">차단 중 문의</span>}
                </td>
                <td className="px-3 py-2 text-neutral-600 truncate max-w-[28rem]">{r.preview || '(그림만 보냄)'}</td>
                <td className="px-5 py-2 text-neutral-500 whitespace-nowrap">{formatDateTime(r.last_message_at)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <TableFooter summary={`${formatCount(data?.total ?? 0)}건`} page={page} totalPages={totalPages} onPageChange={setPage} />
      {openId && (
        <TicketDialog target={target} ticketId={openId} report={report} onClose={closeDialog} onChanged={reloadList} />
      )}
    </WhiteCard>
  )
}

function TicketDialog({ target, ticketId, report, onClose, onChanged }: {
  target: ProjectTarget
  ticketId: string
  report: (e: unknown, fallback: string) => void
  onClose: () => void
  onChanged: () => void
}) {
  const navigate = useNavigate()
  const [ticket, setTicket] = useState<Ticket | null>(null)
  const [reply, setReply] = useState('')
  const [busy, setBusy] = useState(false)
  const [saveError, setSaveError] = useState('')

  const load = useCallback(async () => {
    try {
      setTicket(await callAdmin<Ticket>(target, 'support.get', { ticketId }))
    } catch (e: unknown) {
      report(e, '문의를 불러오지 못했습니다.')
      onClose()
    }
  }, [target, ticketId, report, onClose])

  useEffect(() => { void load() }, [load])

  const run = async (action: string, params: Record<string, unknown>, after?: () => void) => {
    setBusy(true)
    setSaveError('')
    try {
      await callAdmin(target, action, { ticketId, ...params })
      after?.()
      await load()
      onChanged()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setSaveError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const trimmed = reply.trim()
  const meta = Object.entries(ticket?.meta ?? {}).filter(([, v]) => v)

  return (
    <EditDialog
      size="xl"
      title="문의"
      description={ticket ? `${ticket.display_name || ticket.user_id} · ${ticket.category ?? '분류 없음'}` : undefined}
      busy={busy}
      error={saveError || undefined}
      onClose={onClose}
      actions={ticket?.account_id ? (
        <Button size="sm" onClick={() => navigate(`/players?account=${encodeURIComponent(ticket.account_id!)}&name=${encodeURIComponent(ticket.display_name ?? '')}`)}>
          <UserRound className="w-3 h-3" />플레이어 보기
        </Button>
      ) : undefined}
      footer={ticket && (
        ticket.status === 'closed' ? (
          // 다시 열면 마지막 글이 누구 것인지로 상태를 되돌린다 — 운영자가 마지막으로 답했으면 답변 완료.
          <Button onClick={() => void run('support.setStatus', { status: ticket.messages.at(-1)?.sender === 'admin' ? 'answered' : 'open' })} disabled={busy}>
            <RotateCcw className="w-3.5 h-3.5" />다시 열기
          </Button>
        ) : (
          <>
            <Button onClick={() => void run('support.setStatus', { status: 'closed' })} disabled={busy}>
              <XCircle className="w-3.5 h-3.5" />종료
            </Button>
            <Button variant="primary" disabled={busy || !trimmed || trimmed.length > 2000}
              onClick={() => void run('support.reply', { body: trimmed }, () => setReply(''))}>
              {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}답변 보내기
            </Button>
          </>
        )
      )}
    >
      {!ticket ? (
        <div className="py-10 flex justify-center text-neutral-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-neutral-500">
            <StatusBadge status={ticket.status} />
            {ticket.was_banned && <span className="text-red-600">차단된 상태에서 보낸 문의</span>}
            <span>문의 시각 {formatDateTime(ticket.created_at)}</span>
            {meta.map(([k, v]) => <span key={k}>{META_LABEL[k] ?? k} {v}</span>)}
          </div>

          <div className="space-y-3">
            {ticket.messages.map((m) => (
              <div key={m.id} className={`flex ${m.sender === 'admin' ? 'justify-end' : 'justify-start'}`}>
                <div className={`max-w-[85%] rounded-lg px-3 py-2 ${m.sender === 'admin' ? 'bg-brand-soft' : 'bg-neutral-50 border border-neutral-200'}`}>
                  {m.body && <p className="text-sm text-neutral-800 whitespace-pre-wrap break-words">{m.body}</p>}
                  {m.attachments.length > 0 && (
                    <div className="flex flex-wrap gap-2 mt-2">
                      {m.attachments.map((a) => a.url ? (
                        <a key={a.path} href={a.url} target="_blank" rel="noreferrer">
                          <img src={a.url} alt="" className="h-24 w-24 object-cover rounded border border-neutral-200" />
                        </a>
                      ) : (
                        <span key={a.path} className="h-24 w-24 flex items-center justify-center rounded border border-neutral-200 text-xs text-neutral-400">그림 없음</span>
                      ))}
                    </div>
                  )}
                  <div className="mt-1 text-[11px] text-neutral-400">
                    {m.sender === 'admin' ? (m.admin_email ?? '운영자') : '플레이어'} · {formatDateTime(m.created_at)}
                  </div>
                </div>
              </div>
            ))}
          </div>

          {ticket.status === 'closed' ? (
            <p className="rounded-md bg-neutral-50 px-3 py-2 text-sm text-neutral-600">종료된 문의입니다. 다시 열면 답할 수 있습니다.</p>
          ) : (
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">답변</label>
              <textarea className={`${areaCls} w-full`} rows={4} value={reply} onChange={(e) => setReply(e.target.value)} />
              <p className={`text-xs ${trimmed.length > 2000 ? 'text-red-600' : 'text-neutral-500'}`}>
                2,000자까지 넣습니다. 보내면 상태가 답변 완료로 바뀌고 플레이어에게 새 답변 표시가 뜹니다.
              </p>
            </div>
          )}
        </div>
      )}
    </EditDialog>
  )
}

type CategoryDraft = { id: number | null; name: string; en: string; sortOrder: string }

function CategoriesTab({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [rows, setRows] = useState<Category[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [draft, setDraft] = useState<CategoryDraft | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')
  const [toggling, setToggling] = useState<number | null>(null)

  const load = useCallback(async () => {
    setFailed(false)
    try {
      setRows(await callAdmin<Category[]>(target, 'supportCategories.list'))
    } catch (e: unknown) {
      setRows([])
      setFailed(true)
      report(e, '분류를 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const save = async () => {
    if (!draft) return
    setSaving(true)
    setSaveError('')
    try {
      const current = rows?.find((r) => r.id === draft.id)
      await callAdmin(target, 'supportCategories.upsert', {
        id: draft.id,
        name: draft.name.trim(),
        localized: { ...(current?.localized ?? {}), en: draft.en.trim() },
        sortOrder: draft.sortOrder.trim() === '' ? null : Number(draft.sortOrder),
        isActive: current?.is_active ?? true,
      })
      setDraft(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setSaveError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const toggle = async (c: Category, active: boolean) => {
    setToggling(c.id)
    try {
      await callAdmin(target, 'supportCategories.upsert', {
        id: c.id, name: c.name, localized: c.localized, sortOrder: c.sort_order, isActive: active,
      })
      await load()
    } catch (e: unknown) {
      report(e, '분류를 바꾸지 못했습니다.')
    } finally {
      setToggling(null)
    }
  }

  const nextOrder = Math.max(0, ...(rows ?? []).map((r) => r.sort_order)) + 1
  const sortOk = draft ? draft.sortOrder.trim() === '' || /^\d{1,5}$/.test(draft.sortOrder.trim()) : true

  return (
    <WhiteCard>
      <div className="p-5 pb-3">
        <CardHeader
          title="문의 분류"
          description="플레이어가 문의를 보낼 때 고르는 분류입니다. 끄면 새 문의에서 고를 수 없고, 이미 받은 문의는 그대로 남습니다."
          actions={
            <Button variant="primary" size="compact" onClick={() => { setSaveError(''); setDraft({ id: null, name: '', en: '', sortOrder: String(nextOrder) }) }}>
              <Plus className="w-3.5 h-3.5" />분류 추가
            </Button>
          }
        />
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="px-5 py-2 text-left font-medium whitespace-nowrap">순서</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">이름</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">영어 이름</th>
              <th className="px-3 py-2 text-right font-medium whitespace-nowrap">문의 수</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">사용</th>
              <th className="px-5 py-2" />
            </tr>
          </thead>
          <tbody>
            {rows === null || rows.length === 0 ? (
              <TableStatusRow loading={rows === null} empty={rows !== null && rows.length === 0} failed={failed} colSpan={6} emptyText="분류가 없습니다." />
            ) : rows.map((c) => (
              <tr key={c.id} className="border-t border-neutral-100">
                <td className="px-5 py-2 tabular-nums text-neutral-500">{c.sort_order}</td>
                <td className="px-3 py-2 text-neutral-800 whitespace-nowrap">{c.name}</td>
                <td className="px-3 py-2 text-neutral-600 whitespace-nowrap">{c.localized?.['en'] || '없음'}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatCount(c.tickets)}</td>
                <td className="px-3 py-2">
                  <Switch label={`${c.name} 사용`} value={c.is_active} disabled={toggling !== null} onChange={(v) => void toggle(c, v)} />
                </td>
                <td className="px-5 py-2 text-right whitespace-nowrap">
                  <Button size="sm" onClick={() => { setSaveError(''); setDraft({ id: c.id, name: c.name, en: c.localized?.['en'] ?? '', sortOrder: String(c.sort_order) }) }}>
                    <Pencil className="w-3 h-3" />바꾸기
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {draft && (
        <EditDialog
          title={draft.id == null ? '분류 추가' : '분류 바꾸기'}
          busy={saving}
          error={saveError || undefined}
          onClose={() => setDraft(null)}
          footer={
            <>
              <Button onClick={() => setDraft(null)} disabled={saving}>취소</Button>
              <Button variant="primary" onClick={() => void save()}
                disabled={saving || !draft.name.trim() || draft.name.trim().length > 40 || !sortOk}>
                {saving && <Loader2 className="w-3.5 h-3.5 animate-spin" />}저장
              </Button>
            </>
          }
        >
          <div className="space-y-4">
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">이름</label>
              <input className={`${inputCls} w-full`} value={draft.name} maxLength={40}
                onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
              <p className="text-xs text-neutral-500">한국어로 보는 플레이어에게 보이는 이름을 40자까지 넣습니다.</p>
            </div>
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">영어 이름</label>
              <input className={`${inputCls} w-full`} value={draft.en} maxLength={40}
                onChange={(e) => setDraft({ ...draft, en: e.target.value })} />
              <p className="text-xs text-neutral-500">한국어가 아닌 플레이어에게 보이는 이름을 넣습니다. 비우면 위 이름이 보입니다.</p>
            </div>
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">순서</label>
              <input className={`${inputCls} w-28`} value={draft.sortOrder} inputMode="numeric"
                onChange={(e) => setDraft({ ...draft, sortOrder: e.target.value })} />
              <p className={`text-xs ${sortOk ? 'text-neutral-500' : 'text-red-600'}`}>작은 수가 위에 오도록 0 이상의 정수를 넣습니다.</p>
            </div>
          </div>
        </EditDialog>
      )}
    </WhiteCard>
  )
}
