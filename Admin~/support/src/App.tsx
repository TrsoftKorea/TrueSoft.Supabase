import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { ChevronLeft, ImagePlus, Loader2, MessageSquarePlus, Send, X } from 'lucide-react'
import { ApiError, CLOSE_URL, type Attachment, type Category, type Look, type Status, type SupportApi, type Ticket, type TicketSummary } from './api'
import { formatTime, pickLang, reasonText, t, type Lang } from './i18n'
import { shrinkImage } from './image'

type View = { kind: 'list' } | { kind: 'new' } | { kind: 'ticket'; id: string }

const MAX_ATTACH = 3
const MAX_BODY = 2000

const STATUS_CLS: Record<Status, string> = {
  open: 'bg-amber-100 text-amber-800',
  answered: 'bg-green-100 text-green-800',
  closed: 'bg-neutral-100 text-neutral-600',
}

// 게임 안 창이면(?app=1) 닫기 단추를 보인다. 브라우저로 열었으면 브라우저 탭을 닫으면 된다.
const IN_APP = new URLSearchParams(location.search).get('app') === '1'

export function App({ api }: { api: SupportApi | null }) {
  const initialLang = pickLang(new URLSearchParams(location.search).get('lang') ?? navigator.language)
  const [lang, setLang] = useState<Lang>(initialLang)
  const [look, setLook] = useState<Look | null>(null)
  const [banned, setBanned] = useState(false)
  const [fatal, setFatal] = useState<string | null>(api ? null : 'support_session_missing')
  const [ready, setReady] = useState(false)
  const [view, setView] = useState<View>({ kind: 'list' })
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    if (!api) return
    void api.look().then(setLook).catch(() => {})
    api.me()
      .then((me) => { setBanned(me.banned); setLang(pickLang(me.lang)); setReady(true) })
      .catch((e: unknown) => setFatal(e instanceof ApiError ? e.reason : 'server_error'))
  }, [api])

  useEffect(() => {
    if (look?.color) document.documentElement.style.setProperty('--accent', look.color)
    document.title = `${look?.name ? `${look.name} ` : ''}${t(lang, 'title')}`
  }, [look, lang])

  const fail = useCallback((e: unknown) => {
    const reason = e instanceof ApiError ? e.reason : 'server_error'
    if (reason === 'support_session_expired' || reason === 'support_session_invalid') setFatal(reason)
    else setNotice(reasonText(lang, reason))
  }, [lang])

  return (
    <div className="min-h-screen flex flex-col max-w-xl mx-auto">
      <header className="sticky top-0 z-10 bg-white border-b border-neutral-200 px-3 h-14 flex items-center gap-2">
        {view.kind !== 'list' ? (
          <button type="button" className="p-1.5 -ml-1 text-neutral-600" aria-label={t(lang, 'back')} onClick={() => setView({ kind: 'list' })}>
            <ChevronLeft className="w-5 h-5" />
          </button>
        ) : look?.icon ? (
          <img src={look.icon} alt="" className="w-7 h-7 rounded-md" />
        ) : null}
        <div className="flex-1 min-w-0">
          <div className="text-[15px] font-semibold text-neutral-900 truncate">{t(lang, 'title')}</div>
          {look?.name && <div className="text-xs text-neutral-500 truncate">{look.name}</div>}
        </div>
        {IN_APP && (
          <button type="button" className="p-1.5 text-neutral-500" aria-label={t(lang, 'close')} onClick={() => { location.href = CLOSE_URL }}>
            <X className="w-5 h-5" />
          </button>
        )}
      </header>

      <main className="flex-1 px-4 py-4 space-y-4">
        {fatal ? (
          <p className="py-16 text-center text-sm text-neutral-600">
            {fatal === 'support_session_missing' ? t(lang, 'sessionMissing') : reasonText(lang, fatal)}
          </p>
        ) : !ready || !api ? (
          <Spinner label={t(lang, 'loading')} />
        ) : (
          <>
            {banned && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700">{t(lang, 'banned')}</div>
            )}
            {notice && (
              <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5 text-sm text-amber-800">
                <span className="flex-1">{notice}</span>
                <button type="button" aria-label={t(lang, 'close')} onClick={() => setNotice(null)}><X className="w-4 h-4" /></button>
              </div>
            )}
            {view.kind === 'list' && (
              <ListView api={api} lang={lang} onOpen={(id) => { setNotice(null); setView({ kind: 'ticket', id }) }}
                onNew={() => { setNotice(null); setView({ kind: 'new' }) }} onError={fail} />
            )}
            {view.kind === 'new' && (
              <NewView api={api} lang={lang} onError={fail}
                onDone={(id) => { setNotice(t(lang, 'sent')); setView({ kind: 'ticket', id }) }} />
            )}
            {view.kind === 'ticket' && <TicketView api={api} lang={lang} id={view.id} onError={fail} />}
          </>
        )}
      </main>
    </div>
  )
}

