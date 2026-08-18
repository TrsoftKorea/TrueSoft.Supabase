// 문자열/숫자/불리언/JSON 값 하나를 입력받는 위젯 + 파싱.
// 유저 데이터 값 수정과 원격 설정 필드 값 수정이 똑같은 로직을 각자 구현하고 있었다 — 여기 하나로 모은다.

export type ValueType = 'string' | 'number' | 'boolean' | 'json'

export function detectValueType(v: unknown): ValueType {
  if (typeof v === 'boolean') return 'boolean'
  if (typeof v === 'number') return 'number'
  if (typeof v === 'string') return 'string'
  return 'json'
}

export type ParseResult = { ok: true; value: unknown } | { ok: false; error: string }

/** text/boolValue 를 실제 값으로 바꾼다. number/json 은 여기서 검증까지 한다. */
export function parseTypedValue(type: ValueType, text: string, boolValue: boolean): ParseResult {
  if (type === 'boolean') return { ok: true, value: boolValue }
  if (type === 'json') {
    if (text.trim() === '') return { ok: true, value: null }
    try {
      return { ok: true, value: JSON.parse(text) }
    } catch {
      return { ok: false, error: '올바른 JSON이 아닙니다.' }
    }
  }
  if (type === 'number') {
    const n = Number(text)
    if (text.trim() === '' || Number.isNaN(n)) return { ok: false, error: '숫자를 입력하세요.' }
    return { ok: true, value: n }
  }
  return { ok: true, value: text }
}

export function TypedValueInput({
  type,
  text,
  onTextChange,
  boolValue,
  onBoolChange,
  integer = false,
}: {
  type: ValueType
  text: string
  onTextChange: (v: string) => void
  boolValue: boolean
  onBoolChange: (v: boolean) => void
  /** number 타입일 때 스피너 단위를 1로 고정한다(정수 컬럼용). 기본은 소수 허용. */
  integer?: boolean
}) {
  if (type === 'boolean') {
    return (
      <select
        value={boolValue ? 'true' : 'false'}
        onChange={(e) => onBoolChange(e.target.value === 'true')}
        className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
      >
        <option value="true">참</option>
        <option value="false">거짓</option>
      </select>
    )
  }
  if (type === 'json') {
    return (
      <textarea
        value={text}
        onChange={(e) => onTextChange(e.target.value)}
        rows={8}
        className="w-full px-3 py-2 rounded-md border border-neutral-300 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
      />
    )
  }
  return (
    <input
      type={type === 'number' ? 'number' : 'text'}
      step={type === 'number' ? (integer ? 1 : 'any') : undefined}
      value={text}
      onChange={(e) => onTextChange(e.target.value)}
      className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
    />
  )
}
