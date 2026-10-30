import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { GripVertical, Loader2, Pencil, Plus, RefreshCw, RotateCcw, Search, Send, Trash2, UserRound, XCircle } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { Tabs } from '../components/ui/Tabs'
import { Segmented } from '../components/ui/Segmented'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { TableFooter } from '../components/ui/TableFooter'
import { EditDialog } from '../components/ui/EditDialog'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { InfoList } from '../components/ui/InfoList'
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
  platform: '플랫폼', app_version: '앱 버전', os: 'OS', device: '기기', language: '창 언어', server_id: '서버', sdk_version: 'SDK',
}

const LANG_NAME: Record<string, string> = {
  ko: '한국어', en: '영어', ja: '일본어', zh: '중국어', 'zh-CN': '중국어 간체', 'zh-TW': '중국어 번체', th: '태국어', ru: '러시아어',
  de: '독일어', fr: '프랑스어', es: '스페인어', pt: '포르투갈어', vi: '베트남어', id: '인도네시아어',
}
/** 글자 꼴로 본 언어. 로마자면 'latin', 글이 없으면 null. */
function scriptLang(text: string): string | null {
  if (/[가-힣ㄱ-ㆎ]/.test(text)) return 'ko'
  if (/[぀-ヿ]/.test(text)) return 'ja'
  if (/[一-鿿]/.test(text)) return 'zh'
  if (/[฀-๿]/.test(text)) return 'th'
  if (/[Ѐ-ӿ]/.test(text)) return 'ru'
  if (/[A-Za-z]/.test(text)) return 'latin'
  return null
}

/** 플레이어가 쓴 글(가장 최근 글부터)의 언어와 기기 언어 — 운영자가 어느 언어로 답할지 고르는 근거. */
function playerLanguages(messages: Message[], deviceLang: string | undefined): { written: string | null; device: string | null } {
  const raw = (deviceLang ?? '').trim()
  const device = /^zh-(CN|TW)$/i.test(raw) ? `zh-${raw.slice(3).toUpperCase()}` : raw.slice(0, 2).toLowerCase() || null
  let written: string | null = null
  for (const m of [...messages].reverse()) {
    if (m.sender !== 'player' || !m.body.trim()) continue
    written = scriptLang(m.body)
    if (written) break
  }
  return { written, device }
}

const langName = (code: string | null) => (code ? LANG_NAME[code] ?? code : '알 수 없음')

const PAGE_SIZE = 50

type TicketRow = {
  id: string
  account_id: string | null
  user_id: string
  display_name: string | null
  category: string | null
  status: Status
  was_banned: boolean
  // 게임 밖(고객센터 주소)에서 유저 ID 만 적고 보낸 문의 — 본인이 쓴 것인지 확인되지 않았다.
  via_web?: boolean
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
  const [tab, setTab] = useState<'tickets' | 'faqs' | 'categories'>('tickets')
  const [error, setError] = useState<string | null>(null)

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="space-y-5">
      <PageHeader title="고객센터" description="플레이어 문의를 보고 답합니다." />
      <ErrorBanner message={error} onDismiss={() => setError(null)} />
      <Tabs value={tab} onChange={setTab} tabs={[['tickets', '문의'], ['faqs', '자주 묻는 질문'], ['categories', '분류']] as const} />
      {tab === 'tickets' && <TicketsTab target={target} report={report} />}
      {tab === 'faqs' && <FaqsTab target={target} report={report} />}
      {tab === 'categories' && <CategoriesTab target={target} report={report} />}
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
                  {r.was_banned && <span className="ml-1.5 text-[11px] text-red-600">차단</span>}
                  {r.via_web && <span className="ml-1.5 text-[11px] text-amber-700">게임 밖 접수</span>}
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
  const answerLang = ticket ? playerLanguages(ticket.messages, ticket.meta['language']) : null

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
          <InfoList className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2.5" items={[
            { label: '상태', value: <StatusBadge status={ticket.status} /> },
            { label: '문의 시각', value: formatDateTime(ticket.created_at) },
            ticket.was_banned
              ? { label: '계정 상태', value: '차단', tone: 'danger' as const }
              : { label: '계정 상태', value: '정상' },
            ...(ticket.via_web
              ? [{ label: '접수 경로', value: '게임 밖 주소 · 본인 확인 안 됨' }]
              : []),
            ...Object.keys(META_LABEL).filter((k) => k !== 'language').map((k) => ({ label: META_LABEL[k], value: ticket.meta[k] })),
            ...meta.filter(([k]) => !(k in META_LABEL)).map(([k, v]) => ({ label: k, value: v })),
            { label: '창 언어', value: answerLang?.device ? langName(answerLang.device) : null },
            { label: '문의 글 언어', value: answerLang?.written ? (answerLang.written === 'latin' ? '로마자' : langName(answerLang.written)) : null },
          ]} />

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

