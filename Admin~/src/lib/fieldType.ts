// 데이터 필드의 리스트·딕셔너리 타입(게임 코드의 AutoList<int>·AutoDict<K, V> 등).
// 운영 콘솔에서 정하면 유니티 클래스 생성기와 아이템 자동 지급 판정이 그대로 읽는다.
// 꼴 판정은 서버 ts_schema_field_type_norm 과 같다 — 화면은 담기 전에 막고, 가짜 데이터는 서버처럼 거절한다.

export type Shape = 'list' | 'list2d' | 'dict' | 'dict2d'

export const SHAPES: { value: Shape; label: string }[] = [
  { value: 'list', label: '리스트' },
  { value: 'list2d', label: '이중 리스트' },
  { value: 'dict', label: '딕셔너리' },
  { value: 'dict2d', label: '이중 딕셔너리' },
]

const GENERIC: Record<Shape, string> = { list: 'AutoList', list2d: 'AutoList2D', dict: 'AutoDict', dict2d: 'AutoDict2D' }
export const keyCount = (s: Shape) => (s === 'dict' ? 1 : s === 'dict2d' ? 2 : 0)

/** 고르는 타입 — 목록에 있는 이름이거나, CUSTOM 이면 custom 에 적은 이름(열거형·클래스). */
export const CUSTOM = '__custom'
export type TypePick = { pick: string; custom: string }
export const KEY_TYPES = ['string', 'int', 'long']
export const VALUE_TYPES = ['int', 'long', 'short', 'float', 'double', 'decimal', 'bool', 'string']

export type FieldTypeSpec = { shape: Shape; keys: TypePick[]; value: TypePick }

const ID = '[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*'
const ID_RE = new RegExp(`^${ID}$`)
/** 직접 적는 타입 이름의 꼴 — 점으로 이은 C# 이름(Game.CropName). 제네릭은 안 된다. */
export const isTypeName = (s: string) => ID_RE.test(s.trim())

export const pickName = (p: TypePick) => (p.pick === CUSTOM ? p.custom.trim() : p.pick)

export function newSpec(shape: Shape): FieldTypeSpec {
  return {
    shape,
    keys: Array.from({ length: keyCount(shape) }, () => ({ pick: 'string', custom: '' })),
    value: { pick: 'int', custom: '' },
  }
}

/** 고른 것을 타입 글로. 다 고르지 않았거나 이름 꼴이 틀리면 null. */
export function buildFieldType(spec: FieldTypeSpec): string | null {
  const parts = [...spec.keys, spec.value].map(pickName)
  if (parts.some((p) => !isTypeName(p))) return null
  return `${GENERIC[spec.shape]}<${parts.join(', ')}>`
}

/** 서버와 같은 정리 — 맞는 꼴이면 `AutoDict<K, V>` 꼴로, 아니면 null. */
export function normFieldType(t: string | null | undefined): string | null {
  const v = (t ?? '').replace(/\s+/g, '')
  if (!v || v.length > 300) return null
  const list = v.match(new RegExp(`^(AutoList|AutoList2D)<(${ID})>$`))
  if (list) return `${list[1]}<${list[2]}>`
  const dict = v.match(new RegExp(`^AutoDict<(${ID}),(${ID})>$`))
  if (dict) return `AutoDict<${dict[1]}, ${dict[2]}>`
  const dict2 = v.match(new RegExp(`^AutoDict2D<(${ID}),(${ID}),(${ID})>$`))
  if (dict2) return `AutoDict2D<${dict2[1]}, ${dict2[2]}, ${dict2[3]}>`
  return null
}

const toPick = (name: string, known: string[]): TypePick => (known.includes(name) ? { pick: name, custom: '' } : { pick: CUSTOM, custom: name })

/** 저장된 타입 글을 고르기 상태로. 꼴이 틀리면 null. */
export function parseFieldType(t: string | null | undefined): FieldTypeSpec | null {
  const n = normFieldType(t)
  if (!n) return null
  const m = n.match(/^(AutoList2D|AutoList|AutoDict2D|AutoDict)<(.+)>$/)
  if (!m) return null
  const shape = (Object.keys(GENERIC) as Shape[]).find((s) => GENERIC[s] === m[1])!
  const parts = m[2]!.split(', ')
  return {
    shape,
    keys: parts.slice(0, -1).map((k) => toPick(k, KEY_TYPES)),
    value: toPick(parts[parts.length - 1]!, VALUE_TYPES),
  }
}

/** 서버 오류를 화면 문구로. 모르는 오류는 그대로. */
export function fieldTypeError(message: string): string {
  if (message.includes('field_collection_type_invalid')) return '리스트·딕셔너리 타입의 꼴이 맞지 않습니다.'
  if (message.includes('field_collection_type_needs_json')) return '리스트·딕셔너리 타입은 묶음 데이터 필드에만 정할 수 있습니다.'
  return message
}
