// 화면 글. 운영 콘솔의 우편·푸시 문구와 같은 13개 언어. 기기 언어(또는 아래쪽 언어 고르기)를 따르고, 없는 언어는 영어.
import { LOCALES } from './locales'

export const LANGS = ['en', 'ja', 'ko', 'zh-CN', 'zh-TW', 'de', 'fr', 'es', 'pt', 'ru', 'id', 'th', 'vi'] as const
export type Lang = (typeof LANGS)[number]

export const LANG_NAME: Record<Lang, string> = {
  en: 'English', ja: '日本語', ko: '한국어', 'zh-CN': '简体中文', 'zh-TW': '繁體中文', de: 'Deutsch', fr: 'Français',
  es: 'Español', pt: 'Português', ru: 'Русский', id: 'Bahasa Indonesia', th: 'ไทย', vi: 'Tiếng Việt',
}

export const KO = {
  title: '고객센터',
  close: '닫기',
  back: '뒤로',
  heroTitle: '무엇을 도와드릴까요?',
  search: '검색',
  searchResults: '검색 결과 {n}건',
  noResults: '찾는 내용이 없습니다. 원하는 답이 없으면 문의해 주세요.',
  help: '사용자 문의',
  faqTitle: '자주 묻는 질문',
  all: '전체',
  newTicket: '문의하기',
  newTicketDesc: '궁금한 점이나 문제를 알려 주세요',
  coupon: '쿠폰 등록',
  couponDesc: '쿠폰 번호로 보상을 받습니다',
  myTickets: '내 문의',
  myTicketsDesc: '보낸 문의와 답변을 봅니다',
  empty: '아직 보낸 문의가 없습니다.',
  status_open: '답변 대기',
  status_answered: '답변 완료',
  status_closed: '종료',
  newReply: '새 답변',
  category: '분류',
  content: '내용',
  contentHint: '어떤 일이 있었는지 자세히 적어 주세요. 2,000자까지 쓸 수 있습니다.',
  attach: '그림 첨부',
  attachHint: '그림을 3장까지 붙일 수 있습니다.',
  send: '보내기',
  cancel: '취소',
  you: '나',
  staff: '고객센터',
  addMessage: '덧붙일 내용',
  closedNote: '종료된 문의입니다. 더 알릴 내용이 있으면 새로 문의해 주세요.',
  banned: '계정 이용이 제한된 상태입니다. 고객센터 문의는 보낼 수 있습니다.',
  loading: '불러오는 중…',
  sessionExpired: '접속 시간이 지났습니다. 창을 닫고 다시 열어 주세요.',
  badLink: '고객센터 주소가 올바르지 않습니다. 게임 안에서 고객센터를 열어 주세요.',
  sent: '문의를 보냈습니다. 답변이 오면 알려 드립니다.',
  sentWeb: '문의를 보냈습니다. 답변은 게임 안 고객센터에서 확인할 수 있습니다.',
  imageOnly: '(그림)',
  faqForCategory: '문의하기 전에 확인해 보세요',
  userId: '유저 ID',
  userIdHint: '게임 안에서 확인할 수 있는 유저 ID를 적어 주세요.',
  couponCode: '쿠폰 번호',
  couponDone: '쿠폰을 등록했습니다. 보상은 게임 우편함으로 들어갑니다.',
  privacyTitle: '개인정보 수집 및 이용 동의',
  privacyBody: '문의 상담과 답변을 위해 유저 ID, 기기·브라우저 정보, 게임 서버·닉네임을 수집하며 이 목적에만 씁니다.',
  privacyAgree: '개인정보 수집 및 이용에 동의합니다.',
  language: '언어',
  genericError: '문제가 생겼습니다. 잠시 후 다시 시도해 주세요.',
} as const

export type Key = keyof typeof KO

export const REASON_KEYS = [
  'support_body_empty', 'support_body_too_long', 'support_category_invalid', 'support_too_many_open', 'support_too_fast',
  'support_ticket_closed', 'support_ticket_not_found', 'support_attachment_too_large', 'support_attachment_type',
  'support_attachment_invalid', 'support_attachment_limit', 'support_attachments_invalid', 'support_user_not_found',
  'support_user_id_required', 'support_account_restricted', 'coupon_not_found', 'coupon_inactive', 'coupon_expired',
  'coupon_already_used', 'coupon_exhausted',
] as const
export type ReasonKey = (typeof REASON_KEYS)[number]

export const REASON_KO: Record<ReasonKey, string> = {
  support_body_empty: '내용을 적거나 그림을 붙여 주세요.',
  support_body_too_long: '내용은 2,000자까지 쓸 수 있습니다.',
  support_category_invalid: '분류를 골라 주세요.',
  support_too_many_open: '답변을 기다리는 문의가 많습니다. 답변을 받은 뒤 다시 문의해 주세요.',
  support_too_fast: '요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.',
  support_ticket_closed: '종료된 문의에는 덧붙일 수 없습니다.',
  support_ticket_not_found: '문의를 찾을 수 없습니다.',
  support_attachment_too_large: '그림이 너무 큽니다. 5MB 이하로 붙여 주세요.',
  support_attachment_type: 'PNG·JPG·WEBP 그림만 붙일 수 있습니다.',
  support_attachment_invalid: '그림을 읽지 못했습니다. 다른 그림을 붙여 주세요.',
  support_attachment_limit: '붙일 수 있는 그림 수를 넘었습니다.',
  support_attachments_invalid: '그림을 다시 붙여 주세요.',
  support_user_not_found: '유저 ID를 찾을 수 없습니다. 다시 확인해 주세요.',
  support_user_id_required: '유저 ID를 먼저 적어 주세요.',
  support_account_restricted: '이용이 제한된 계정은 쿠폰을 등록할 수 없습니다.',
  coupon_not_found: '쿠폰 번호가 맞지 않습니다.',
  coupon_inactive: '지금은 쓸 수 없는 쿠폰입니다.',
  coupon_expired: '기간이 지난 쿠폰입니다.',
  coupon_already_used: '이미 사용한 쿠폰입니다.',
  coupon_exhausted: '모두 사용된 쿠폰입니다.',
}

export type Strings = Record<Key, string>
export type Reasons = Record<ReasonKey, string>

const TABLE: Record<Lang, { ui: Strings; reasons: Reasons }> = { ko: { ui: KO, reasons: REASON_KO }, ...LOCALES }

export function pickLang(raw: string | null | undefined): Lang {
  const v = (raw ?? '').trim().toLowerCase()
  if (v.startsWith('zh')) return /tw|hk|mo|hant/.test(v) ? 'zh-TW' : 'zh-CN'
  const two = v.slice(0, 2)
  return (LANGS as readonly string[]).includes(two) ? (two as Lang) : 'en'
}

export function t(lang: Lang, key: Key, vars?: Record<string, string | number>): string {
  let s = TABLE[lang].ui[key]
  if (vars) for (const [k, v] of Object.entries(vars)) s = s.replace(`{${k}}`, String(v))
  return s
}

export function reasonText(lang: Lang, reason: string): string {
  if (reason === 'support_session_expired' || reason === 'support_session_invalid') return t(lang, 'sessionExpired')
  return (TABLE[lang].reasons as Record<string, string>)[reason] ?? t(lang, 'genericError')
}

export function formatTime(lang: Lang, iso: string): string {
  return new Date(iso).toLocaleString(lang, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}
