// 가짜 데이터 모드(--mode mock, 주소에 ?mock=1). 서버 없이 화면 모양을 확인한다. 일반 빌드에는 들어가지 않는다.
// ?banned=1 이면 차단된 계정, ?lang=en 이면 영어, ?web=1 이면 게임 밖에서 주소로 연 창(로그인 전).
// 게임 밖 로그인은 가짜 단추로 바로 된다. ?unlinked=1 이면 연결 안 된 계정, ?nologin=1 이면 로그인 ID 를 안 넣은 게임.
// 쿠폰은 WELCOME 이 성공, USED 가 이미 사용, 그 밖은 틀린 번호.
// 계정 삭제(&view=delete): ?noapple=1 이면 구글 단추만, ?scheduled=1 이면 이미 예약됨, ?guestdays=0 이면 게스트 자동 삭제 없음.
import type { Attachment, Category, Message, SupportApi, Ticket, TicketSummary } from './api'
import { ApiError } from './api'

export function createMockApi(): SupportApi {
  const params = new URLSearchParams(location.search)
  const now = Date.now()
  const ago = (min: number) => new Date(now - min * 60_000).toISOString()
  let signedIn = params.get('web') !== '1'
  let viaWeb = false
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
  const checkUser = () => {
    if (!signedIn) throw new ApiError('support_session_missing')
  }
  const noLogin = params.get('nologin') === '1'
  // 예약은 유예 일수 뒤 자정 — 실제 서버는 게임 시간대 자정, 가짜는 브라우저 시간대 자정. ?delay=14 로 일수를 바꾼다.
  const delayDays = Number(params.get('delay') ?? 7)
  // ?webdel=0 이면 웹 계정 삭제를 끈 프로젝트, ?statusfail=1 이면 상태 조회가 처음 한 번 실패(다시 시도로 풀림).
  const webOff = params.get('webdel') === '0'
  let statusFails = params.get('statusfail') === '1' ? 1 : 0
  // 약관·방침(?view=privacy·terms): 방침은 지난 버전·시행 중·시행 예정, 약관은 시행 중 하나. ?legal=none 이면 둘 다 게시 전.
  const noLegal = params.get('legal') === 'none'
  const day = (n: number) => new Date(now + n * 86_400_000).toISOString().slice(0, 10)
  const legalBody = (title: string, v: number) => `# ${title}\n\n${en ? 'Devil Slayer' : '데빌 슬레이어'} — 버전 ${v}.\n\n## 1. 수집하는 항목\n\n| 항목 | 목적 | 보관 기간 |\n|---|---|---|\n| 기기 식별자 | 게스트 계정 복구 | 탈퇴 시까지 |\n| 결제 기록 | 결제·환불 처리 | 관계 법령에 따른 기간 |\n\n## 2. 문의\n\n- 고객센터: [바로 가기](https://example.com/support)\n- 이 줄의 HTML 은 글자로 보여야 함: <img src=x onerror=alert(1)>\n\n## 3. 번호 목록(사이에 문단)\n\n1. 수집 항목\n\n앞 항목의 설명 문단.\n\n2. 이용 목적\n3. 보관 기간\n\n2026. 11. 24 시행 — 이 줄은 목록이 아니라 날짜 그대로.\n\n[https://a.example](https://b.example) — 링크 안의 주소는 한 번만 링크.\n\n**시행일부터 적용됩니다.**`
  const legalDocs = {
    privacy: [
      { version: 1, effective_date: day(-120), in_force: true },
      { version: 2, effective_date: day(-30), in_force: true },
      { version: 3, effective_date: day(7), in_force: false },
    ],
    terms: [{ version: 1, effective_date: day(-60), in_force: true }],
  }
  const deadline = () => { const d = new Date(now + (delayDays + 1) * 86_400_000); d.setHours(0, 0, 0, 0); return d.toISOString() }
  // ?scheduled=1 이면 예약됨, ?scheduled=past 면 삭제 시각이 지나 정리를 기다리는 계정.
  let scheduledAt: string | null = params.get('scheduled') === '1' ? deadline()
    : params.get('scheduled') === 'past' ? new Date(now - 10 * 60_000).toISOString() : null
  const guestDays = params.get('guestdays')

  return {
    get signedIn() { return signedIn },
    get viaWeb() { return viaWeb },
    // ?lookfail=1 이면 프로젝트 정보를 못 받음(게임 이름·로그인 단추·유예 일수 없음).
    look: () => params.get('lookfail') === '1' ? Promise.reject(new ApiError('server_error')) : wait({
      name: en ? 'Devil Slayer' : '데빌 슬레이어', color: '#7c3aed', icon: null,
      google_client_id: noLogin ? null : 'mock-google',
      apple_services_id: noLogin || params.get('noapple') === '1' ? null : 'mock-apple',
      guest_inactive_days: guestDays == null ? 365 : Number(guestDays),
      withdrawal_delay_days: delayDays,
      web_withdrawal: !webOff,
    }),
    login: async () => {
      await wait(null)
      if (params.get('unlinked') === '1') throw new ApiError('support_web_not_linked')
      signedIn = true
      viaWeb = true
      return { banned: params.get('banned') === '1', lang: en ? 'en' : 'ko' }
    },
    logout: () => { signedIn = false; viaWeb = false },
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
    me: () => wait({ banned: params.get('banned') === '1', lang: en ? 'en' : 'ko', email: viaWeb ? 'player@example.com' : null }),
    categories: (lang) => wait(categoriesFor(lang)),
    tickets: () => wait([...tickets].sort((a, b) => summary(b).last_message_at.localeCompare(summary(a).last_message_at)).map(summary)),
    ticket: (id) => {
      const x = tickets.find((y) => y.id === id)
      if (!x) return Promise.reject(new ApiError('support_ticket_not_found'))
      x.unread = false
      return wait(x)
    },
    create: async (categoryId, body, attachments) => {
      checkUser()
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
    upload: async (contentType, data) => {
      checkUser()
      const path = `mock/${crypto.randomUUID()}.jpg`
      uploads.set(path, `data:${contentType};base64,${data}`)
      return wait({ path, url: uploads.get(path)! })
    },
    coupon: async (code) => {
      await wait(null)
      checkUser()
      if (params.get('banned') === '1') throw new ApiError('support_account_restricted')
      const c = code.toUpperCase()
      if (usedCoupons.has(c)) throw new ApiError('coupon_already_used')
      if (c !== 'WELCOME') throw new ApiError('coupon_not_found')
      usedCoupons.add(c)
    },
    withdrawalStatus: async () => {
      await wait(null)
      checkUser()
      if (statusFails > 0) { statusFails--; throw new ApiError('server_error') }
      return { scheduled_at: scheduledAt }
    },
    withdrawalRequest: async () => {
      await wait(null)
      checkUser()
      if (webOff) throw new ApiError('support_withdrawal_disabled')
      if (!viaWeb) throw new ApiError('withdrawal_web_only')
      scheduledAt ??= deadline()
      return { scheduled_at: scheduledAt }
    },
    legal: async (kind, _lang, version) => {
      await wait(null)
      const docs = noLegal ? [] : legalDocs[kind]
      const inForce = docs.filter((d) => d.in_force).sort((a, b) => b.version - a.version)
      const pick = version != null ? docs.find((d) => d.version === version) : inForce[0]
      const up = docs.find((d) => !d.in_force)
      return {
        current: pick ? { version: pick.version, effective_date: pick.effective_date, body: legalBody(kind === 'privacy' ? '개인정보처리방침' : '이용약관', pick.version)
          // 방침 1판은 시행일 문장을 손으로 쓰던 옛 판 꼴 — 화면이 시행일 문장을 한 번만 보이는지 본다.
          + (kind === 'privacy' && pick.version === 1 ? `\n\n- 시행 일자: ${pick.effective_date}` : '') } : null,
        upcoming: up ? { version: up.version, effective_date: up.effective_date } : null,
        versions: inForce.map((d) => ({ version: d.version, effective_date: d.effective_date })),
      }
    },
    withdrawalCancel: async () => {
      await wait(null)
      checkUser()
      if (webOff) throw new ApiError('support_withdrawal_disabled')
      if (!viaWeb) throw new ApiError('withdrawal_web_only')
      if (!scheduledAt) throw new ApiError('withdrawal_not_scheduled')
      scheduledAt = null
    },
  }
}