type Faq = {
  id: number
  category_id: number | null
  category: string | null
  question: string
  answer: string
  localized: Record<string, { question?: string; answer?: string }>
  sort_order: number
  is_active: boolean
}

type FaqDraft = { id: number | null; categoryId: string; question: string; answer: string; enQuestion: string; enAnswer: string }

function FaqsTab({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [rows, setRows] = useState<Faq[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [categories, setCategories] = useState<Category[]>([])
  const [draft, setDraft] = useState<FaqDraft | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')
  const [toggling, setToggling] = useState<number | null>(null)
  const [deleting, setDeleting] = useState<Faq | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [deleteError, setDeleteError] = useState('')
  // 끌어서 순서 바꾸기 — 놓는 즉시 화면 순서를 바꾸고 저장한다. 저장이 실패하면 다시 불러와 서버 순서로 되돌린다.
  const [dragId, setDragId] = useState<number | null>(null)
  const [overId, setOverId] = useState<number | null>(null)
  const [reordering, setReordering] = useState(false)

  const load = useCallback(async () => {
    setFailed(false)
    try {
      const [faqs, cats] = await Promise.all([
        callAdmin<Faq[]>(target, 'supportFaqs.list'),
        callAdmin<Category[]>(target, 'supportCategories.list'),
      ])
      setRows(faqs)
      setCategories(cats)
    } catch (e: unknown) {
      setRows([])
      setFailed(true)
      report(e, '자주 묻는 질문을 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const save = async () => {
    if (!draft) return
    setSaving(true)
    setSaveError('')
    try {
      const current = rows?.find((r) => r.id === draft.id)
      await callAdmin(target, 'supportFaqs.upsert', {
        id: draft.id,
        categoryId: draft.categoryId ? Number(draft.categoryId) : null,
        question: draft.question.trim(),
        answer: draft.answer.trim(),
        localized: { ...(current?.localized ?? {}), en: { question: draft.enQuestion.trim(), answer: draft.enAnswer.trim() } },
        // 새 질문은 맨 아래에 켜진 채 붙는다. 있던 질문은 순서·사용 여부를 보내지 않는다 — 화면이 불러 둔 옛 값으로
        // 다른 운영자가 끈 질문을 다시 켜지 않게(서버는 빈 값이면 그대로 둔다).
        sortOrder: current ? null : nextOrder,
        isActive: current ? null : true,
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

  const toggle = async (f: Faq, active: boolean) => {
    setToggling(f.id)
    try {
      await callAdmin(target, 'supportFaqs.setActive', { id: f.id, isActive: active })
      await load()
    } catch (e: unknown) {
      report(e, '자주 묻는 질문을 바꾸지 못했습니다.')
    } finally {
      setToggling(null)
    }
  }

  const remove = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    setDeleteError('')
    try {
      await callAdmin(target, 'supportFaqs.delete', { id: deleting.id })
      setDeleting(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setDeleteError(e instanceof Error ? e.message : '지우지 못했습니다.')
    } finally {
      setDeleteBusy(false)
    }
  }

  // 놓은 줄의 자리를 차지한다 — 아래로 끌면 그 줄 뒤, 위로 끌면 그 줄 앞.
  const drop = async (targetId: number) => {
    const from = rows?.findIndex((r) => r.id === dragId) ?? -1
    const to = rows?.findIndex((r) => r.id === targetId) ?? -1
    setDragId(null)
    setOverId(null)
    if (!rows || from < 0 || to < 0 || from === to) return
    const next = [...rows]
    const [moved] = next.splice(from, 1)
    next.splice(to, 0, moved!)
    setRows(next.map((r, i) => ({ ...r, sort_order: i + 1 })))
    setReordering(true)
    try {
      await callAdmin(target, 'supportFaqs.reorder', { ids: next.map((r) => r.id) })
    } catch (e: unknown) {
      report(e, '순서를 저장하지 못했습니다.')
      await load()
    } finally {
      setReordering(false)
    }
  }

  const nextOrder = Math.max(0, ...(rows ?? []).map((r) => r.sort_order)) + 1
  // 영어는 질문·답을 함께 넣거나 함께 비운다 — 한쪽만 있으면 영어 질문에 한국어 답이 붙는다.
  const enPaired = !!draft && !!draft.enQuestion.trim() === !!draft.enAnswer.trim()
  const draftOk = !!draft && !!draft.question.trim() && draft.question.trim().length <= 200
    && !!draft.answer.trim() && draft.answer.trim().length <= 4000 && enPaired

  return (
    <WhiteCard>
      <div className="p-5 pb-3">
        <CardHeader
          title="자주 묻는 질문"
          description="플레이어가 고객센터 창에서 문의하기 전에 보는 질문과 답입니다. 줄을 끌어 놓으면 창에 보이는 순서가 바뀝니다."
          actions={
            <Button variant="primary" size="compact" onClick={() => {
              setSaveError('')
              setDraft({ id: null, categoryId: '', question: '', answer: '', enQuestion: '', enAnswer: '' })
            }}>
              <Plus className="w-3.5 h-3.5" />질문 추가
            </Button>
          }
        />
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="pl-3 pr-0 py-2 w-8" />
              <th className="px-3 py-2 text-left font-medium min-w-[16rem]">질문</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">분류</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">영어</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">사용</th>
              <th className="px-5 py-2" />
            </tr>
          </thead>
          <tbody>
            {rows === null || rows.length === 0 ? (
              <TableStatusRow loading={rows === null} empty={rows !== null && rows.length === 0} failed={failed} colSpan={6} emptyText="자주 묻는 질문이 없습니다." />
            ) : rows.map((f) => (
              <tr key={f.id} draggable={!reordering}
                onDragStart={(e) => { e.dataTransfer.effectAllowed = 'move'; setDragId(f.id) }}
                onDragOver={(e) => { if (dragId == null) return; e.preventDefault(); if (overId !== f.id) setOverId(f.id) }}
                onDrop={(e) => { e.preventDefault(); void drop(f.id) }}
                onDragEnd={() => { setDragId(null); setOverId(null) }}
                className={`border-t border-neutral-100 ${dragId === f.id ? 'opacity-40' : ''} ${overId === f.id && dragId !== f.id ? 'bg-brand-soft' : ''}`}>
                <td className="pl-3 pr-0 py-2 text-neutral-400 cursor-grab active:cursor-grabbing" title="끌어서 순서 바꾸기">
                  <GripVertical className="w-4 h-4" />
                </td>
                <td className="px-3 py-2 text-neutral-800">{f.question}</td>
                <td className="px-3 py-2 text-neutral-600 whitespace-nowrap">{f.category ?? '전체'}</td>
                <td className="px-3 py-2 text-neutral-600 whitespace-nowrap">{f.localized?.['en']?.question ? '있음' : '없음'}</td>
                <td className="px-3 py-2">
                  <Switch label={`${f.question} 사용`} value={f.is_active} disabled={toggling !== null} onChange={(v) => void toggle(f, v)} />
                </td>
                <td className="px-5 py-2 text-right whitespace-nowrap space-x-2">
                  <Button size="sm" onClick={() => {
                    setSaveError('')
                    setDraft({
                      id: f.id, categoryId: f.category_id ? String(f.category_id) : '', question: f.question, answer: f.answer,
                      enQuestion: f.localized?.['en']?.question ?? '', enAnswer: f.localized?.['en']?.answer ?? '',
                    })
                  }}>
                    <Pencil className="w-3 h-3" />바꾸기
                  </Button>
                  <Button size="sm" variant="dangerOutline" onClick={() => { setDeleteError(''); setDeleting(f) }}>
                    <Trash2 className="w-3 h-3" />지우기
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {draft && (
        <EditDialog
          size="lg"
          title={draft.id == null ? '질문 추가' : '질문 바꾸기'}
          busy={saving}
          error={saveError || undefined}
          onClose={() => setDraft(null)}
          footer={
            <>
              <Button onClick={() => setDraft(null)} disabled={saving}>취소</Button>
              <Button variant="primary" onClick={() => void save()} disabled={saving || !draftOk}>
                {saving && <Loader2 className="w-3.5 h-3.5 animate-spin" />}저장
              </Button>
            </>
          }
        >
          <div className="space-y-4">
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">분류</label>
              <select className={`${inputCls} w-48`} value={draft.categoryId} onChange={(e) => setDraft({ ...draft, categoryId: e.target.value })}>
                <option value="">전체</option>
                {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
              <p className="text-xs text-neutral-500">문의할 때 이 분류를 고른 플레이어에게 먼저 보입니다. 전체면 첫 화면에만 보입니다.</p>
            </div>
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">질문</label>
              <input className={`${inputCls} w-full`} value={draft.question} maxLength={200}
                onChange={(e) => setDraft({ ...draft, question: e.target.value })} />
            </div>
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">답</label>
              <textarea className={`${areaCls} w-full`} rows={5} value={draft.answer} maxLength={4000}
                onChange={(e) => setDraft({ ...draft, answer: e.target.value })} />
              <p className="text-xs text-neutral-500">줄을 바꾸면 창에서도 줄이 바뀝니다. 4,000자까지 넣습니다.</p>
            </div>
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">영어 질문</label>
              <input className={`${inputCls} w-full`} value={draft.enQuestion} maxLength={200}
                onChange={(e) => setDraft({ ...draft, enQuestion: e.target.value })} />
            </div>
            <div className="space-y-1">
              <label className="block text-sm font-medium text-neutral-800">영어 답</label>
              <textarea className={`${areaCls} w-full`} rows={4} value={draft.enAnswer} maxLength={4000}
                onChange={(e) => setDraft({ ...draft, enAnswer: e.target.value })} />
              <p className={`text-xs ${enPaired ? 'text-neutral-500' : 'text-red-600'}`}>
                한국어가 아닌 플레이어에게 보이는 문구를 영어 질문과 함께 넣습니다. 둘 다 비우면 위 한국어 문구가 보입니다.
              </p>
            </div>
          </div>
        </EditDialog>
      )}
      <ConfirmDialog
        open={deleting !== null}
        title="질문을 지울까요?"
        description={deleting?.question}
        confirmLabel="지우기"
        danger
        busy={deleteBusy}
        error={deleteError}
        onConfirm={() => void remove()}
        onCancel={() => setDeleting(null)}
      />
    </WhiteCard>
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
        // 있던 분류는 사용 여부를 보내지 않는다 — 옛 화면 값으로 다른 운영자가 끈 분류를 다시 켜지 않게.
        isActive: current ? null : true,
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
      await callAdmin(target, 'supportCategories.setActive', { id: c.id, isActive: active })
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
