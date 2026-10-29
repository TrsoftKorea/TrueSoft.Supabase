// 화면 글. 기기 언어가 한국어면 한국어, 그 밖은 영어.
const KO = {
  title: '고객센터',
  close: '닫기',
  myTickets: '내 문의',
  newTicket: '문의하기',
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
  back: '목록',
  you: '나',
  staff: '고객센터',
  addMessage: '덧붙일 내용',
  closedNote: '종료된 문의입니다. 더 알릴 내용이 있으면 새로 문의해 주세요.',
  banned: '계정 이용이 제한된 상태입니다. 고객센터 문의는 보낼 수 있습니다.',
  loading: '불러오는 중…',
  sessionExpired: '접속 시간이 지났습니다. 창을 닫고 다시 열어 주세요.',
  sessionMissing: '게임 안에서 고객센터를 열어 주세요.',
  sent: '문의를 보냈습니다. 답변이 오면 알려 드립니다.',
  imageOnly: '(그림)',
  retry: '다시 시도',
} as const

type Key = keyof typeof KO

const EN: Record<Key, string> = {
  title: 'Customer Center',
  close: 'Close',
  myTickets: 'My inquiries',
  newTicket: 'New inquiry',
  empty: 'You have not sent any inquiries yet.',
  status_open: 'Waiting',
  status_answered: 'Answered',
  status_closed: 'Closed',
  newReply: 'New reply',
  category: 'Category',
  content: 'Message',
  contentHint: 'Please describe what happened in detail. Up to 2,000 characters.',
  attach: 'Attach image',
  attachHint: 'You can attach up to 3 images.',
  send: 'Send',
  cancel: 'Cancel',
  back: 'List',
  you: 'You',
  staff: 'Support',
  addMessage: 'Add a message',
  closedNote: 'This inquiry is closed. Please open a new inquiry if you need more help.',
  banned: 'Your account is restricted. You can still contact support.',
  loading: 'Loading…',
  sessionExpired: 'Your session has expired. Please close this window and open it again.',
  sessionMissing: 'Please open the customer center from the game.',
  sent: 'Your inquiry has been sent. We will let you know when we reply.',
  imageOnly: '(image)',
  retry: 'Try again',
}

// 서버가 돌려주는 실패 사유 → 문구. 없는 사유는 일반 오류 문구.
const REASON_KO: Record<string, string> = {
  support_body_empty: '내용을 적거나 그림을 붙여 주세요.',
  support_body_too_long: '내용은 2,000자까지 쓸 수 있습니다.',
  support_category_invalid: '분류를 골라 주세요.',
  support_too_many_open: '답변을 기다리는 문의가 많습니다. 답변을 받은 뒤 다시 문의해 주세요.',
  support_too_fast: '잠시 후 다시 보내 주세요.',
  support_ticket_closed: '종료된 문의에는 덧붙일 수 없습니다.',
  support_ticket_not_found: '문의를 찾을 수 없습니다.',
  support_attachment_too_large: '그림이 너무 큽니다. 5MB 이하로 붙여 주세요.',
  support_attachment_type: 'PNG·JPG·WEBP 그림만 붙일 수 있습니다.',
  support_attachment_invalid: '그림을 읽지 못했습니다. 다른 그림을 붙여 주세요.',
  support_attachment_limit: '붙일 수 있는 그림 수를 넘었습니다.',
  support_attachments_invalid: '그림을 다시 붙여 주세요.',
  support_session_expired: KO.sessionExpired,
  support_session_invalid: KO.sessionExpired,
}
const REASON_EN: Record<string, string> = {
  support_body_empty: 'Please write a message or attach an image.',
  support_body_too_long: 'Messages can be up to 2,000 characters.',
  support_category_invalid: 'Please choose a category.',
  support_too_many_open: 'You have too many inquiries waiting. Please wait for a reply first.',
  support_too_fast: 'Please try again in a moment.',
  support_ticket_closed: 'You cannot add to a closed inquiry.',
  support_ticket_not_found: 'Inquiry not found.',
  support_attachment_too_large: 'The image is too large. Please attach images under 5MB.',
  support_attachment_type: 'Only PNG, JPG and WEBP images can be attached.',
  support_attachment_invalid: 'We could not read the image. Please attach a different one.',
  support_attachment_limit: 'You have reached the image limit.',
  support_attachments_invalid: 'Please attach the images again.',
  support_session_expired: EN.sessionExpired,
  support_session_invalid: EN.sessionExpired,
}

export type Lang = 'ko' | 'en'

export function pickLang(raw: string | null | undefined): Lang {
  return (raw ?? '').toLowerCase().startsWith('ko') ? 'ko' : 'en'
}

export function t(lang: Lang, key: Key): string {
  return lang === 'ko' ? KO[key] : EN[key]
}

export function reasonText(lang: Lang, reason: string): string {
  const map = lang === 'ko' ? REASON_KO : REASON_EN
  return map[reason] ?? (lang === 'ko' ? '문제가 생겼습니다. 잠시 후 다시 시도해 주세요.' : 'Something went wrong. Please try again later.')
}

export function formatTime(lang: Lang, iso: string): string {
  const d = new Date(iso)
  return d.toLocaleString(lang === 'ko' ? 'ko-KR' : 'en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}
