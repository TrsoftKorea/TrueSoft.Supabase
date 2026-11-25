// 개인정보처리방침·이용약관 화면의 고정 글. 본문은 운영 콘솔에서 쓰고, 여기는 제목·시행일 같은 틀만 둔다.
import type { Lang } from './i18n'

const ko = {
  legalPrivacy: '개인정보처리방침',
  legalTerms: '이용약관',
  legalAddendum: '부칙', legalAddendumTerms: '이 약관은 {date}부터 시행합니다.', legalAddendumPrivacy: '이 개인정보처리방침은 {date}부터 시행합니다.',
  legalUpcoming: '{date}부터 새 내용이 시행됩니다.',
  legalUpcomingView: '새 내용 보기',
  legalUpcomingNotice: '{date}부터 시행될 내용입니다.',
  legalOldNotice: '{date}부터 시행된 지난 내용입니다.',
  legalBackCurrent: '지금 시행 중인 내용 보기',
  legalPastTitle: '지난 내용',
  legalPastItem: '{date} 시행',
  legalNotReady: '준비 중입니다.',
}

export type LegalKey = keyof typeof ko
type LegalText = Record<LegalKey, string>

const en: LegalText = {
  legalPrivacy: 'Privacy Policy', legalTerms: 'Terms of Service', legalAddendum: 'Effective Date', legalAddendumTerms: 'These Terms of Service take effect on {date}.', legalAddendumPrivacy: 'This Privacy Policy takes effect on {date}.',
  legalUpcoming: 'An updated version takes effect on {date}.', legalUpcomingView: 'View the update',
  legalUpcomingNotice: 'This version takes effect on {date}.', legalOldNotice: 'This is a previous version, effective from {date}.',
  legalBackCurrent: 'View the current version', legalPastTitle: 'Previous versions', legalPastItem: 'Effective {date}',
  legalNotReady: 'Coming soon.',
}
const ja: LegalText = {
  legalPrivacy: 'プライバシーポリシー', legalTerms: '利用規約', legalAddendum: '附則', legalAddendumTerms: '本規約は{date}から施行します。', legalAddendumPrivacy: '本プライバシーポリシーは{date}から施行します。',
  legalUpcoming: '{date}から新しい内容が施行されます。', legalUpcomingView: '新しい内容を見る',
  legalUpcomingNotice: '{date}から施行される内容です。', legalOldNotice: '{date}から施行された以前の内容です。',
  legalBackCurrent: '現在の内容を見る', legalPastTitle: '以前の内容', legalPastItem: '{date} 施行',
  legalNotReady: '準備中です。',
}
const zhCN: LegalText = {
  legalPrivacy: '隐私政策', legalTerms: '服务条款', legalAddendum: '附则', legalAddendumTerms: '本服务条款自{date}起施行。', legalAddendumPrivacy: '本隐私政策自{date}起施行。',
  legalUpcoming: '新内容将于 {date} 生效。', legalUpcomingView: '查看新内容',
  legalUpcomingNotice: '此内容将于 {date} 生效。', legalOldNotice: '这是自 {date} 起生效的旧版内容。',
  legalBackCurrent: '查看当前内容', legalPastTitle: '历史版本', legalPastItem: '{date} 生效',
  legalNotReady: '准备中。',
}
const zhTW: LegalText = {
  legalPrivacy: '隱私權政策', legalTerms: '服務條款', legalAddendum: '附則', legalAddendumTerms: '本服務條款自{date}起施行。', legalAddendumPrivacy: '本隱私權政策自{date}起施行。',
  legalUpcoming: '新內容將於 {date} 生效。', legalUpcomingView: '查看新內容',
  legalUpcomingNotice: '此內容將於 {date} 生效。', legalOldNotice: '這是自 {date} 起生效的舊版內容。',
  legalBackCurrent: '查看目前內容', legalPastTitle: '歷史版本', legalPastItem: '{date} 生效',
  legalNotReady: '準備中。',
}
const de: LegalText = {
  legalPrivacy: 'Datenschutzerklärung', legalTerms: 'Nutzungsbedingungen', legalAddendum: 'Inkrafttreten', legalAddendumTerms: 'Diese Nutzungsbedingungen treten am {date} in Kraft.', legalAddendumPrivacy: 'Diese Datenschutzerklärung tritt am {date} in Kraft.',
  legalUpcoming: 'Eine neue Fassung gilt ab {date}.', legalUpcomingView: 'Neue Fassung ansehen',
  legalUpcomingNotice: 'Diese Fassung gilt ab {date}.', legalOldNotice: 'Dies ist eine frühere Fassung, gültig ab {date}.',
  legalBackCurrent: 'Aktuelle Fassung ansehen', legalPastTitle: 'Frühere Fassungen', legalPastItem: 'Gültig ab {date}',
  legalNotReady: 'In Vorbereitung.',
}
const fr: LegalText = {
  legalPrivacy: 'Politique de confidentialité', legalTerms: 'Conditions d’utilisation', legalAddendum: 'Entrée en vigueur', legalAddendumTerms: 'Les présentes conditions d’utilisation entrent en vigueur le {date}.', legalAddendumPrivacy: 'La présente politique de confidentialité entre en vigueur le {date}.',
  legalUpcoming: 'Une nouvelle version entre en vigueur le {date}.', legalUpcomingView: 'Voir la nouvelle version',
  legalUpcomingNotice: 'Cette version entre en vigueur le {date}.', legalOldNotice: 'Ancienne version, en vigueur à partir du {date}.',
  legalBackCurrent: 'Voir la version actuelle', legalPastTitle: 'Versions précédentes', legalPastItem: 'En vigueur le {date}',
  legalNotReady: 'En préparation.',
}
const es: LegalText = {
  legalPrivacy: 'Política de privacidad', legalTerms: 'Términos del servicio', legalAddendum: 'Entrada en vigor', legalAddendumTerms: 'Estos términos del servicio entran en vigor el {date}.', legalAddendumPrivacy: 'Esta política de privacidad entra en vigor el {date}.',
  legalUpcoming: 'Una nueva versión entra en vigor el {date}.', legalUpcomingView: 'Ver la nueva versión',
  legalUpcomingNotice: 'Esta versión entra en vigor el {date}.', legalOldNotice: 'Versión anterior, vigente desde el {date}.',
  legalBackCurrent: 'Ver la versión actual', legalPastTitle: 'Versiones anteriores', legalPastItem: 'Vigente desde el {date}',
  legalNotReady: 'En preparación.',
}
const pt: LegalText = {
  legalPrivacy: 'Política de privacidade', legalTerms: 'Termos de serviço', legalAddendum: 'Entrada em vigor', legalAddendumTerms: 'Estes termos de serviço entram em vigor em {date}.', legalAddendumPrivacy: 'Esta política de privacidade entra em vigor em {date}.',
  legalUpcoming: 'Uma nova versão entra em vigor em {date}.', legalUpcomingView: 'Ver a nova versão',
  legalUpcomingNotice: 'Esta versão entra em vigor em {date}.', legalOldNotice: 'Versão anterior, em vigor desde {date}.',
  legalBackCurrent: 'Ver a versão atual', legalPastTitle: 'Versões anteriores', legalPastItem: 'Em vigor desde {date}',
  legalNotReady: 'Em preparação.',
}
const ru: LegalText = {
  legalPrivacy: 'Политика конфиденциальности', legalTerms: 'Условия использования', legalAddendum: 'Вступление в силу', legalAddendumTerms: 'Настоящие условия использования вступают в силу {date}.', legalAddendumPrivacy: 'Настоящая политика конфиденциальности вступает в силу {date}.',
  legalUpcoming: 'Новая редакция вступает в силу {date}.', legalUpcomingView: 'Посмотреть новую редакцию',
  legalUpcomingNotice: 'Эта редакция вступает в силу {date}.', legalOldNotice: 'Это прежняя редакция, действовавшая с {date}.',
  legalBackCurrent: 'Посмотреть действующую редакцию', legalPastTitle: 'Прежние редакции', legalPastItem: 'Действует с {date}',
  legalNotReady: 'Готовится.',
}
const id: LegalText = {
  legalPrivacy: 'Kebijakan Privasi', legalTerms: 'Ketentuan Layanan', legalAddendum: 'Tanggal Berlaku', legalAddendumTerms: 'Ketentuan Layanan ini berlaku mulai {date}.', legalAddendumPrivacy: 'Kebijakan Privasi ini berlaku mulai {date}.',
  legalUpcoming: 'Versi baru berlaku mulai {date}.', legalUpcomingView: 'Lihat versi baru',
  legalUpcomingNotice: 'Versi ini berlaku mulai {date}.', legalOldNotice: 'Ini versi sebelumnya, berlaku sejak {date}.',
  legalBackCurrent: 'Lihat versi saat ini', legalPastTitle: 'Versi sebelumnya', legalPastItem: 'Berlaku sejak {date}',
  legalNotReady: 'Sedang disiapkan.',
}
const th: LegalText = {
  legalPrivacy: 'นโยบายความเป็นส่วนตัว', legalTerms: 'ข้อกำหนดการให้บริการ', legalAddendum: 'วันที่มีผลบังคับใช้', legalAddendumTerms: 'ข้อกำหนดการให้บริการนี้มีผลตั้งแต่ {date}', legalAddendumPrivacy: 'นโยบายความเป็นส่วนตัวนี้มีผลตั้งแต่ {date}',
  legalUpcoming: 'ฉบับใหม่จะมีผลตั้งแต่ {date}', legalUpcomingView: 'ดูฉบับใหม่',
  legalUpcomingNotice: 'ฉบับนี้จะมีผลตั้งแต่ {date}', legalOldNotice: 'นี่คือฉบับก่อนหน้า ซึ่งมีผลตั้งแต่ {date}',
  legalBackCurrent: 'ดูฉบับปัจจุบัน', legalPastTitle: 'ฉบับก่อนหน้า', legalPastItem: 'มีผลตั้งแต่ {date}',
  legalNotReady: 'กำลังจัดเตรียม',
}
const vi: LegalText = {
  legalPrivacy: 'Chính sách quyền riêng tư', legalTerms: 'Điều khoản dịch vụ', legalAddendum: 'Hiệu lực', legalAddendumTerms: 'Điều khoản dịch vụ này có hiệu lực từ {date}.', legalAddendumPrivacy: 'Chính sách quyền riêng tư này có hiệu lực từ {date}.',
  legalUpcoming: 'Phiên bản mới có hiệu lực từ {date}.', legalUpcomingView: 'Xem phiên bản mới',
  legalUpcomingNotice: 'Phiên bản này có hiệu lực từ {date}.', legalOldNotice: 'Đây là phiên bản trước, có hiệu lực từ {date}.',
  legalBackCurrent: 'Xem phiên bản hiện tại', legalPastTitle: 'Phiên bản trước', legalPastItem: 'Có hiệu lực từ {date}',
  legalNotReady: 'Đang chuẩn bị.',
}

export const LEGAL_LOCALES: Record<Lang, LegalText> = { ko, en, ja, 'zh-CN': zhCN, 'zh-TW': zhTW, de, fr, es, pt, ru, id, th, vi }

/** 시행일('YYYY-MM-DD', 게임 시간대 날짜)을 그 언어의 날짜 글로 — 날짜만 있으므로 시간대를 바꾸지 않고 그대로 읽는다. */
export function formatLegalDate(lang: Lang, date: string): string {
  const d = new Date(`${date}T00:00:00Z`)
  if (Number.isNaN(d.getTime())) return date
  return d.toLocaleDateString(lang, { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' })
}
