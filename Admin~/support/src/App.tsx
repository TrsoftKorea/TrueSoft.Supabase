import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ChevronDown, ChevronLeft, ChevronRight, Gift, ImagePlus, Inbox, Loader2, MessageSquarePlus, Search, Send, X } from 'lucide-react'
import { ApiError, CLOSE_URL, type Attachment, type Category, type Faq, type Look, type Provider, type Status, type SupportApi, type Ticket, type TicketSummary } from './api'
import { formatTime, LANG_NAME, LANGS, pickLang, reasonText, t, type Key, type Lang } from './i18n'
import { shrinkImage } from './image'
import { prepareApple, renderGoogleButton, type AppleLogin } from './login'

type View =
  | { kind: 'home' } | { kind: 'new' } | { kind: 'coupon' } | { kind: 'tickets' } | { kind: 'ticket'; id: string }
  | { kind: 'login'; next: View }

const MAX_ATTACH = 3
const MAX_BODY = 2000
const LANG_KEY = 'tb_support_lang'

const STATUS_CLS: Record<Status, string> = {
  open: 'bg-amber-100 text-amber-800',
  answered: 'bg-green-100 text-green-800',
  closed: 'bg-neutral-100 text-neutral-600',
}

// 게임 안 창이면(?app=1) 닫기 단추를 보인다. 브라우저로 열었으면 브라우저 탭을 닫으면 된다.
const IN_APP = new URLSearchParams(location.search).get('app') === '1'

// 고른 언어는 창을 닫아도 기억한다. 로그인은 api.ts 가 창을 닫으면 잊는 저장 공간에 둔다(공용 PC 대비).
function readStored(key: string): string | null {
  try { return localStorage.getItem(key) } catch { return null }
}
function writeStored(key: string, value: string) {
  try { localStorage.setItem(key, value) } catch { /* 저장이 막힌 창이면 이번 화면에서만 */ }
}

function initialLang(): Lang {
  return pickLang(readStored(LANG_KEY) ?? new URLSearchParams(location.search).get('lang') ?? navigator.language)
}

/** 검색 — 띄어 쓴 낱말이 모두 질문·답·분류 이름 어딘가에 있으면 찾은 것으로 본다. */
function matches(f: Faq, category: string | undefined, query: string): boolean {
  const hay = `${f.question}\n${f.answer}\n${category ?? ''}`.toLowerCase()
  return query.toLowerCase().split(/\s+/).filter(Boolean).every((w) => hay.includes(w))
}

