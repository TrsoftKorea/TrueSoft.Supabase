const FORMATTER = new Intl.DateTimeFormat('ko-KR', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

/** ISO 문자열을 로컬 시각으로 표시합니다. 값이 없거나 깨졌으면 '-'. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '-'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '-'
  return FORMATTER.format(d)
}

/** 차단 종료 시각을 "오전/오후" 표기로 보여줍니다. 값이 없거나 깨졌으면 빈 문자열. */
export function formatBanUntil(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const yyyy = d.getFullYear()
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  const h = d.getHours()
  const minute = String(d.getMinutes()).padStart(2, '0')
  const ampm = h < 12 ? '오전' : '오후'
  const h12 = h % 12 === 0 ? 12 : h % 12
  return `${yyyy}. ${mm}. ${dd}. ${ampm} ${h12}:${minute}`
}

/** 원화 금액을 "₩1,234" 형식으로 표시합니다. 값이 없거나 숫자가 아니면 "₩0". */
export function formatKRW(n: number | string | null | undefined): string {
  if (n === null || n === undefined) return '₩0'
  const num = typeof n === 'string' ? Number(n) : n
  if (!Number.isFinite(num)) return '₩0'
  return '₩' + num.toLocaleString('ko-KR')
}
