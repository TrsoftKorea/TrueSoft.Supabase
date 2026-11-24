// 데이터 필드의 리스트·딕셔너리 타입(게임 코드의 AutoList<int>·AutoDict<K, V> 등).
// 운영 콘솔에서 정하면 유니티 클래스 생성기와 아이템 자동 지급 판정이 그대로 읽는다.
// 열거형·클래스는 이름 없이 종류만 `enum`·`class` 로 보낸다 — 이름은 생성기가 게임 쪽(CSV·클래스 파일)에서 채운다.
// 꼴 판정은 서버 ts_schema_field_type_norm 과 같다 — 가짜 데이터가 서버처럼 거절할 때 쓴다.
// 이 파일은 가짜 데이터 검사가 따로 변환해 쓰므로 다른 파일을 불러오지 않는다.

export type Shape = 'list' | 'list2d' | 'dict' | 'dict2d'

const GENERIC: Record<Shape, string> = { list: 'AutoList', list2d: 'AutoList2D', dict: 'AutoDict', dict2d: 'AutoDict2D' }
// 화면에 보이는 이름 — 저장·전송 값(AutoList…)에서 Auto 를 뗀다. 서버·생성기는 Auto 이름을 쓰니 보일 때만 바꾼다.
const DISPLAY: Record<string, string> = { AutoList: 'List', AutoList2D: 'List2D', AutoDict: 'Dictionary', AutoDict2D: 'Dictionary2D' }
// 타입 글 안의 종류 표시 — enum·class 는 C# 예약어라 실제 타입 이름과 겹치지 않는다.
const KIND_TEXT: Record<string, string> = { enum: '열거형', class: '클래스' }

export const KEY_TYPES = ['string', 'int', 'long', 'enum']
export const VALUE_TYPES = ['int', 'long', 'short', 'float', 'double', 'decimal', 'bool', 'string', 'enum', 'class']

// 화면 이름표는 code 를 schemaLabels 의 fieldTypeLabel 에 넘겨 만든다(타입 이름표를 만드는 곳은 그 한 곳).
export const SHAPES: { value: Shape; code: string }[] = (['list', 'list2d', 'dict', 'dict2d'] as const)
  .map((value) => ({ value, code: DISPLAY[GENERIC[value]]! }))
export const keyCount = (s: Shape) => (s === 'dict' ? 1 : s === 'dict2d' ? 2 : 0)

export type FieldTypeSpec = { shape: Shape; keys: string[]; value: string }

export function newSpec(shape: Shape): FieldTypeSpec {
  return { shape, keys: Array.from({ length: keyCount(shape) }, () => 'string'), value: 'int' }
}

export function buildFieldType(spec: FieldTypeSpec): string {
  return `${GENERIC[spec.shape]}<${[...spec.keys, spec.value].join(', ')}>`
}

const ID = '[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*'

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

/**
 * 저장된 타입 글을 고르기 상태로. 꼴이 틀리면 null.
 * 이름이 든 옛 값(AutoDict<Game.CropName, int>)은 키 자리면 열거형, 값 자리면 클래스로 읽는다.
 */
export function parseFieldType(t: string | null | undefined): FieldTypeSpec | null {
  const n = normFieldType(t)
  const m = n?.match(/^(AutoList2D|AutoList|AutoDict2D|AutoDict)<(.+)>$/)
  if (!m) return null
  const shape = (Object.keys(GENERIC) as Shape[]).find((s) => GENERIC[s] === m[1])!
  const parts = m[2]!.split(', ')
  return {
    shape,
    keys: parts.slice(0, -1).map((k) => (KEY_TYPES.includes(k) ? k : 'enum')),
    value: VALUE_TYPES.includes(parts.at(-1)!) ? parts.at(-1)! : 'class',
  }
}

/** 화면에 보일 타입 글자 — AutoDict<enum, int> → Dictionary<열거형, int>. 꼴이 틀린 값은 그대로. */
export function displayFieldType(t: string): string {
  const spec = parseFieldType(t)
  if (!spec) return t
  const parts = [...spec.keys, spec.value].map((p) => KIND_TEXT[p] ?? p)
  return `${DISPLAY[GENERIC[spec.shape]]}<${parts.join(', ')}>`
}

const NUMERIC = ['int', 'long', 'short', 'float', 'double', 'decimal']

/**
 * 이 타입의 원소에 우편 보상을 자동 지급할 수 있는 모양 — 서버 ts_mail_collection_kind 와 같은 규칙.
 * 1차원 리스트·딕셔너리이고 값이 숫자 종류일 때만(딕셔너리 키는 class 가 아닐 때) list·dict, 그 밖은 other.
 * 화면은 gameItems 의 elementFits(키, 모양)로 아이템마다 지급되는지 가른다.
 */
export function grantKindOf(spec: FieldTypeSpec): 'list' | 'dict' | 'other' {
  if (!NUMERIC.includes(spec.value)) return 'other'
  if (spec.shape === 'list') return 'list'
  if (spec.shape === 'dict' && spec.keys[0] !== 'class') return 'dict'
  return 'other'
}

/** 서버 오류를 화면 문구로. 모르는 오류는 그대로. */
export function fieldTypeError(message: string): string {
  if (message.includes('field_collection_type_invalid')) return '리스트·딕셔너리 타입의 꼴이 맞지 않습니다.'
  if (message.includes('field_collection_type_needs_json')) return '리스트·딕셔너리 타입은 묶음 데이터 필드에만 정할 수 있습니다.'
  return message
}
