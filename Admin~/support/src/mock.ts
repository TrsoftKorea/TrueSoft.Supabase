// 가짜 데이터 모드(--mode mock, 주소에 ?mock=1). 서버 없이 화면 모양을 확인한다. 일반 빌드에는 들어가지 않는다.
// ?banned=1 이면 차단된 계정, ?lang=en 이면 영어, ?web=1 이면 게임 밖에서 주소로 연 창(로그인 없음).
// 쿠폰은 WELCOME 이 성공, USED 가 이미 사용, 그 밖은 틀린 번호. 게임 밖 창의 유저 ID 는 1234 만 있는 것으로 본다.
import type { Attachment, Category, Message, SupportApi, Ticket, TicketSummary } from './api'
import { ApiError } from './api'

export function createMockApi(): SupportApi {
  const params = new URLSearchParams(location.search)
  const now = Date.now()
  const ago = (min: number) => new Date(now - min * 60_000).toISOString()
  const signedIn = params.get('web') !== '1'
  const isKo = (lang: string) => lang.toLowerCase().startsWith('ko')

  const categoriesFor = (lang: string): Category[] => isKo(lang)
    ? [{ id: 1, name: '결제' }, { id: 2, name: '계정' }, { id: 3, name: '버그 신고' }, { id: 4, name: '기타' }]
    : [{ id: 1, name: 'Payment' }, { id: 2, name: 'Account' }, { id: 3, name: 'Bug report' }, { id: 4, name: 'Other' }]
  const shot = `data:image/svg+xml;utf8,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="320" height="568"><rect width="320" height="568" fill="#7c3aed"/></svg>')}`
  const en = params.get('lang') === 'en'
  const cats = categoriesFor(en ? 'en' : 'ko')

  const tickets: Array<Ticket & { unread: boolean }> = [
    {
      id: 't2', status: 'answered', unread: true, category: cats[2]!.name, created_at: ago(1500),
      messages: [
        { id: 1, sender: 'player', body: en ? 'The game freezes on boss stage 7.' : '보스 스테이지 7에서 화면이 멈춥니다.', attachments: [{ path: 'a/1.jpg', url: shot }], created_at: ago(1500) },
        { id: 2, sender: 'admin', body: en ? 'Sorry for the trouble. It will be fixed in the next update.' : '불편을 드려 죄송합니다. 다음 업데이트에서 고쳐질 예정입니다.', attachments: [], created_at: ago(1320) },
      ],
    },
    {
      id: 't1', status: 'closed', unread: false, category: cats[3]!.name, created_at: ago(4300),
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
  const usedCoupons = new Set<string>(['USED'])
  const checkUser = (userId?: string) => {
    if (!signedIn && userId !== '1234') throw new ApiError('support_user_not_found')
  }

  return {
    signedIn,
    look: () => wait({ name: en ? 'Devil Slayer' : '데빌 슬레이어', color: '#7c3aed', icon: null }),
    faqs: (lang) => wait(!isKo(lang)
      ? [
          { id: 1, category_id: 1, question: 'I paid but did not get my item.', answer: 'Items are delivered to your mailbox.\nRestart the app to retry pending purchases.' },
          { id: 2, category_id: 2, question: 'Can I continue on a new device?', answer: 'Link your Google or Apple account, then sign in with it on the new device.' },
          { id: 3, category_id: 4, question: 'How can I change my nickname?', answer: 'You can change it in Settings > Profile.' },
          { id: 4, category_id: 1, question: 'Can I get a refund?', answer: 'Refunds are handled by the Google Play or App Store where you purchased.' },
        ]
      : [
          { id: 1, category_id: 1, question: '결제했는데 아이템이 안 들어왔어요.', answer: '결제 후 우편함으로 지급됩니다.\n앱을 다시 켜면 들어오지 않은 결제를 다시 확인합니다.' },
          { id: 2, category_id: 2, question: '기기를 바꾸면 진행이 이어지나요?', answer: '구글·애플 계정을 연동해 두면 새 기기에서 같은 계정으로 로그인해 이어 할 수 있습니다.' },
          { id: 3, category_id: 4, question: '닉네임은 어떻게 바꾸나요?', answer: '설정 > 프로필에서 바꿀 수 있습니다.' },
          { id: 4, category_id: 1, question: '환불을 받을 수 있나요?', answer: '환불은 결제한 구글 플레이·앱스토어에서 처리합니다.' },
          { id: 5, category_id: null, question: '쿠폰은 어디서 쓰나요?', answer: '고객센터 첫 화면의 쿠폰 등록에서 쓸 수 있습니다. 보상은 우편함으로 들어갑니다.' },
        ]),
    me: () => wait({ banned: params.get('banned') === '1', lang: en ? 'en' : 'ko' }),
    categories: (lang) => wait(categoriesFor(lang)),
    tickets: () => wait([...tickets].sort((a, b) => summary(b).last_message_at.localeCompare(summary(a).last_message_at)).map(summary)),
    ticket: (id) => {
      const x = tickets.find((y) => y.id === id)
      if (!x) return Promise.reject(new ApiError('support_ticket_not_found'))
      x.unread = false
      return wait(x)
    },
    create: async (categoryId, body, attachments, userId) => {
      checkUser(userId)
      if (!body.trim() && attachments.length === 0) throw new ApiError('support_body_empty')
      const id = `t${tickets.length + 1}`
      const msg: Message = { id: 1, sender: 'player', body: body.trim(), attachments: toPaths(attachments), created_at: new Date().toISOString() }
      tickets.push({ id, status: 'open', unread: false, category: cats.find((c) => c.id === categoryId)?.name ?? null, created_at: msg.created_at, messages: [msg] })
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
    upload: async (contentType, data, userId) => {
      checkUser(userId)
      const path = `mock/${crypto.randomUUID()}.jpg`
      uploads.set(path, `data:${contentType};base64,${data}`)
      return wait({ path, url: uploads.get(path)! })
    },
    coupon: async (code, userId) => {
      await wait(null)
      checkUser(userId)
      if (signedIn && params.get('banned') === '1') throw new ApiError('support_account_restricted')
      const c = code.toUpperCase()
      if (usedCoupons.has(c)) throw new ApiError('coupon_already_used')
      if (c !== 'WELCOME') throw new ApiError('coupon_not_found')
      usedCoupons.add(c)
    },
  }
}
