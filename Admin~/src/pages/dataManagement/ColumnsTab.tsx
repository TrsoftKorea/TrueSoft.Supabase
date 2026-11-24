import { useEffect, useMemo, useState, useCallback } from 'react'
import { Plus, Pencil, Trash2, Search } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { SYSTEM_COLS } from '../../lib/userData'
import { usePendingChanges } from '../../lib/pendingChanges'
import { TableCard } from '../../components/ui/DataTable'
import { TableFooter } from '../../components/ui/TableFooter'
import { PendingChanges, type PendingItem } from '../../components/ui/PendingChanges'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { opLabel, fieldTypeLabel, pgTypeLabel } from '../../lib/schemaLabels'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EditDialog } from '../../components/ui/EditDialog'
import { formatTotal } from '../../components/ui/format'
import { Button } from '../../components/ui/Button'
import { dangerInputCls, inputCls } from '../../components/ui/Input'
import { RefreshButton } from '../../components/ui/FilterBar'
import { elementField, elementFits, type CatalogItem } from '../../lib/gameItems'
import { StatusText, Tag } from '../../components/ui/StatusText'
import { buildFieldType, displayFieldType, fieldTypeError, grantKindOf, newSpec, parseFieldType, SHAPES, type FieldTypeSpec, type Shape } from '../../lib/fieldType'
import { CollectionTypeFields } from './CollectionTypeFields'

// field_type: 리스트·딕셔너리 타입(AutoDict<K, V> 등). 타입을 안 정한 묶음 데이터 필드·그 밖의 필드는 null, 옛 서버는 안 보낸다.
type Col = { column_name: string; data_type: string; is_nullable: string; column_default: string | null; field_type?: string | null }

const PAGE_SIZE = 10
// schema.stage 의 feature 값 — 문자열을 곳곳에 흩어 적으면 오타 하나가 조용히 변경 목록에서
// 빠지는 draft 를 만든다(스테이징은 되지만 대기 패널 필터엔 안 걸림).
const USER_DATA_FIELD = 'user_data_field'

// 원격 설정·유저 데이터 값 편집기는 JS 값을 다루지만, 여기는 컬럼(필드) 자체를 만들고
// 지우는 DDL이라 Postgres 타입을 직접 다룬다. 운영자에게는 익숙한 C# 타입명으로 보여준다.
type CSharpType =
  | 'string' | 'short' | 'int' | 'long'
  | 'float' | 'double' | 'decimal'
  | 'bool'
  | 'DateTimeOffset' | 'DateTime' | 'DateOnly' | 'TimeOnly'
  | 'JSON'

const TYPE_TO_PG: Record<CSharpType, string> = {
  string: 'text', short: 'int2', int: 'int4', long: 'int8',
  float: 'float4', double: 'float8', decimal: 'numeric',
  bool: 'boolean',
  DateTimeOffset: 'timestamptz', DateTime: 'timestamp', DateOnly: 'date', TimeOnly: 'time',
  JSON: 'jsonb',
}

const PG_TO_CSHARP: Partial<Record<string, CSharpType>> = {
  text: 'string', 'character varying': 'string', varchar: 'string',
  int2: 'short', smallint: 'short',
  int4: 'int', integer: 'int',
  int8: 'long', bigint: 'long',
  float4: 'float', real: 'float',
  float8: 'double', 'double precision': 'double',
  numeric: 'decimal',
  boolean: 'bool', bool: 'bool',
  timestamptz: 'DateTimeOffset', 'timestamp with time zone': 'DateTimeOffset',
  timestamp: 'DateTime', 'timestamp without time zone': 'DateTime',
  date: 'DateOnly',
  time: 'TimeOnly', 'time without time zone': 'TimeOnly',
  jsonb: 'JSON', json: 'JSON',
}

// 새 필드로 고를 수 있는 타입. 묶음 데이터(JSON)는 리스트·딕셔너리 구조와 안쪽 타입을 함께 정해서만 만든다.
type ScalarType = Exclude<CSharpType, 'JSON'>
const SCALAR_TYPES: ScalarType[] = [
  'string', 'short', 'int', 'long', 'float', 'double', 'decimal',
  'bool', 'DateTimeOffset', 'DateTime', 'DateOnly', 'TimeOnly',
]
const isShape = (v: string): v is Shape => SHAPES.some((s) => s.value === v)
/** 구조를 바꿔도 이미 고른 값 타입은 남긴다. */
const reshape = (prev: FieldTypeSpec | null, shape: Shape): FieldTypeSpec => ({ ...newSpec(shape), ...(prev ? { value: prev.value } : {}) })


