// 가짜 데이터 모드(--mode mock, 주소에 ?mock=1). 서버 없이 화면 모양을 확인한다. 일반 빌드에는 들어가지 않는다.
// ?banned=1 이면 차단된 계정으로, ?lang=en 이면 영어로 띄운다.
import type { Attachment, Category, Message, SupportApi, Ticket, TicketSummary } from './api'
import { ApiError } from './api'

export function createMockApi(): SupportApi {
  const params = new URLSearchParams(location.search)
  const now = Date.now()
  const ago = (min: number) => new Date(now - min * 60_000).toISOString()
  const en = params.get('lang') === 'en'

  const categories: Category[] = en
    ? [{ id: 1, name: 'Payment' }, { id: 2, name: 'Account' }, { id: 3, name: 'Bug report' }, { id: 4, name: 'Other' }]
    : [{ id: 1, name: '결제' }, { id: 2, name: '계정' }, { id: 3, name: '버그 신고' }, { id: 4, name: '기타' }]
  const shot = `data:image/svg+xml;utf8,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="320" height="568"><rect width="320" height="568" fill="#7c3aed"/></svg>')}`

  const tickets: Array<Ticket & { unread: boolean }> = [
    {
      id: 't2', status: 'answered', unread: true, category: categories[2]!.name, created_at: ago(1500),
      messages: [
        { id: 1, sender: 'player', body: en ? 'The game freezes on boss stage 7.' : '보스 스테이지 7에서 화면이 멈춥니다.', attachments: [{ path: 'a/1.jpg', url: shot }], created_at: ago(1500) },
        { id: 2, sender: 'admin', body: en ? 'Sorry for the trouble. It will be fixed in the next update.' : '불편을 드려 죄송합니다. 다음 업데이트에서 고쳐질 예정입니다.', attachments: [], created_at: ago(1320) },
      ],
    },
    {
      id: 't1', status: 'closed', unread: false, category: categories[3]!.name, created_at: ago(4300),
      messages: [
        { id: 1, sender: 'player', body: en ? 'How can I change my nickname?' : '닉네임은 어떻게 바꾸나요?', attachments: [], created_at: ago(4300) },
        { id: 2, sender: 'admin', body: en ? 'You can change it in Settings > Profile.' : '설정 > 프로필에서 바꿀 수 있습니다.', attachments: [], created_at: ago(4200) },
      ],
    },
  ]

  const wait = <T>(v: T) => new Promise<T>((r) => setTimeout(() => r(v), 250))
  const summary = (x: (typeof tickets)[number]): TicketSummary => ({
    id: x.id, status: x.status, unread: x.unread, category: x.category, preview: x.messages[0]?.body ?? null,
    created_at: x.created_at, last_message_at: x.messages[x.messages.length - 1]!.created_at,
  })
  const toPaths = (paths: string[]): Attachment[] => paths.map((p) => ({ path: p, url: uploads.get(p) ?? shot }))
  const uploads = new Map<string, string>()

  return {
    look: () => wait({ name: en ? 'Devil Slayer' : '데빌 슬레이어', color: '#7c3aed', icon: null }),
    me: () => wait({ banned: params.get('banned') === '1', lang: en ? 'en' : 'ko' }),
    categories: () => wait(categories),
    tickets: () => wait([...tickets].sort((a, b) => summary(b).last_message_at.localeCompare(summary(a).last_message_at)).map(summary)),
    ticket: (id) => {
      const x = tickets.find((y) => y.id === id)
      if (!x) return Promise.reject(new ApiError('support_ticket_not_found'))
      x.unread = false
      return wait(x)
    },
    create: (categoryId, body, attachments) => {
      if (!body.trim() && attachments.length === 0) return Promise.reject(new ApiError('support_body_empty'))
      const id = `t${tickets.length + 1}`
      const msg: Message = { id: 1, sender: 'player', body: body.trim(), attachments: toPaths(attachments), created_at: new Date().toISOString() }
      tickets.push({ id, status: 'open', unread: false, category: categories.find((c) => c.id === categoryId)?.name ?? null, created_at: msg.created_at, messages: [msg] })
      return wait(id)
    },
    reply: (ticketId, body, attachments) => {
      const x = tickets.find((y) => y.id === ticketId)
      if (!x) return Promise.reject(new ApiError('support_ticket_not_found'))
      if (x.status === 'closed') return Promise.reject(new ApiError('support_ticket_closed'))
      x.messages.push({ id: x.messages.length + 1, sender: 'player', body: body.trim(), attachments: toPaths(attachments), created_at: new Date().toISOString() })
      x.status = 'open'
      return wait(undefined)
    },
    upload: (contentType, data) => {
      const path = `mock/${crypto.randomUUID()}.jpg`
      uploads.set(path, `data:${contentType};base64,${data}`)
      return wait({ path, url: uploads.get(path)! })
    },
  }
}
