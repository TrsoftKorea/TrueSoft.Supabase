import { CircleHelp } from 'lucide-react'
import { inputCls } from '../../components/ui/Input'
import { fieldTypeLabel } from '../../lib/schemaLabels'
import { buildFieldType, displayFieldType, KEY_TYPES, VALUE_TYPES, type FieldTypeSpec } from '../../lib/fieldType'

const NAME_NOTE = '열거형·클래스 이름은 유니티 클래스 생성기의 CSV 타입 열에 적습니다.'

/** 리스트·딕셔너리 필드의 키·값 타입 고르기. 구조(리스트·딕셔너리 등)는 부르는 쪽이 고른다. */
export function CollectionTypeFields({ spec, onChange }: { spec: FieldTypeSpec; onChange: (s: FieldTypeSpec) => void }) {
  const keyLabels = spec.shape === 'dict2d' ? ['첫째 키 타입', '둘째 키 타입'] : ['키 타입']
  const named = [...spec.keys, spec.value].some((p) => p === 'enum' || p === 'class')
  return (
    <>
      {spec.keys.map((k, i) => (
        <TypeSelect key={i} label={keyLabels[i] ?? '키 타입'} options={KEY_TYPES} value={k}
          onChange={(v) => onChange({ ...spec, keys: spec.keys.map((x, j) => (j === i ? v : x)) })} />
      ))}
      <TypeSelect label="값 타입" options={VALUE_TYPES} value={spec.value} onChange={(v) => onChange({ ...spec, value: v })} />
      <p className="text-xs text-neutral-500 inline-flex items-center gap-1.5">
        게임 코드 타입 <span className="font-mono text-neutral-700">{displayFieldType(buildFieldType(spec))}</span>
        {named && (
          <span title={NAME_NOTE} aria-label={NAME_NOTE} className="cursor-help text-neutral-400">
            <CircleHelp className="w-3.5 h-3.5" />
          </span>
        )}
      </p>
    </>
  )
}

function TypeSelect({ label, options, value, onChange }: {
  label: string
  options: string[]
  value: string
  onChange: (v: string) => void
}) {
  return (
    <div>
      <label className="block text-xs text-neutral-500 mb-1">{label}</label>
      <select value={value} onChange={(e) => onChange(e.target.value)} className={inputCls + ' w-full'}>
        {options.map((o) => <option key={o} value={o}>{fieldTypeLabel(o)}</option>)}
      </select>
    </div>
  )
}