const isIntegerType = (t: CSharpType) => (['short', 'int', 'long'] as CSharpType[]).includes(t)
const isDecimalType = (t: CSharpType) => (['float', 'double', 'decimal'] as CSharpType[]).includes(t)
const isDateTimeType = (t: CSharpType) => (['DateTimeOffset', 'DateTime'] as CSharpType[]).includes(t)

// 아이템 카탈로그 — 우편 보상 코드가 데이터 필드 이름과 같으면 받을 때 그 필드에 더해진다.
// null = 못 불러옴. 그때는 "영향 없음"으로 보이지 않게 확인하지 못했다고 알린다.
type Catalog = CatalogItem[] | null

/** 필드를 지우거나 만들면 받기가 실패하게 되는 아이템을 알리는 상자. 막지는 않는다(게임에 처리 코드가 있을 수 있다). */
function ItemImpactNote({ catalog, affected, effect }: { catalog: Catalog; affected: CatalogItem[]; effect: string }) {
  if (catalog === null) {
    return <p className="text-xs text-neutral-500">아이템 카탈로그를 불러오지 못해 우편 보상에 주는 영향을 확인하지 못했습니다.</p>
  }
  if (affected.length === 0) return null
  return (
    <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 space-y-1">
      {affected.map((c) => (
        <p key={c.key}>
          아이템 카탈로그의 <span className="font-medium">{c.display_name || c.key}</span>
          <span className="font-mono text-xs"> {c.key}</span> — {effect}
        </p>
      ))}
      <p className="text-red-600">게임에 처리 코드가 없으면 그 아이템을 받을 수 없게 됩니다.</p>
    </div>
  )
}

// 스칼라(비 JSON)는 NOT NULL로 만들므로 기본값이 반드시 필요하다(빈 값이면 ALTER ADD COLUMN 실패).
const scalarDefaultMissing = (type: CSharpType, value: string) => type !== 'JSON' && !value.trim()

function fromPgDefault(pgDefault: string | null, type: CSharpType): string {
  if (!pgDefault) return ''
  if (pgDefault === 'now()') return 'now()'
  if (pgDefault === 'CURRENT_DATE') return 'CURRENT_DATE'
  const quotedMatch = pgDefault.match(/^'([\s\S]*)'(?:::\S+)?$/)
  if (quotedMatch && quotedMatch[1] !== undefined) {
    const v = quotedMatch[1].replace(/''/g, "'")
    if (isDateTimeType(type) && v.includes(' ')) return v.replace(' ', 'T')
    return v
  }
  const castMatch = pgDefault.match(/^\(([^)]+)\)(?:::\S+)?$/)
  if (castMatch && castMatch[1] !== undefined) return castMatch[1]
  return pgDefault
}

// 목록의 기본값 칸 — DB 표기('abc'::text, now())를 운영자가 읽는 값으로.
function defaultLabel(col: Col): string {
  const t = PG_TO_CSHARP[col.data_type]
  if (!col.column_default) return ''
  if (!t) return col.column_default
  const v = fromPgDefault(col.column_default, t)
  if (v === 'now()') return '저장되는 시각'
  if (v === 'CURRENT_DATE') return '저장되는 날짜'
  if (t === 'bool') return v === 'true' ? '참' : v === 'false' ? '거짓' : v
  if (t === 'string' && v === '') return '(빈 글자)'
  if (isDateTimeType(t)) return v.replace('T', ' ')
  return v
}

function toPgDefault(v: string, t: CSharpType): string | null {
  if (!v) return null
  if (isDateTimeType(t) && v !== 'now()') return `'${v.replace('T', ' ')}'`
  if (t === 'DateOnly' && v !== 'CURRENT_DATE') return `'${v}'`
  if (t === 'TimeOnly') return `'${v}'`
  if (t === 'string') return `'${v.replace(/'/g, "''")}'`
  return v
}

