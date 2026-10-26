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

/** 개수·인원·점수를 천 단위 쉼표로. 숫자가 아니면 '-'. */
export function formatCount(n: number | string | null | undefined): string {
  if (n === null || n === undefined || n === '') return '-'
  const num = typeof n === 'string' ? Number(n) : n
  if (!Number.isFinite(num)) return '-'
  return num.toLocaleString('ko-KR')
}

/** 쪽 넘김 옆 건수 표기. 모든 표가 같은 말을 쓰도록 한 곳에 둔다. */
export function formatTotal(n: number | null | undefined): string {
  return `총 ${formatCount(n ?? 0)}건`
}

/**
 * 서버가 준 시각(UTC ISO)을 `<input type="datetime-local">` 값으로. 운영자 PC 시각 기준.
 * ISO 문자열을 앞 16자로 자르면 UTC 벽시계가 들어가, 저장할 때 로컬로 다시 읽혀 9시간 밀린다.
 */
export function toDatetimeLocal(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const p = (v: number) => String(v).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 보상 목록을 "아이템 이름 ×1,000, …"으로. 카탈로그에 없는 키는 키 그대로 둔다. */
export function itemsText(
  items: { key: string; count: number | string }[] | null | undefined,
  catalog: { key: string; display_name: string }[],
): string {
  if (!items || items.length === 0) return '-'
  const name = (key: string) => catalog.find((c) => c.key === key)?.display_name || key
  return items.map((it) => `${name(it.key)} ×${formatCount(it.count)}`).join(', ')
}

/** user_data_logs.diff(바뀌기 전 값)를 "필드: 이전값" 목록 한 줄로 요약합니다. */
export function diffSummary(diff: Record<string, unknown> | null | undefined): string {
  const entries = Object.entries(diff ?? {})
  if (entries.length === 0) return '—'
  return entries
    .map(([k, v]) => `${k}: 이전값 ${typeof v === 'object' ? JSON.stringify(v) : String(v)}`)
    .join(' · ')
}
