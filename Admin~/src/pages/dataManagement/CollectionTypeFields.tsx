import { inputCls } from '../../components/ui/Input'
import { fieldTypeLabel } from '../../lib/schemaLabels'
import { buildFieldType, CUSTOM, isTypeName, KEY_TYPES, VALUE_TYPES, type FieldTypeSpec, type TypePick } from '../../lib/fieldType'

/** 리스트·딕셔너리 필드의 키·값 타입 고르기. 구조(리스트·딕셔너리 등)는 부르는 쪽이 고른다. */
export function CollectionTypeFields({ spec, onChange }: { spec: FieldTypeSpec; onChange: (s: FieldTypeSpec) => void }) {
  const keyLabels = spec.shape === 'dict2d' ? ['첫째 키 타입', '둘째 키 타입'] : ['키 타입']
  const built = buildFieldType(spec)
  return (
    <>
      {spec.keys.map((k, i) => (
        <TypePickField key={i} label={keyLabels[i] ?? '키 타입'} options={KEY_TYPES} customLabel="열거형" value={k}
          onChange={(v) => onChange({ ...spec, keys: spec.keys.map((x, j) => (j === i ? v : x)) })} />
      ))}
      <TypePickField label="값 타입" options={VALUE_TYPES} customLabel="클래스·열거형" value={spec.value}
        onChange={(v) => onChange({ ...spec, value: v })} />
      {built && <p className="text-xs text-neutral-500">게임 코드 타입 <span className="font-mono text-neutral-700">{built}</span></p>}
    </>
  )
}

function TypePickField({ label, options, customLabel, value, onChange }: {
  label: string
  options: string[]
  customLabel: string
  value: TypePick
  onChange: (v: TypePick) => void
}) {
  const badName = value.pick === CUSTOM && value.custom.trim() !== '' && !isTypeName(value.custom)
  return (
    <div>
      <label className="block text-xs text-neutral-500 mb-1">{label}</label>
      <select value={value.pick} onChange={(e) => onChange({ ...value, pick: e.target.value })} className={inputCls + ' w-full'}>
        {options.map((o) => <option key={o} value={o}>{fieldTypeLabel(o)}</option>)}
        <option value={CUSTOM}>{customLabel}</option>
      </select>
      {value.pick === CUSTOM && (
        <>
          <input value={value.custom} onChange={(e) => onChange({ ...value, custom: e.target.value })}
            aria-label={`${label} 이름`} className={inputCls + ' w-full mt-1.5 font-mono'} />
          {badName
            ? <p className="mt-1 text-xs text-red-600">타입 이름 꼴이 맞지 않습니다.</p>
            : <p className="mt-1 text-xs text-neutral-500">게임 코드의 타입 이름을 넣습니다. 예: Game.CropName</p>}
        </>
      )}
    </div>
  )
}