function DefaultValueInput({ type, value, onChange }: { type: CSharpType; value: string; onChange: (v: string) => void }) {

  if (type === 'bool') {
    return (
      <select value={value} onChange={(e) => onChange(e.target.value)} className={`${inputCls} w-full`}>
        {value === '' && <option value="" disabled>선택 안 함</option>}
        <option value="true">참</option>
        <option value="false">거짓</option>
      </select>
    )
  }
  if (isIntegerType(type) || isDecimalType(type)) {
    return <input type="number" value={value} onChange={(e) => onChange(e.target.value)} step={isIntegerType(type) ? 1 : 'any'} className={`${inputCls} w-full`} />
  }
  if (isDateTimeType(type)) {
    return (
      <div className="space-y-1.5">
        <input
          type="datetime-local"
          value={value === 'now()' ? '' : value}
          onChange={(e) => onChange(e.target.value)}
          disabled={value === 'now()'}
          className={`${inputCls} w-full disabled:bg-neutral-50 disabled:text-neutral-400`}
        />
        <label className="flex items-center gap-1.5 text-xs text-neutral-500 cursor-pointer">
          <input type="checkbox" checked={value === 'now()'} onChange={(e) => onChange(e.target.checked ? 'now()' : '')} className="rounded" />
          저장되는 시각으로
        </label>
      </div>
    )
  }
  if (type === 'DateOnly') {
    return (
      <div className="space-y-1.5">
        <input
          type="date"
          value={value === 'CURRENT_DATE' ? '' : value}
          onChange={(e) => onChange(e.target.value)}
          disabled={value === 'CURRENT_DATE'}
          className={`${inputCls} w-full disabled:bg-neutral-50 disabled:text-neutral-400`}
        />
        <label className="flex items-center gap-1.5 text-xs text-neutral-500 cursor-pointer">
          <input type="checkbox" checked={value === 'CURRENT_DATE'} onChange={(e) => onChange(e.target.checked ? 'CURRENT_DATE' : '')} className="rounded" />
          저장되는 날짜로
        </label>
      </div>
    )
  }
  if (type === 'TimeOnly') {
    return <input type="time" value={value} onChange={(e) => onChange(e.target.value)} className={`${inputCls} w-full`} />
  }
  if (type === 'JSON') {
    return (
      <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2">
        <p className="text-xs text-neutral-500 leading-relaxed">리스트·딕셔너리 필드는 기본값을 지정하지 않습니다. 시작값은 게임에서 관리됩니다.</p>
      </div>
    )
  }
  return <input type="text" value={value} onChange={(e) => onChange(e.target.value)} className={`${inputCls} w-full`} />
}

function DeleteColumnModal({
  target, onUnauthenticated, col, catalog, onClose, onDeleted,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  col: Col
  catalog: Catalog
  onClose: () => void
  onDeleted: () => void
}) {
  // 지금 이 필드로 자동 지급되는 아이템만 — 이미 막혀 있던 것은 지워도 달라지지 않는다.
  const affected = (catalog ?? []).filter((c) => (c.key === col.column_name || elementField(c.key) === col.column_name) && c.grant_mode === 'auto')
  const [confirm, setConfirm] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const handleDelete = async () => {
    if (confirm !== col.column_name || loading) return
    setLoading(true); setError('')
    try {
      await callAdmin(target, 'schema.stage', {
        feature: USER_DATA_FIELD, action: 'drop', objectName: col.column_name, params: { colname: col.column_name },
      })
      onDeleted()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <ConfirmDialog
      open
      title="필드 삭제"
      confirmLabel="변경 관리에 담기"
      danger
      busy={loading}
      confirmDisabled={confirm !== col.column_name}
      error={error}
      onConfirm={() => { void handleDelete() }}
      onCancel={onClose}
    >
      <div className="space-y-4">
        <div className="rounded-md bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
          <p className="font-medium">게시하면 되돌릴 수 없습니다.</p>
          <p className="mt-1 text-red-600"><span className="font-mono font-semibold">{col.column_name}</span> 필드와 모든 데이터가 영구 삭제됩니다.</p>
        </div>
        <ItemImpactNote catalog={catalog} affected={affected} effect="이 필드로 자동 지급됩니다. 지우면 자동 지급이 사라집니다." />
        <div>
          <label className="block text-xs text-neutral-500 mb-1">
            확인을 위해 필드명 <span className="font-mono font-semibold text-neutral-700">{col.column_name}</span> 을 입력하세요
          </label>
          <input
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void handleDelete() }}
            className={dangerInputCls + ' w-full'}
          />
        </div>
      </div>
    </ConfirmDialog>
  )
}