function Spinner({ label }: { label: string }) {
  return (
    <div className="py-16 flex flex-col items-center gap-2 text-sm text-neutral-500">
      <Loader2 className="w-5 h-5 animate-spin" />
      {label}
    </div>
  )
}

function AccentButton({ children, disabled, onClick, className = '' }: { children: ReactNode; disabled?: boolean; onClick: () => void; className?: string }) {
  return (
    <button type="button" disabled={disabled} onClick={onClick}
      className={`inline-flex items-center justify-center gap-1.5 rounded-lg px-4 h-11 text-sm font-medium text-white disabled:opacity-40 ${className}`}
      style={{ backgroundColor: 'var(--accent)' }}>
      {children}
    </button>
  )
}

function StatusBadge({ lang, status }: { lang: Lang; status: Status }) {
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[11px] font-medium whitespace-nowrap ${STATUS_CLS[status]}`}>
      {t(lang, `status_${status}`)}
    </span>
  )
}

function ListView({ api, lang, onOpen, onNew, onError }: {
  api: SupportApi; lang: Lang; onOpen: (id: string) => void; onNew: () => void; onError: (e: unknown) => void
}) {
  const [rows, setRows] = useState<TicketSummary[] | null>(null)

  useEffect(() => {
    api.tickets(lang).then(setRows).catch((e: unknown) => { setRows([]); onError(e) })
  }, [api, lang, onError])

  return (
    <div className="space-y-3">
      <AccentButton className="w-full" onClick={onNew}>
        <MessageSquarePlus className="w-4 h-4" />{t(lang, 'newTicket')}
      </AccentButton>
      <h2 className="pt-2 text-sm font-semibold text-neutral-700">{t(lang, 'myTickets')}</h2>
      {rows === null ? (
        <Spinner label={t(lang, 'loading')} />
      ) : rows.length === 0 ? (
        <p className="py-10 text-center text-sm text-neutral-500">{t(lang, 'empty')}</p>
      ) : (
        <ul className="space-y-2">
          {rows.map((r) => (
            <li key={r.id}>
              <button type="button" onClick={() => onOpen(r.id)}
                className="w-full text-left rounded-lg border border-neutral-200 bg-white px-3 py-3 active:bg-neutral-50">
                <div className="flex items-center gap-2">
                  <StatusBadge lang={lang} status={r.status} />
                  <span className="text-xs text-neutral-500 truncate">{r.category}</span>
                  {r.unread && (
                    <span className="ml-auto inline-flex items-center gap-1 text-[11px] font-medium text-red-600">
                      <span className="w-1.5 h-1.5 rounded-full bg-red-500" />{t(lang, 'newReply')}
                    </span>
                  )}
                </div>
                <p className="mt-1.5 text-sm text-neutral-800 line-clamp-2 break-words">{r.preview || t(lang, 'imageOnly')}</p>
                <p className="mt-1 text-xs text-neutral-400">{formatTime(lang, r.last_message_at)}</p>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** 그림 붙이기 — 고르는 즉시 줄여 올려 경로를 받아 둔다. 보내기는 그 경로만 넘긴다. */
function AttachPicker({ api, lang, items, onChange, onError }: {
  api: SupportApi; lang: Lang; items: Attachment[]; onChange: (next: Attachment[]) => void; onError: (e: unknown) => void
}) {
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)

  const pick = async (files: FileList | null) => {
    if (!files) return
    setBusy(true)
    let next = items
    try {
      for (const f of Array.from(files).slice(0, MAX_ATTACH - items.length)) {
        const img = await shrinkImage(f)
        const up = await api.upload(img.contentType, img.base64)
        next = [...next, { path: up.path, url: up.url ?? img.previewUrl }]
        onChange(next)
      }
    } catch (e: unknown) {
      onError(e instanceof ApiError ? e : new ApiError('support_attachments_invalid'))
    } finally {
      setBusy(false)
      if (input.current) input.current.value = ''
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {items.map((a) => (
          <div key={a.path} className="relative">
            <img src={a.url ?? ''} alt="" className="w-20 h-20 object-cover rounded-lg border border-neutral-200" />
            <button type="button" aria-label={t(lang, 'cancel')} onClick={() => onChange(items.filter((x) => x.path !== a.path))}
              className="absolute -top-1.5 -right-1.5 w-6 h-6 rounded-full bg-neutral-800 text-white flex items-center justify-center">
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        ))}
        {items.length < MAX_ATTACH && (
          <button type="button" disabled={busy} onClick={() => input.current?.click()}
            className="w-20 h-20 rounded-lg border border-dashed border-neutral-300 flex flex-col items-center justify-center gap-1 text-xs text-neutral-500 disabled:opacity-50">
            {busy ? <Loader2 className="w-5 h-5 animate-spin" /> : <ImagePlus className="w-5 h-5" />}
            {t(lang, 'attach')}
          </button>
        )}
      </div>
      <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" multiple className="hidden"
        onChange={(e) => void pick(e.target.files)} />
    </div>
  )
}

function NewView({ api, lang, onDone, onError }: {
  api: SupportApi; lang: Lang; onDone: (id: string) => void; onError: (e: unknown) => void
}) {
  const [categories, setCategories] = useState<Category[] | null>(null)
  const [categoryId, setCategoryId] = useState<number | null>(null)
  const [body, setBody] = useState('')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [sending, setSending] = useState(false)

  useEffect(() => {
    api.categories(lang).then(setCategories).catch((e: unknown) => { setCategories([]); onError(e) })
  }, [api, lang, onError])

  const send = async () => {
    if (categoryId == null) return
    setSending(true)
    try {
      onDone(await api.create(categoryId, body, attachments.map((a) => a.path)))
    } catch (e: unknown) {
      onError(e)
    } finally {
      setSending(false)
    }
  }

  const tooLong = body.length > MAX_BODY
  const canSend = categoryId != null && !tooLong && (body.trim().length > 0 || attachments.length > 0) && !sending

  return (
    <div className="space-y-5">
      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-neutral-700">{t(lang, 'category')}</h2>
        {categories === null ? <Spinner label={t(lang, 'loading')} /> : (
          <div className="flex flex-wrap gap-2">
            {categories.map((c) => (
              <button key={c.id} type="button" onClick={() => setCategoryId(c.id)} aria-pressed={categoryId === c.id}
                className={`h-9 px-3.5 rounded-full border text-sm ${categoryId === c.id ? 'text-white border-transparent' : 'border-neutral-300 text-neutral-700 bg-white'}`}
                style={categoryId === c.id ? { backgroundColor: 'var(--accent)' } : undefined}>
                {c.name}
              </button>
            ))}
          </div>
        )}
      </section>
      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-neutral-700">{t(lang, 'content')}</h2>
        <textarea rows={7} value={body} onChange={(e) => setBody(e.target.value)} aria-label={t(lang, 'content')}
          className="w-full rounded-lg border border-neutral-300 bg-white px-3 py-2.5 text-[15px] text-neutral-900 focus:outline-none focus:border-[color:var(--accent)]" />
        <p className={`text-xs ${tooLong ? 'text-red-600' : 'text-neutral-500'}`}>{t(lang, 'contentHint')} ({body.length.toLocaleString()}/{MAX_BODY.toLocaleString()})</p>
      </section>
      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-neutral-700">{t(lang, 'attach')}</h2>
        <AttachPicker api={api} lang={lang} items={attachments} onChange={setAttachments} onError={onError} />
        <p className="text-xs text-neutral-500">{t(lang, 'attachHint')}</p>
      </section>
      <AccentButton className="w-full" disabled={!canSend} onClick={() => void send()}>
        {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}{t(lang, 'send')}
      </AccentButton>
    </div>
  )
}

function TicketView({ api, lang, id, onError }: { api: SupportApi; lang: Lang; id: string; onError: (e: unknown) => void }) {
  const [ticket, setTicket] = useState<Ticket | null>(null)
  const [body, setBody] = useState('')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [sending, setSending] = useState(false)
  // 그림 크게 보기. 주소를 넘기지 않고 페이지 안에서 덮어 보여 준다 — 게임 안 창에서 그림 주소로 넘어가면
  // 페이지의 닫기 단추가 사라져 빠져나올 길이 없어진다.
  const [viewing, setViewing] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setTicket(await api.ticket(id, lang))
    } catch (e: unknown) {
      onError(e)
    }
  }, [api, id, lang, onError])

  useEffect(() => { void load() }, [load])

  const send = async () => {
    setSending(true)
    try {
      await api.reply(id, body, attachments.map((a) => a.path))
      setBody('')
      setAttachments([])
      await load()
    } catch (e: unknown) {
      onError(e)
    } finally {
      setSending(false)
    }
  }

  if (!ticket) return <Spinner label={t(lang, 'loading')} />
  const tooLong = body.length > MAX_BODY
  const canSend = !tooLong && (body.trim().length > 0 || attachments.length > 0) && !sending

  return (
    <div className="space-y-4">
      {viewing && (
        <div className="fixed inset-0 z-20 bg-black/90 flex items-center justify-center" onClick={() => setViewing(null)}>
          <img src={viewing} alt="" className="max-w-full max-h-full object-contain" />
          <button type="button" aria-label={t(lang, 'close')} onClick={() => setViewing(null)}
            className="absolute top-3 right-3 w-10 h-10 rounded-full bg-white/15 text-white flex items-center justify-center">
            <X className="w-5 h-5" />
          </button>
        </div>
      )}
      <div className="flex items-center gap-2">
        <StatusBadge lang={lang} status={ticket.status} />
        <span className="text-sm text-neutral-600">{ticket.category}</span>
      </div>
      <div className="space-y-3">
        {ticket.messages.map((m) => (
          <div key={m.id} className={`flex ${m.sender === 'player' ? 'justify-end' : 'justify-start'}`}>
            <div className={`max-w-[85%] rounded-2xl px-3.5 py-2.5 ${m.sender === 'player' ? 'bg-brand-soft' : 'bg-white border border-neutral-200'}`}>
              <div className="text-[11px] font-medium text-neutral-500 mb-0.5">{m.sender === 'player' ? t(lang, 'you') : t(lang, 'staff')}</div>
              {m.body && <p className="text-[15px] text-neutral-900 whitespace-pre-wrap break-words">{m.body}</p>}
              {m.attachments.length > 0 && (
                <div className="flex flex-wrap gap-2 mt-2">
                  {m.attachments.map((a) => a.url && (
                    <button key={a.path} type="button" onClick={() => setViewing(a.url)}>
                      <img src={a.url} alt="" className="w-24 h-24 object-cover rounded-lg border border-neutral-200" />
                    </button>
                  ))}
                </div>
              )}
              <div className="mt-1 text-[11px] text-neutral-400">{formatTime(lang, m.created_at)}</div>
            </div>
          </div>
        ))}
      </div>
      {ticket.status === 'closed' ? (
        <p className="rounded-lg bg-neutral-100 px-3 py-2.5 text-sm text-neutral-600">{t(lang, 'closedNote')}</p>
      ) : (
        <div className="space-y-2 pt-2 border-t border-neutral-200">
          <textarea rows={3} value={body} onChange={(e) => setBody(e.target.value)} aria-label={t(lang, 'addMessage')}
            className="w-full rounded-lg border border-neutral-300 bg-white px-3 py-2.5 text-[15px] text-neutral-900 focus:outline-none" />
          {tooLong && <p className="text-xs text-red-600">{reasonText(lang, 'support_body_too_long')}</p>}
          <AttachPicker api={api} lang={lang} items={attachments} onChange={setAttachments} onError={onError} />
          <AccentButton className="w-full" disabled={!canSend} onClick={() => void send()}>
            {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}{t(lang, 'send')}
          </AccentButton>
        </div>
      )}
    </div>
  )
}
