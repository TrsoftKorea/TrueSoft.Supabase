// support-api 엣지 함수 호출. 주소의 ?p=<프로젝트 ref> 로 어느 게임인지, #t=<접속 표> 로 누구인지 받는다.
// 접속 표가 없으면(브라우저로 주소만 연 것) 로그인 없는 창이 되어 유저 ID 로 쿠폰 등록·문의 보내기만 한다.
// 접속 표는 SDK 가 본인 확인 뒤 받아 넘긴 것이다 — 주소 기록에 남지 않게 읽자마자 지우고 이 창의 저장 공간에만 둔다.
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
export type Look = { name: string | null; color: string | null; icon: string | null }
export type Me = { banned: boolean; lang: string }

export class ApiError extends Error {
  constructor(readonly reason: string) {
    super(reason)
  }
}

// signedIn — 게임 안 창(접속 표가 있음). create·upload·coupon 은 userId 를 넘기면 게임 밖 경로, 안 넘기면 접속 표로 간다.
export interface SupportApi {
  readonly signedIn: boolean
  look(): Promise<Look>
  faqs(lang: string): Promise<Faq[]>
  me(): Promise<Me>
  categories(lang: string): Promise<Category[]>
  tickets(lang: string): Promise<TicketSummary[]>
  ticket(id: string, lang: string): Promise<Ticket>
  create(categoryId: number, body: string, attachments: string[], userId?: string, lang?: string): Promise<string>
  reply(ticketId: string, body: string, attachments: string[]): Promise<void>
  upload(contentType: string, base64: string, userId?: string): Promise<Attachment>
  coupon(code: string, userId?: string): Promise<void>
}

const TOKEN_KEY = 'tb_support_token'

function readToken(): string | null {
  const hash = new URLSearchParams(location.hash.slice(1))
  const fromHash = hash.get('t')
  if (fromHash) {
    history.replaceState(null, '', location.pathname + location.search)
    try { sessionStorage.setItem(TOKEN_KEY, fromHash) } catch { /* 저장이 막힌 창이면 이번 화면에서만 쓴다 */ }
    return fromHash
  }
  try { return sessionStorage.getItem(TOKEN_KEY) } catch { return null }
}

export function createApi(): SupportApi | null {
  const params = new URLSearchParams(location.search)
  const ref = params.get('p') ?? ''
  if (!ALLOWED_REFS.has(ref)) return null
  const url = `https://${ref}.supabase.co/functions/v1/support-api`
  const token = readToken()

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

  // 쓰기 요청은 userId 가 있으면 게임 밖 경로로 간다 — 접속 표가 만료돼 화면이 게임 밖 모드로 바뀐 뒤에도
  // 화면과 같은 경로를 타게(표가 남아 있다는 것만 보고 표로 보내면 만료 오류만 되풀이된다).
  return {
    signedIn: !!token,
    look: () => call<Look>('look', {}, false),
    faqs: (lang) => call<Faq[]>('faqs', { lang }, false),
    me: () => call<Me>('me'),
    categories: (lang) => call<Category[]>('categories.public', { lang }, false),
    tickets: (lang) => call<TicketSummary[]>('tickets', { lang }),
    ticket: (id, lang) => call<Ticket>('ticket', { ticket_id: id, lang }),
    create: async (categoryId, body, attachments, userId, lang) => (userId === undefined
      ? await call<{ id: string }>('create', { category_id: categoryId, body, attachments })
      : await call<{ id: string }>('web.create', { user_id: userId, category_id: categoryId, body, attachments, lang }, false)).id,
    reply: async (ticketId, body, attachments) => { await call('reply', { ticket_id: ticketId, body, attachments }) },
    upload: (contentType, data, userId) => (userId === undefined
      ? call<Attachment>('upload', { content_type: contentType, data })
      : call<Attachment>('web.upload', { user_id: userId, content_type: contentType, data }, false)),
    coupon: async (code, userId) => {
      if (userId === undefined) await call('coupon', { code })
      else await call('web.coupon', { user_id: userId, code }, false)
    },
  }
}

/** 게임 안 창이면 닫기 단추가 이 주소로 간다 — SDK 의 웹뷰가 가로채 창을 닫는다. */
export const CLOSE_URL = 'truebase-support://close'