function EditColumnModal({
  target, onUnauthenticated, col, catalog, onClose, onSaved,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  col: Col
  catalog: Catalog
  onClose: () => void
  onSaved: () => void
}) {
  const csharpType = PG_TO_CSHARP[col.data_type]
  const [initialDefault] = useState(() => (csharpType ? fromPgDefault(col.column_default, csharpType) : ''))
  const [defaultValue, setDefaultValue] = useState(initialDefault)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const missingDefault = csharpType ? scalarDefaultMissing(csharpType, defaultValue) : false
  // 묶음 데이터 필드는 리스트·딕셔너리 타입을 정하거나 바꾼다. 타입이 없던 필드만 "정하지 않음"으로 둘 수 있다.
  const isJson = csharpType === 'JSON'
  // 비교는 저장값을 되읽어 다시 조립한 값과 한다 — 이름이 든 옛 값(Game.CropName)은 열거형으로 읽혀,
  // 그대로 비교하면 손대지 않아도 바뀐 것으로 보이고 저장값을 덮어쓴다.
  const [initialSpec] = useState(() => parseFieldType(col.field_type))
  const initialType = initialSpec ? buildFieldType(initialSpec) : null
  const [spec, setSpec] = useState<FieldTypeSpec | null>(initialSpec)
  const nextType = spec ? buildFieldType(spec) : null
  const typeChanged = isJson && nextType !== initialType
  // 타입을 바꾸면 지금 이 필드의 원소로 자동 지급되는 아이템 중 새 모양으로는 지급되지 않는 것.
  const lostItems = typeChanged && spec
    ? (catalog ?? []).filter((c) => c.grant_mode === 'auto' && elementField(c.key) === col.column_name && !elementFits(c.key, grantKindOf(spec)))
    : []
  // 그대로면 담을 변경이 없다(JSON 필드는 기본값을 여기서 고치지 않는다).
  const unchanged = defaultValue === initialDefault && !typeChanged

  const handleSubmit = async () => {
    if (!csharpType || missingDefault || unchanged || loading) return
    setLoading(true); setError('')
    try {
      await callAdmin(target, 'schema.stage', {
        feature: USER_DATA_FIELD, action: 'update', objectName: col.column_name,
        params: {
          colname: col.column_name,
          // NULL 허용은 타입으로 고정 — JSON=허용, 스칼라=비허용.
          nullable: isJson,
          // JSON 은 이 화면에서 기본값을 편집하지 않으니, 원래 있던 기본값을 그대로 둔다
          // (여기서 null 로 보내면 게시할 때 기존 기본값이 조용히 사라진다).
          default_sql: isJson ? col.column_default : toPgDefault(defaultValue, csharpType),
          // 키가 없으면 서버가 타입을 그대로 둔다 — 바꿨을 때만 보낸다.
          ...(typeChanged ? { field_type: nextType } : {}),
        },
      })
      onSaved()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? fieldTypeError(e.message) : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <EditDialog
      error={error || undefined}
      title="필드 수정"
      description={`${col.column_name} · ${col.field_type ? displayFieldType(col.field_type) : pgTypeLabel(col.data_type)}`}
      busy={loading}
      onClose={onClose}
      footer={<>
        <Button variant="outline" onClick={onClose} disabled={loading}>취소</Button>
        <Button variant="primary"
          onClick={() => { void handleSubmit() }}
          disabled={!csharpType || loading || missingDefault || unchanged}
        >
          {loading ? '담는 중…' : '변경 관리에 담기'}
        </Button>
      </>}
    >
          {isJson && (
            <>
              <div>
                <label className="block text-xs text-neutral-500 mb-1">구조</label>
                <select
                  value={spec?.shape ?? ''}
                  onChange={(e) => setSpec(isShape(e.target.value) ? reshape(spec, e.target.value) : null)}
                  className={inputCls + ' w-full'}
                >
                  {!initialType && <option value="">정하지 않음</option>}
                  {SHAPES.map((s) => <option key={s.value} value={s.value}>{fieldTypeLabel(s.code)}</option>)}
                </select>
              </div>
              {spec && <CollectionTypeFields spec={spec} onChange={setSpec} />}
              {typeChanged && (
                <ItemImpactNote catalog={catalog} affected={lostItems} effect="이 필드의 원소로 자동 지급됩니다. 바꾼 타입으로는 자동 지급되지 않습니다." />
              )}
            </>
          )}
          {csharpType ? (
            <div>
              <label className="block text-xs text-neutral-500 mb-1">기본값</label>
              <DefaultValueInput type={csharpType} value={defaultValue} onChange={setDefaultValue} />
              {missingDefault && <p className="mt-1 text-xs text-amber-600">기본값이 필요합니다.</p>}
            </div>
          ) : (
            <p className="text-xs text-amber-600">이 타입({col.data_type})은 여기서 수정할 수 없습니다.</p>
          )}
    </EditDialog>
  )
}

function AddColumnModal({
  target, onUnauthenticated, catalog, onClose, onAdded,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  catalog: Catalog
  onClose: () => void
  onAdded: () => void
}) {
  const [name, setName] = useState('')
  // 고른 것 — 일반 타입이거나 리스트·딕셔너리 구조. 구조면 키·값 타입은 spec 에 든다.
  const [kind, setKind] = useState<ScalarType | Shape>('int')
  const [spec, setSpec] = useState<FieldTypeSpec | null>(null)
  const [defaultValue, setDefaultValue] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const collection = isShape(kind)
  const type: CSharpType = collection ? 'JSON' : kind
  const fieldType = collection && spec ? buildFieldType(spec) : null
  const missingDefault = scalarDefaultMissing(type, defaultValue)
  // 숫자가 아닌 필드를 아이템 코드와 같은 이름으로 만들면 그 아이템은 형식 다름이 된다(서버 판정은 이름 정확 일치).
  const isNumeric = isIntegerType(type) || isDecimalType(type)
  const affected = !isNumeric && name.trim() ? (catalog ?? []).filter((c) => c.key === name.trim()) : []
  // 리스트·딕셔너리 필드를 그 이름으로 만들면 원소 꼴 아이템(이름.키·이름[번호])은 고른 모양에 맞을 때만 자동 지급되고,
  // 안 맞으면 형식 다름이 된다(서버와 같은 판정 — 리스트↔[번호], 딕셔너리↔.키, 값이 숫자 종류일 때만).
  const elementItems = collection && spec && name.trim() ? (catalog ?? []).filter((c) => elementField(c.key) === name.trim()) : []
  const grantKind = spec ? grantKindOf(spec) : 'other'
  const elementOk = elementItems.filter((c) => elementFits(c.key, grantKind))
  const elementBad = elementItems.filter((c) => !elementFits(c.key, grantKind))

  const handleKindChange = (v: string) => {
    if (isShape(v)) {
      setKind(v)
      setSpec(reshape(spec, v))
      setDefaultValue('')
      return
    }
    const t = v as ScalarType
    setKind(t)
    setDefaultValue(t === 'bool' ? 'false' : '')
  }

  const handleSubmit = async () => {
    if (!name.trim() || missingDefault || loading) return
    setLoading(true); setError('')
    try {
      await callAdmin(target, 'schema.stage', {
        feature: USER_DATA_FIELD, action: 'add', objectName: name.trim(),
        params: {
          colname: name.trim(),
          coltype: TYPE_TO_PG[type],
          nullable: collection,
          default_sql: collection ? null : toPgDefault(defaultValue, type),
          ...(collection ? { field_type: fieldType } : {}),
        },
      })
      onAdded()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? fieldTypeError(e.message) : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <EditDialog
      error={error || undefined}
      title="필드 추가"
      busy={loading}
      onClose={onClose}
      footer={<>
        <Button variant="outline" onClick={onClose} disabled={loading}>취소</Button>
        <Button variant="primary"
          onClick={() => { void handleSubmit() }}
          disabled={loading || !name.trim() || missingDefault}
        >
          {loading ? '담는 중…' : '변경 관리에 담기'}
        </Button>
      </>}
    >
          <div>
            <label className="block text-xs text-neutral-500 mb-1">필드명</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className={inputCls + ' w-full'}
            />
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">타입</label>
            <select
              value={kind}
              onChange={(e) => handleKindChange(e.target.value)}
              className={inputCls + ' w-full'}
            >
              {SCALAR_TYPES.map((t) => <option key={t} value={t}>{fieldTypeLabel(t)}</option>)}
              {SHAPES.map((s) => <option key={s.value} value={s.value}>{fieldTypeLabel(s.code)}</option>)}
            </select>
          </div>
          {collection && spec && <CollectionTypeFields spec={spec} onChange={setSpec} />}
          <div>
            <label className="block text-xs text-neutral-500 mb-1">기본값</label>
            <DefaultValueInput type={type} value={defaultValue} onChange={setDefaultValue} />
            {missingDefault && <p className="mt-1 text-xs text-amber-600">기본값이 필요합니다.</p>}
          </div>
          {!isNumeric && name.trim() && (
            <ItemImpactNote catalog={catalog} affected={affected} effect="코드가 이 필드 이름과 같습니다. 숫자가 아닌 필드로 만들면 형식 다름이 됩니다." />
          )}
          {collection && name.trim() && (
            <ItemImpactNote catalog={catalog} affected={elementBad} effect="이 필드의 원소를 가리키지만 고른 타입으로는 자동 지급되지 않습니다." />
          )}
          {elementOk.length > 0 && (
            <div className="rounded-md border border-neutral-200 bg-neutral-50 px-3 py-2 text-sm text-neutral-700 space-y-1">
              {elementOk.map((c) => (
                <p key={c.key}>
                  아이템 카탈로그의 <span className="font-medium">{c.display_name || c.key}</span>
                  <span className="font-mono text-xs"> {c.key}</span> — 이 필드의 원소로 자동 지급됩니다.
                </p>
              ))}
            </div>
          )}
    </EditDialog>
  )
}

export default function ColumnsTab({
  target, onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [cols, setCols] = useState<Col[]>([])
  const [loading, setLoading] = useState(true)
  const { drafts: allDrafts, error: draftsError, reload: reloadDrafts } = usePendingChanges()
  const drafts = useMemo(() => (allDrafts ?? []).filter((d) => d.feature === USER_DATA_FIELD), [allDrafts])
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [showAdd, setShowAdd] = useState(false)
  const [editCol, setEditCol] = useState<Col | null>(null)
  const [deleteCol, setDeleteCol] = useState<Col | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  // 대기 중 변경 조회 실패(인증 실패 제외)는 공유 컨텍스트가 조용히 삼키므로, 여기로 넘어오면
  // 기존 에러 배너에 실어 보여준다.
  useEffect(() => {
    if (draftsError) setError(draftsError)
  }, [draftsError])

  const reloadCols = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      // 시스템 컬럼은 필드 목록에서 뺀다 — 게임 데이터가 아니다. userData.columns 는 대부분
      // 이미 서버에서 걸러 주지만(created_at 만 남는다), 판단 기준은 여기 하나(SYSTEM_COLS)로 통일한다.
      const rows = await callAdmin<Col[]>(target, 'userData.columns')
      setCols(rows.filter((c) => !SYSTEM_COLS.has(c.column_name)))
    } catch (e: unknown) {
      report(e, '필드 목록을 불러오지 못했습니다.')
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => {
    void reloadCols()
  }, [reloadCols])

  // 우편 보상 영향 확인용. 실패해도 화면을 막지 않고 창 안에서 "확인하지 못함"으로 알린다.
  const [catalog, setCatalog] = useState<Catalog>([])
  useEffect(() => {
    let alive = true
    callAdmin<CatalogItem[]>(target, 'gameItems.list')
      .then((rows) => { if (alive) setCatalog(rows) })
      .catch((e: unknown) => {
        if (!alive) return
        if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
        setCatalog(null)
      })
    return () => { alive = false }
  }, [target, onUnauthenticated])

  // 리스트·딕셔너리 타입을 정한 변경은 그 타입도 함께 — 같은 필드를 담아 두고 무엇을 정했는지 잊지 않게.
  const pendingItems: PendingItem[] = drafts.map((d) => {
    const ft = d.params?.['field_type']
    const typeText = 'field_type' in (d.params ?? {}) ? (typeof ft === 'string' && ft ? displayFieldType(ft) : '타입 지움') : ''
    return { id: d.id, label: opLabel(d.feature, d.action), detail: typeText ? `${d.object_name} · ${typeText}` : d.object_name }
  })

  const pendingDropNames = new Set(drafts.filter((d) => d.action === 'drop').map((d) => d.object_name))

  const discardOne = async (id: number) => {
    setBusy(true)
    try { await callAdmin(target, 'schema.discardDraft', { id }); await reloadDrafts() }
    catch (e: unknown) { report(e, '실패했습니다.') }
    finally { setBusy(false) }
  }

  const q = search.trim().toLowerCase()
  const filtered = useMemo(
    () => (q
      ? cols.filter((c) =>
          c.column_name.toLowerCase().includes(q)
          || c.data_type.toLowerCase().includes(q)
          || (c.field_type ?? '').toLowerCase().includes(q)
          || displayFieldType(c.field_type ?? '').toLowerCase().includes(q)
          || pgTypeLabel(c.data_type).toLowerCase().includes(q))
      : cols),
    [cols, q],
  )

  useEffect(() => { setPage(1) }, [q])

  const total = filtered.length
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const pageRows = useMemo(() => filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE), [filtered, page])
  const start = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const end = Math.min(page * PAGE_SIZE, total)

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <PendingChanges items={pendingItems} onDiscard={(id) => void discardOne(id)} busy={busy} />

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="relative w-64 max-w-full">
          <Search className="w-4 h-4 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className={inputCls + ' w-full pl-9'}
          />
        </div>
        <div className="flex gap-2">
          <Button variant="primary" size="compact"
            onClick={() => setShowAdd(true)}
          >
            <Plus className="w-4 h-4" />필드 추가
          </Button>
          <RefreshButton onClick={() => { void reloadCols(); void reloadDrafts() }} loading={loading} />
        </div>
      </div>

      <TableCard
        columns={[
          {
            key: 'name', header: '필드', kind: 'mono', cardTitle: true, cell: (c) => pendingDropNames.has(c.column_name)
              ? <span className="inline-flex items-center gap-1.5"><Tag tone="bad">삭제 대기</Tag>{c.column_name}</span>
              : c.column_name,
          },
          {
            // 리스트·딕셔너리 타입은 게임 코드에 쓰는 이름 그대로. 타입을 안 정한 묶음 데이터 필드는 그 사실을 드러낸다.
            key: 'type', header: '타입', kind: 'name', cell: (c) => c.field_type ? (
              <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700 font-mono">{displayFieldType(c.field_type)}</span>
            ) : (
              <span className="inline-flex items-center gap-2">
                <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">{pgTypeLabel(c.data_type)}</span>
                {PG_TO_CSHARP[c.data_type] === 'JSON' && <StatusText tone="warn">타입 미정</StatusText>}
              </span>
            ),
          },
          { key: 'default', header: '기본값', kind: 'text', cell: (c) => defaultLabel(c), full: (c) => c.column_default ?? '' },
          {
            // 삭제 대기는 대기 변경 패널에서 취소한다 — 버튼은 감추되 자리는 남겨 줄마다 폭을 맞춘다.
            key: 'actions', header: '', kind: 'actions', cell: (c) => (
              <span className={'inline-flex gap-1.5' + (pendingDropNames.has(c.column_name) ? ' invisible' : '')}>
                <button onClick={() => setEditCol(c)} title="필드 수정" aria-label="필드 수정" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                  <Pencil className="w-3.5 h-3.5" />
                </button>
                <button onClick={() => setDeleteCol(c)} title="필드 삭제" aria-label="필드 삭제" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500">
                  <Trash2 className="w-3.5 h-3.5" />
                </button>
              </span>
            ),
          },
        ]}
        rows={pageRows}
        rowKey={(c) => c.column_name}
        loading={loading}
        failed={failed}
        emptyText={cols.length === 0 ? '필드가 없습니다.' : '검색 결과가 없습니다.'}
        footer={total > 0 && (
          <TableFooter summary={<>{start}–{end} / {formatTotal(total)}</>} page={page} totalPages={totalPages} onPageChange={setPage} />
        )}
      />

      {showAdd && (
        <AddColumnModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          catalog={catalog}
          onClose={() => setShowAdd(false)}
          onAdded={() => { setShowAdd(false); void reloadDrafts() }}
        />
      )}
      {editCol && (
        <EditColumnModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          col={editCol}
          catalog={catalog}
          onClose={() => setEditCol(null)}
          onSaved={() => { setEditCol(null); void reloadDrafts() }}
        />
      )}
      {deleteCol && (
        <DeleteColumnModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          col={deleteCol}
          catalog={catalog}
          onClose={() => setDeleteCol(null)}
          onDeleted={() => { setDeleteCol(null); void reloadDrafts() }}
        />
      )}
    </div>
  )
}