export function App({ api }: { api: SupportApi | null }) {
  const [lang, setLangState] = useState<Lang>(initialLang)
  // null 은 받는 중, undefined 는 받지 못함(로그인 화면이 "불러오는 중"에 멈추지 않게 가른다).
  const [look, setLook] = useState<Look | null | undefined>(null)
  const [banned, setBanned] = useState(false)
  const [expired, setExpired] = useState(false)
  const [ready, setReady] = useState(!api?.signedIn)
  const [view, setView] = useState<View>({ kind: 'home' })
  const [notice, setNotice] = useState<string | null>(null)
  const [faqs, setFaqs] = useState<Faq[] | null>(null)
  const [categories, setCategories] = useState<Category[]>([])
  // 로그인·로그아웃은 api 안의 접속 표를 바꾼다 — 화면을 다시 그리게 하는 신호.
  const [, setAuthVersion] = useState(0)
  const signedIn = !!api?.signedIn && !expired

  const setLang = (next: Lang) => { setLangState(next); writeStored(LANG_KEY, next) }

  useEffect(() => {
    if (!api) return
    void api.look().then(setLook).catch(() => setLook(undefined))
    if (!api.signedIn) return
    api.me()
      .then((me) => {
        setBanned(me.banned)
        if (!readStored(LANG_KEY) && !new URLSearchParams(location.search).get('lang')) setLangState(pickLang(me.lang))
        setReady(true)
      })
      .catch((e: unknown) => {
        const reason = e instanceof ApiError ? e.reason : 'server_error'
        // 접속 표가 끝났으면 — 이 창에서 로그인한 것이면 로그아웃해 다시 로그인하게, 게임 안 창이면 창을 다시 열게 한다.
        // 통신 오류면 그 사유를 보이고 표는 그대로 쓴다.
        if (reason === 'support_session_expired' || reason === 'support_session_invalid') {
          if (api.viaWeb) { api.logout(); setNotice(t(initialLang(), 'loginAgain')) } else setExpired(true)
        } else setNotice(reasonText(pickLang(navigator.language), reason))
        setReady(true)
      })
  }, [api])

  // FAQ·분류는 공개 정보라 본인 확인과 따로 받는다. 언어를 바꾸면 다시 받고, 늦게 온 앞 언어 응답은 버린다.
  useEffect(() => {
    if (!api) return
    let live = true
    api.faqs(lang).then((v) => { if (live) setFaqs(v) }).catch(() => { if (live) setFaqs([]) })
    api.categories(lang).then((v) => { if (live) setCategories(v) }).catch(() => { if (live) setCategories([]) })
    return () => { live = false }
  }, [api, lang])

  useEffect(() => {
    if (look?.color) document.documentElement.style.setProperty('--accent', look.color)
    document.title = `${look?.name ? `${look.name} ` : ''}${t(lang, 'title')}`
    document.documentElement.lang = lang
  }, [look, lang])

  const showNotice = useCallback((text: string | null) => {
    setNotice(text)
    if (text) window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [])

  const go = useCallback((next: View) => { setNotice(null); setView(next); window.scrollTo({ top: 0 }) }, [])

  const fail = useCallback((e: unknown) => {
    const reason = e instanceof ApiError ? e.reason : 'server_error'
    if ((reason === 'support_session_expired' || reason === 'support_session_invalid') && api?.viaWeb) {
      api.logout()
      setAuthVersion((v) => v + 1)
      setView({ kind: 'login', next: { kind: 'home' } })
      showNotice(t(lang, 'loginAgain'))
      return
    }
    if (reason === 'support_session_expired' || reason === 'support_session_invalid') setExpired(true)
    showNotice(reasonText(lang, reason))
  }, [api, lang, showNotice])

  // 로그인이 필요한 화면은 로그인부터 — 끝나면 원래 가려던 화면으로 간다.
  const open = (next: View) => go(signedIn || next.kind === 'home' ? next : { kind: 'login', next })

  const onLogin = (r: { banned: boolean }) => {
    setBanned(r.banned)
    setExpired(false)
    setAuthVersion((v) => v + 1)
    go(view.kind === 'login' ? view.next : { kind: 'home' })
  }

  const logout = () => {
    api?.logout()
    setBanned(false)
    setAuthVersion((v) => v + 1)
    go({ kind: 'home' })
  }

  const notices = (
    <>
      {banned && <Banner tone="red">{t(lang, 'banned')}</Banner>}
      {expired && <Banner tone="amber">{t(lang, 'sessionExpired')}</Banner>}
      {notice && <Banner tone="amber" onClose={() => setNotice(null)} closeLabel={t(lang, 'close')}>{notice}</Banner>}
    </>
  )

  let body: ReactNode
  if (!api) {
    body = <p className="py-16 text-center text-sm text-neutral-600">{t(lang, 'badLink')}</p>
  } else if (!ready) {
    body = <Spinner label={t(lang, 'loading')} />
  } else if (view.kind === 'home') {
    body = (
      <HomeView api={api} lang={lang} signedIn={signedIn} faqs={faqs} categories={categories} notices={notices}
        look={look} onGo={open} onError={fail} />
    )
  } else {
    const titles: Record<Exclude<View['kind'], 'home'>, Key> = {
      new: 'newTicket', coupon: 'coupon', tickets: 'myTickets', ticket: 'myTickets', login: 'login',
    }
    body = (
      <>
        <SubHeader lang={lang} title={t(lang, titles[view.kind])} onBack={() => go(view.kind === 'ticket' ? { kind: 'tickets' } : { kind: 'home' })} />
        <main className="flex-1 w-full max-w-2xl mx-auto px-4 py-5 space-y-4">
          {notices}
          {view.kind === 'login' && <LoginView api={api} lang={lang} look={look} onDone={onLogin} onError={fail} />}
          {view.kind === 'new' && (
            <NewView api={api} lang={lang} faqs={faqs ?? []} categories={categories} onError={fail}
              onDone={(id) => { go({ kind: 'ticket', id }); setNotice(t(lang, 'sent')) }} />
          )}
          {view.kind === 'coupon' && <CouponView api={api} lang={lang} onError={fail} onDone={() => showNotice(null)} />}
          {view.kind === 'tickets' && <TicketList api={api} lang={lang} onOpen={(id) => go({ kind: 'ticket', id })} onError={fail} />}
          {view.kind === 'ticket' && <TicketView api={api} lang={lang} id={view.id} onError={fail} />}
        </main>
      </>
    )
  }

  return (
    <div className="min-h-screen flex flex-col">
      <div className="flex-1 flex flex-col">{body}</div>
      <Footer lang={lang} onLang={setLang} name={look?.name ?? null} onLogout={api?.viaWeb && signedIn ? logout : undefined} />
    </div>
  )
}

function Banner({ tone, children, onClose, closeLabel }: { tone: 'red' | 'amber'; children: ReactNode; onClose?: () => void; closeLabel?: string }) {
  const cls = tone === 'red' ? 'border-red-200 bg-red-50 text-red-700' : 'border-amber-200 bg-amber-50 text-amber-800'
  return (
    <div className={`flex items-start gap-2 rounded-lg border px-3 py-2.5 text-sm ${cls}`}>
      <span className="flex-1">{children}</span>
      {onClose && <button type="button" aria-label={closeLabel} onClick={onClose}><X className="w-4 h-4" /></button>}
    </div>
  )
}

function CloseButton({ lang, className = '' }: { lang: Lang; className?: string }) {
  if (!IN_APP) return null
  return (
    <button type="button" className={`p-1.5 ${className}`} aria-label={t(lang, 'close')} onClick={() => { location.href = CLOSE_URL }}>
      <X className="w-5 h-5" />
    </button>
  )
}

function SubHeader({ lang, title, onBack }: { lang: Lang; title: string; onBack: () => void }) {
  return (
    <header className="sticky top-0 z-10 bg-white border-b border-neutral-200">
      <div className="max-w-2xl mx-auto px-3 h-14 flex items-center gap-2">
        <button type="button" className="p-1.5 -ml-1 text-neutral-600" aria-label={t(lang, 'back')} onClick={onBack}>
          <ChevronLeft className="w-5 h-5" />
        </button>
        <div className="flex-1 min-w-0 text-[15px] font-semibold text-neutral-900 truncate">{title}</div>
        <CloseButton lang={lang} className="text-neutral-500" />
      </div>
    </header>
  )
}

function Footer({ lang, onLang, name, onLogout }: { lang: Lang; onLang: (l: Lang) => void; name: string | null; onLogout: (() => void) | undefined }) {
  return (
    <footer className="mt-10 border-t border-neutral-200 bg-white">
      <div className="max-w-2xl mx-auto px-4 py-5 space-y-3">
        <nav aria-label={t(lang, 'language')} className="flex flex-wrap gap-x-4 gap-y-2">
          {LANGS.map((l) => (
            <button key={l} type="button" lang={l} onClick={() => onLang(l)} aria-current={l === lang}
              className={`text-sm ${l === lang ? 'font-semibold text-neutral-900' : 'text-neutral-500 hover:text-neutral-800'}`}>
              {LANG_NAME[l]}
            </button>
          ))}
        </nav>
        {(name || onLogout) && (
          <div className="flex items-center gap-3">
            {name && <p className="flex-1 text-xs text-neutral-400">{name}</p>}
            {onLogout && (
              <button type="button" onClick={onLogout} className="ml-auto text-xs text-neutral-500 underline hover:text-neutral-800">
                {t(lang, 'logout')}
              </button>
            )}
          </div>
        )}
      </div>
    </footer>
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

function AccentButton({ children, disabled, onClick, className = '', submit = false }: {
  children: ReactNode; disabled?: boolean; onClick?: () => void; className?: string; submit?: boolean
}) {
  return (
    <button type={submit ? 'submit' : 'button'} disabled={disabled} onClick={onClick}
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

function Field({ label, hint, children }: { label: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <section className="space-y-2">
      <h2 className="text-sm font-semibold text-neutral-700">{label}</h2>
      {children}
      {hint && <div className="text-xs text-neutral-500">{hint}</div>}
    </section>
  )
}

const INPUT_CLS = 'w-full h-11 rounded-lg border border-neutral-300 bg-white px-3 text-[15px] text-neutral-900 focus:outline-none focus:border-[color:var(--accent)]'

/** 질문을 누르면 답이 펼쳐지는 목록. 항목이 없으면 아무것도 그리지 않는다. */
function FaqList({ lang, items }: { lang: Lang; items: Faq[] }) {
  const [open, setOpen] = useState<number | null>(null)
  if (items.length === 0) return null
  return (
    <ul className="rounded-lg border border-neutral-200 bg-white divide-y divide-neutral-200">
      {items.map((f) => (
        <li key={f.id}>
          <button type="button" aria-expanded={open === f.id} onClick={() => setOpen(open === f.id ? null : f.id)}
            className="w-full flex items-center gap-2 px-3.5 py-3.5 text-left text-[15px] text-neutral-800">
            <span className="shrink-0 font-semibold" style={{ color: 'var(--accent)' }}>Q</span>
            <span className="flex-1 break-words">{f.question}</span>
            <ChevronDown className={`w-4 h-4 shrink-0 text-neutral-400 transition-transform ${open === f.id ? 'rotate-180' : ''}`} />
          </button>
          {open === f.id && (
            <p className="px-3.5 pb-4 pl-9 text-sm text-neutral-600 whitespace-pre-wrap break-words" lang={lang}>{f.answer}</p>
          )}
        </li>
      ))}
    </ul>
  )
}

function HomeView({ api, lang, signedIn, faqs, categories, notices, look, onGo, onError }: {
  api: SupportApi; lang: Lang; signedIn: boolean; faqs: Faq[] | null; categories: Category[]; notices: ReactNode
  look: Look | null | undefined; onGo: (v: View) => void; onError: (e: unknown) => void
}) {
  const [draft, setDraft] = useState('')
  const [query, setQuery] = useState('')
  const [tab, setTab] = useState<number | null>(null)
  const [unread, setUnread] = useState(0)
  const results = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!signedIn) return
    api.tickets(lang).then((rows) => setUnread(rows.filter((r) => r.unread).length)).catch(onError)
  }, [api, lang, signedIn, onError])

  const catName = useMemo(() => new Map(categories.map((c) => [c.id, c.name])), [categories])
  // FAQ 가 있는 분류만 고르기에 둔다.
  const tabs = categories.filter((c) => faqs?.some((f) => f.category_id === c.id))
  const searching = query.trim().length > 0
  const shown = (faqs ?? []).filter((f) => searching
    ? matches(f, f.category_id == null ? undefined : catName.get(f.category_id), query)
    : tab == null || f.category_id === tab)

  const submit = () => {
    setQuery(draft)
    requestAnimationFrame(() => results.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
  }

  // 로그인 없이는 못 쓰는 일들. 게임 안 창은 접속 표가 끝났으면(창을 다시 열어야 한다), 브라우저로 연 창은 이 게임에
  // 웹 로그인이 없으면 감춘다 — 누르면 막다른 화면만 나온다. 웹 로그인이 있으면 누를 때 로그인부터 한다.
  const webLogin = !IN_APP && !!(look?.google_client_id || look?.apple_services_id)
  const actions: Array<{ key: string; icon: ReactNode; title: Key; desc: Key; view: View; badge?: number }> = signedIn || webLogin
    ? [
        { key: 'new', icon: <MessageSquarePlus className="w-5 h-5" />, title: 'newTicket', desc: 'newTicketDesc', view: { kind: 'new' } },
        { key: 'coupon', icon: <Gift className="w-5 h-5" />, title: 'coupon', desc: 'couponDesc', view: { kind: 'coupon' } },
        { key: 'tickets', icon: <Inbox className="w-5 h-5" />, title: 'myTickets', desc: 'myTicketsDesc', view: { kind: 'tickets' }, badge: unread },
      ]
    : []

  return (
    <>
      <header className="relative text-white"
        style={{
          backgroundColor: 'var(--accent)',
          backgroundImage: 'linear-gradient(135deg, color-mix(in srgb, var(--accent) 70%, #000) 0%, color-mix(in srgb, var(--accent) 45%, #0f172a) 100%)',
        }}>
        <CloseButton lang={lang} className="absolute top-2 right-2 text-white/80" />
        <div className="max-w-2xl mx-auto px-4 pt-10 pb-9 flex flex-col items-center text-center">
          {look?.icon && <img src={look.icon} alt="" className="w-20 h-20 rounded-full border-4 border-white/90 bg-white object-cover shadow" />}
          {look?.name && <p className="mt-3 text-sm text-white/80">{look.name}</p>}
          <h1 className="mt-1 text-2xl font-semibold">{t(lang, 'heroTitle')}</h1>
          <form className="mt-6 w-full max-w-lg flex rounded-lg overflow-hidden shadow-sm" role="search"
            onSubmit={(e) => { e.preventDefault(); submit() }}>
            <div className="flex-1 flex items-center bg-white">
              <Search className="w-4 h-4 ml-3 shrink-0 text-neutral-400" />
              <input type="search" value={draft} aria-label={t(lang, 'search')}
                onChange={(e) => { setDraft(e.target.value); if (!e.target.value) setQuery('') }}
                className="flex-1 min-w-0 h-12 px-2.5 bg-transparent text-[15px] text-neutral-900 focus:outline-none" />
            </div>
            <button type="submit" className="px-5 h-12 bg-black/45 text-sm font-semibold text-white hover:bg-black/60">
              {t(lang, 'search')}
            </button>
          </form>
        </div>
      </header>

      <main className="flex-1 w-full max-w-2xl mx-auto px-4 py-6 space-y-7">
        {notices}
        {!searching && actions.length > 0 && (
          <section className="space-y-2.5">
            <h2 className="text-sm font-semibold" style={{ color: 'var(--accent)' }}>{t(lang, 'help')}</h2>
            <ul className={`grid gap-2.5 ${actions.length === 3 ? 'sm:grid-cols-3' : 'sm:grid-cols-2'}`}>
              {actions.map((a) => (
                <li key={a.key}>
                  <button type="button" onClick={() => onGo(a.view)}
                    className="w-full h-full flex items-center gap-3 rounded-lg border border-neutral-200 bg-white px-3.5 py-3.5 text-left active:bg-neutral-50 sm:flex-col sm:items-start">
                    <span className="relative shrink-0 w-9 h-9 rounded-full flex items-center justify-center text-white" style={{ backgroundColor: 'var(--accent)' }}>
                      {a.icon}
                      {!!a.badge && (
                        <span className="absolute -top-1 -right-1 min-w-[18px] h-[18px] px-1 rounded-full bg-red-500 text-[11px] font-semibold leading-[18px] text-center">{a.badge}</span>
                      )}
                    </span>
                    <span className="flex-1 min-w-0">
                      <span className="block text-[15px] font-medium text-neutral-900">{t(lang, a.title)}</span>
                      <span className="block mt-0.5 text-xs text-neutral-500">{t(lang, a.desc)}</span>
                    </span>
                    <ChevronRight className="w-4 h-4 shrink-0 text-neutral-400 sm:hidden" />
                  </button>
                </li>
              ))}
            </ul>
          </section>
        )}

        <section ref={results} className="space-y-3 scroll-mt-4">
          <h2 className="text-sm font-semibold" style={{ color: 'var(--accent)' }}>
            {searching ? t(lang, 'searchResults', { n: shown.length }) : t(lang, 'faqTitle')}
          </h2>
          {!searching && tabs.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {[{ id: null as number | null, name: t(lang, 'all') }, ...tabs].map((c) => (
                <button key={c.id ?? 'all'} type="button" onClick={() => setTab(c.id)} aria-pressed={tab === c.id}
                  className={`h-8 px-3 rounded-full border text-sm ${tab === c.id ? 'text-white border-transparent' : 'border-neutral-300 text-neutral-700 bg-white'}`}
                  style={tab === c.id ? { backgroundColor: 'var(--accent)' } : undefined}>
                  {c.name}
                </button>
              ))}
            </div>
          )}
          {faqs === null ? <Spinner label={t(lang, 'loading')} /> : (
            <>
              <FaqList key={`${query}|${tab}`} lang={lang} items={shown} />
              {searching && shown.length === 0 && (
                <div className="rounded-lg border border-dashed border-neutral-300 px-4 py-6 text-center space-y-3">
                  <p className="text-sm text-neutral-600">{t(lang, 'noResults')}</p>
                  {actions.length > 0 && (
                    <AccentButton onClick={() => onGo({ kind: 'new' })}>
                      <MessageSquarePlus className="w-4 h-4" />{t(lang, 'newTicket')}
                    </AccentButton>
                  )}
                </div>
              )}
            </>
          )}
        </section>
      </main>
    </>
  )
}

function TicketList({ api, lang, onOpen, onError }: {
  api: SupportApi; lang: Lang; onOpen: (id: string) => void; onError: (e: unknown) => void
}) {
  const [rows, setRows] = useState<TicketSummary[] | null>(null)

  useEffect(() => {
    api.tickets(lang).then(setRows).catch((e: unknown) => { setRows([]); onError(e) })
  }, [api, lang, onError])

  if (rows === null) return <Spinner label={t(lang, 'loading')} />
  if (rows.length === 0) return <p className="py-10 text-center text-sm text-neutral-500">{t(lang, 'empty')}</p>
  return (
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

/** 게임 밖 창의 로그인. 게임 안 창은 SDK 가 접속 표를 주므로 여기 오는 것은 그 표가 끝났을 때뿐이다. */
function LoginView({ api, lang, look, onDone, onError }: {
  api: SupportApi; lang: Lang; look: Look | null | undefined; onDone: (r: { banned: boolean }) => void; onError: (e: unknown) => void
}) {
  const googleEl = useRef<HTMLDivElement>(null)
  const appleLogin = useRef<AppleLogin | null>(null)
  const [busy, setBusy] = useState(false)
  const google = look?.google_client_id ?? null
  const apple = look?.apple_services_id ?? null
  const mock = __MOCK__ && (google?.startsWith('mock') || apple?.startsWith('mock'))

  // 화면이 다시 그려져도 구글 단추를 새로 초기화하지 않게 최신 값은 ref 로 읽는다 — 다시 초기화하면 nonce 가 바뀌어
  // 이미 열린 구글 창에서 돌아온 토큰이 서버에서 맞지 않는다.
  const latest = useRef({ api, lang, onDone, onError })
  latest.current = { api, lang, onDone, onError }

  const finish = useCallback(async (provider: Provider, idToken: string, nonce: string) => {
    const { api: a, lang: l, onDone: done, onError: err } = latest.current
    setBusy(true)
    try {
      done(await a.login(provider, idToken, nonce, l))
    } catch (e: unknown) {
      err(e)
    } finally {
      setBusy(false)
    }
  }, [])

  // 구글 단추는 구글 스크립트가 그린다. 언어를 바꾸면 그 언어로 다시 그린다.
  useEffect(() => {
    if (!google || mock || IN_APP || !googleEl.current) return
    renderGoogleButton(googleEl.current, google, lang, (token, nonce) => void finish('google', token, nonce))
      .catch(() => latest.current.onError(new ApiError('support_web_login_failed')))
  }, [google, mock, lang, finish])

  useEffect(() => {
    if (!apple || mock || IN_APP) return
    let live = true
    prepareApple(apple).then((a) => { if (live) appleLogin.current = a }).catch(() => {})
    return () => { live = false }
  }, [apple, mock])

  const onApple = async () => {
    setBusy(true)
    try {
      const r = mock ? { idToken: 'mock', rawNonce: '' } : await appleLogin.current?.signIn()
      if (r) await finish('apple', r.idToken, r.rawNonce)
      else if (r === undefined) onError(new ApiError('support_web_login_failed'))
    } catch {
      onError(new ApiError('support_web_login_failed'))
    } finally {
      setBusy(false)
    }
  }

  if (IN_APP) return <p className="py-10 text-center text-sm text-neutral-600">{t(lang, 'sessionExpired')}</p>
  if (look === undefined) return <p className="py-10 text-center text-sm text-neutral-600">{t(lang, 'genericError')}</p>
  if (!look) return <Spinner label={t(lang, 'loading')} />
  if (!google && !apple) return <p className="py-10 text-center text-sm text-neutral-600">{t(lang, 'loginUnavailable')}</p>

  return (
    <div className="py-4 flex flex-col items-center gap-5 text-center">
      <p className="text-[15px] text-neutral-800">{t(lang, 'loginDesc')}</p>
      <div className="w-full max-w-[320px] flex flex-col gap-3">
        {google && (mock
          ? <button type="button" disabled={busy} onClick={() => void finish('google', 'mock', '')}
              className="h-11 rounded border border-neutral-300 bg-white text-sm font-medium text-neutral-800">Google</button>
          : <div ref={googleEl} className="h-11 flex justify-center" />)}
        {apple && (
          <button type="button" disabled={busy} onClick={() => void onApple()}
            className="apple-signin h-11 rounded-md bg-black text-white text-[15px] font-medium inline-flex items-center justify-center gap-2 disabled:opacity-50">
            <svg viewBox="0 0 17 20" className="w-4 h-4 fill-current" aria-hidden="true">
              <path d="M14.1 10.6c0-2.6 2.1-3.8 2.2-3.9-1.2-1.8-3.1-2-3.7-2-1.6-.2-3.1.9-3.9.9-.8 0-2-.9-3.4-.9-1.7 0-3.3 1-4.2 2.6-1.8 3.1-.5 7.7 1.3 10.2.9 1.2 1.9 2.6 3.2 2.6 1.3-.1 1.8-.8 3.3-.8 1.6 0 2 .8 3.4.8 1.4 0 2.3-1.3 3.1-2.5 1-1.4 1.4-2.8 1.4-2.9-.1 0-2.7-1-2.7-4.1zM11.5 3c.7-.9 1.2-2 1.1-3.2-1 0-2.3.7-3 1.6-.7.8-1.2 2-1.1 3.1 1.1.1 2.3-.6 3-1.5z" />
            </svg>
            {t(lang, 'appleSignIn')}
          </button>
        )}
      </div>
      {busy && <Loader2 className="w-5 h-5 animate-spin text-neutral-500" />}
      <p className="text-xs text-neutral-500">{t(lang, 'loginGuest')}</p>
    </div>
  )
}

function NewView({ api, lang, faqs, categories, onDone, onError }: {
  api: SupportApi; lang: Lang; faqs: Faq[]; categories: Category[]; onDone: (id: string) => void; onError: (e: unknown) => void
}) {
  const [categoryId, setCategoryId] = useState<number | null>(null)
  const [body, setBody] = useState('')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [agree, setAgree] = useState(false)
  const [sending, setSending] = useState(false)

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
  const canSend = categoryId != null && !tooLong && agree && (body.trim().length > 0 || attachments.length > 0) && !sending

  return (
    <div className="space-y-6">
      <Field label={t(lang, 'category')}>
        <div className="flex flex-wrap gap-2">
          {categories.map((c) => (
            <button key={c.id} type="button" onClick={() => setCategoryId(c.id)} aria-pressed={categoryId === c.id}
              className={`h-9 px-3.5 rounded-full border text-sm ${categoryId === c.id ? 'text-white border-transparent' : 'border-neutral-300 text-neutral-700 bg-white'}`}
              style={categoryId === c.id ? { backgroundColor: 'var(--accent)' } : undefined}>
              {c.name}
            </button>
          ))}
        </div>
      </Field>
      {/* 고른 분류의 FAQ 를 먼저 보인다 — 답이 거기 있으면 문의할 필요가 없다. */}
      {categoryId != null && faqs.some((f) => f.category_id === categoryId) && (
        <Field label={t(lang, 'faqForCategory')}>
          <FaqList key={categoryId} lang={lang} items={faqs.filter((f) => f.category_id === categoryId)} />
        </Field>
      )}
      <Field label={t(lang, 'content')}
        hint={<span className={tooLong ? 'text-red-600' : ''}>{t(lang, 'contentHint')} ({body.length.toLocaleString()}/{MAX_BODY.toLocaleString()})</span>}>
        <textarea rows={7} value={body} onChange={(e) => setBody(e.target.value)} aria-label={t(lang, 'content')}
          className="w-full rounded-lg border border-neutral-300 bg-white px-3 py-2.5 text-[15px] text-neutral-900 focus:outline-none focus:border-[color:var(--accent)]" />
      </Field>
      <Field label={t(lang, 'attach')} hint={t(lang, 'attachHint')}>
        <AttachPicker api={api} lang={lang} items={attachments} onChange={setAttachments} onError={onError} />
      </Field>
      <section className="rounded-lg border border-neutral-200 bg-white px-3.5 py-3 space-y-2.5">
        <h2 className="text-sm font-semibold text-neutral-700">{t(lang, 'privacyTitle')}</h2>
        <p className="text-xs text-neutral-500 leading-relaxed">{t(lang, 'privacyBody')}</p>
        <label className="flex items-center gap-2 text-sm text-neutral-800">
          <input type="checkbox" checked={agree} onChange={(e) => setAgree(e.target.checked)} className="w-4 h-4 accent-[color:var(--accent)]" />
          {t(lang, 'privacyAgree')}
        </label>
      </section>
      <AccentButton className="w-full" disabled={!canSend} onClick={() => void send()}>
        {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}{t(lang, 'send')}
      </AccentButton>
    </div>
  )
}

function CouponView({ api, lang, onDone, onError }: {
  api: SupportApi; lang: Lang; onDone: () => void; onError: (e: unknown) => void
}) {
  const [code, setCode] = useState('')
  const [sending, setSending] = useState(false)
  const [done, setDone] = useState(false)

  const send = async () => {
    setSending(true)
    setDone(false)
    try {
      await api.coupon(code.trim())
      setCode('')
      setDone(true)
      onDone()
    } catch (e: unknown) {
      onError(e)
    } finally {
      setSending(false)
    }
  }

  const canSend = code.trim().length > 0 && !sending

  return (
    <form className="space-y-6" onSubmit={(e) => { e.preventDefault(); if (canSend) void send() }}>
      {done && <div className="rounded-lg border border-green-200 bg-green-50 px-3 py-2.5 text-sm text-green-800">{t(lang, 'couponDone')}</div>}
      <Field label={t(lang, 'couponCode')}>
        <input value={code} onChange={(e) => { setCode(e.target.value); setDone(false) }} aria-label={t(lang, 'couponCode')}
          autoComplete="off" autoCapitalize="characters" spellCheck={false} className={`${INPUT_CLS} uppercase tracking-wide`} />
      </Field>
      <AccentButton className="w-full" disabled={!canSend} submit>
        {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Gift className="w-4 h-4" />}{t(lang, 'send')}
      </AccentButton>
    </form>
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
