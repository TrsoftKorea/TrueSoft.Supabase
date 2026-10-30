// support-api 엣지 함수 호출. 주소의 ?p=<프로젝트 ref> 로 어느 게임인지, 접속 표로 누구인지 받는다.
// 접속 표는 둘 중 하나로 얻는다 — 게임 안 창은 SDK 가 본인 확인 뒤 주소의 #t= 로 넘기고, 브라우저로 연 창은
// 구글·애플로 로그인해 web.login 으로 받는다. 주소 기록에 남지 않게 읽자마자 지우고 이 창의 저장 공간에만 둔다.
// 받는 게임은 운영 콘솔의 게임 목록(projectTarget)뿐이다 — 아무 프로젝트나 받으면 남이 자기 서버를 물린 링크로
// 공식 주소에서 가짜 고객센터를 보여 줄 수 있다. 게임을 추가할 때는 콘솔과 같은 곳만 고치면 된다.
import { PROJECTS } from '../../src/lib/projectTarget'

const ALLOWED_REFS = new Set(
  PROJECTS.map((p) => /^https:\/\/([a-z0-9]{20})\.supabase\.co/.exec(p.url)?.[1]).filter((r): r is string => !!r),
)

export type Status = 'open' | 'answered' | 'closed'
export type Attachment = { path: string; url: string | null }
export type TicketSummary = {
  id: string; status: Status; unread: boolean; category: string | null; preview: string | null
  created_at: string; last_message_at: string
}
export type Message = { id: number; sender: 'player' | 'admin'; body: string; attachments: Attachment[]; created_at: string }
export type Ticket = { id: string; status: Status; category: string | null; created_at: string; messages: Message[] }
export type Category = { id: number; name: string }
export type Faq = { id: number; category_id: number | null; question: string; answer: string }
// google_client_id·apple_services_id 는 게임 밖 로그인 단추의 ID. 비어 있으면 그 단추를 보이지 않는다.
export type Look = {
  name: string | null; color: string | null; icon: string | null
  google_client_id?: string | null; apple_services_id?: string | null
}
export type Me = { banned: boolean; lang: string }
export type Provider = 'google' | 'apple'
export type Login = { banned: boolean; lang: string }

export class ApiError extends Error {
  constructor(readonly reason: string) {
    super(reason)
  }
}

// signedIn — 접속 표가 있음. viaWeb — 그 표를 이 창에서 로그인해 받았음(로그아웃 단추를 보인다).
export interface SupportApi {
  readonly signedIn: boolean
  readonly viaWeb: boolean
  look(): Promise<Look>
  faqs(lang: string): Promise<Faq[]>
  login(provider: Provider, idToken: string, nonce: string, lang: string): Promise<Login>
  logout(): void
  me(): Promise<Me>
  categories(lang: string): Promise<Category[]>
  tickets(lang: string): Promise<TicketSummary[]>
  ticket(id: string, lang: string): Promise<Ticket>
  create(categoryId: number, body: string, attachments: string[]): Promise<string>
  reply(ticketId: string, body: string, attachments: string[]): Promise<void>
  upload(contentType: string, base64: string): Promise<Attachment>
  coupon(code: string): Promise<void>
}

const TOKEN_KEY = 'tb_support_token'
const WEB_KEY = 'tb_support_web'

function session(): Storage | null {
  try { return sessionStorage } catch { return null }
}

function readToken(): string | null {
  const hash = new URLSearchParams(location.hash.slice(1))
  const fromHash = hash.get('t')
  if (fromHash) {
    history.replaceState(null, '', location.pathname + location.search)
    try {
      session()?.setItem(TOKEN_KEY, fromHash)
      session()?.removeItem(WEB_KEY)
    } catch { /* 저장이 막힌 창이면 이번 화면에서만 쓴다 */ }
    return fromHash
  }
  try { return session()?.getItem(TOKEN_KEY) ?? null } catch { return null }
}

export function createApi(): SupportApi | null {
  const params = new URLSearchParams(location.search)
  const ref = params.get('p') ?? ''
  if (!ALLOWED_REFS.has(ref)) return null
  const url = `https://${ref}.supabase.co/functions/v1/support-api`
  let token = readToken()
  let viaWeb = (() => { try { return !!token && session()?.getItem(WEB_KEY) === '1' } catch { return false } })()

  const call = async <T>(action: string, body: Record<string, unknown> = {}, auth = true): Promise<T> => {
    if (auth && !token) throw new ApiError('support_session_missing')
    let res: Response
    try {
      res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(auth ? { Authorization: `Support ${token}` } : {}) },
        body: JSON.stringify({ action, ...body }),
      })
    } catch {
      throw new ApiError('network_error')
    }
    const json = await res.json().catch(() => null) as { ok?: boolean; data?: T; reason?: string } | null
    if (!json?.ok) throw new ApiError(json?.reason ?? 'server_error')
    return json.data as T
  }

  return {
    get signedIn() { return !!token },
    get viaWeb() { return viaWeb },
    look: () => call<Look>('look', {}, false),
    faqs: (lang) => call<Faq[]>('faqs', { lang }, false),
    login: async (provider, idToken, nonce, lang) => {
      const r = await call<Login & { token: string }>('web.login', { provider, id_token: idToken, nonce, lang }, false)
      token = r.token
      viaWeb = true
      try {
        session()?.setItem(TOKEN_KEY, r.token)
        session()?.setItem(WEB_KEY, '1')
      } catch { /* 저장이 막힌 창이면 이번 화면에서만 */ }
      return { banned: r.banned, lang: r.lang }
    },
    logout: () => {
      token = null
      viaWeb = false
      try {
        session()?.removeItem(TOKEN_KEY)
        session()?.removeItem(WEB_KEY)
      } catch { /* 저장이 막힌 창 */ }
    },
    me: () => call<Me>('me'),
    categories: (lang) => call<Category[]>('categories.public', { lang }, false),
    tickets: (lang) => call<TicketSummary[]>('tickets', { lang }),
    ticket: (id, lang) => call<Ticket>('ticket', { ticket_id: id, lang }),
    create: async (categoryId, body, attachments) =>
      (await call<{ id: string }>('create', { category_id: categoryId, body, attachments })).id,
    reply: async (ticketId, body, attachments) => { await call('reply', { ticket_id: ticketId, body, attachments }) },
    upload: (contentType, data) => call<Attachment>('upload', { content_type: contentType, data }),
    coupon: async (code) => { await call('coupon', { code }) },
  }
}

/** 게임 안 창이면 닫기 단추가 이 주소로 간다 — SDK 의 웹뷰가 가로채 창을 닫는다. */
export const CLOSE_URL = 'truebase-support://close'
