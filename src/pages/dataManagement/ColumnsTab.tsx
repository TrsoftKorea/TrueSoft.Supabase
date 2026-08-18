import { useEffect, useMemo, useState, useCallback } from 'react'
import { Plus, RefreshCw, X, ChevronLeft, ChevronRight, Loader2, Pencil, Trash2, Search } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { PendingChanges, type PendingItem } from '../../components/ui/PendingChanges'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { opLabel } from '../../lib/schemaLabels'

type Col = { column_name: string; data_type: string; is_nullable: string; column_default: string | null }
type DraftRow = { id: number; feature: string; action: string; object_name: string }

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

const ALL_TYPES: CSharpType[] = [
  'string', 'short', 'int', 'long', 'float', 'double', 'decimal',
  'bool', 'DateTimeOffset', 'DateTime', 'DateOnly', 'TimeOnly', 'JSON',
]

const isIntegerType = (t: CSharpType) => (['short', 'int', 'long'] as CSharpType[]).includes(t)
const isDecimalType = (t: CSharpType) => (['float', 'double', 'decimal'] as CSharpType[]).includes(t)
const isDateTimeType = (t: CSharpType) => (['DateTimeOffset', 'DateTime'] as CSharpType[]).includes(t)

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

function toPgDefault(v: string, t: CSharpType): string | null {
  if (!v) return null
  if (isDateTimeType(t) && v !== 'now()') return `'${v.replace('T', ' ')}'`
  if (t === 'DateOnly' && v !== 'CURRENT_DATE') return `'${v}'`
  if (t === 'TimeOnly') return `'${v}'`
  if (t === 'string') return `'${v.replace(/'/g, "''")}'`
  return v
}

function DefaultValueInput({ type, value, onChange }: { type: CSharpType; value: string; onChange: (v: string) => void }) {
  const inputCls = 'w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'

  if (type === 'bool') {
    return (
      <select value={value} onChange={(e) => onChange(e.target.value)} className={`${inputCls} bg-white`}>
        <option value="">지정 안 함</option>
        <option value="true">true</option>
        <option value="false">false</option>
      </select>
    )
  }
  if (isIntegerType(type) || isDecimalType(type)) {
    return <input type="number" value={value} onChange={(e) => onChange(e.target.value)} step={isIntegerType(type) ? 1 : 'any'} className={inputCls} />
  }
  if (isDateTimeType(type)) {
    return (
      <div className="space-y-1.5">
        <input
          type="datetime-local"
          value={value === 'now()' ? '' : value}
          onChange={(e) => onChange(e.target.value)}
          disabled={value === 'now()'}
          className={`${inputCls} disabled:bg-neutral-50 disabled:text-neutral-400`}
        />
        <label className="flex items-center gap-1.5 text-xs text-neutral-500 cursor-pointer">
          <input type="checkbox" checked={value === 'now()'} onChange={(e) => onChange(e.target.checked ? 'now()' : '')} className="rounded" />
          now() 사용
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
          className={`${inputCls} disabled:bg-neutral-50 disabled:text-neutral-400`}
        />
        <label className="flex items-center gap-1.5 text-xs text-neutral-500 cursor-pointer">
          <input type="checkbox" checked={value === 'CURRENT_DATE'} onChange={(e) => onChange(e.target.checked ? 'CURRENT_DATE' : '')} className="rounded" />
          CURRENT_DATE 사용
        </label>
      </div>
    )
  }
  if (type === 'TimeOnly') {
    return <input type="time" value={value} onChange={(e) => onChange(e.target.value)} className={inputCls} />
  }
  if (type === 'JSON') {
    return (
      <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2">
        <p className="text-xs text-neutral-500 leading-relaxed">JSON 필드는 기본값을 지정하지 않습니다. 시작값은 게임에서 관리됩니다.</p>
      </div>
    )
  }
  return <input type="text" value={value} onChange={(e) => onChange(e.target.value)} className={inputCls} />
}

function DeleteColumnModal({
  target, onUnauthenticated, col, onClose, onDeleted,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  col: Col
  onClose: () => void
  onDeleted: () => void
}) {
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
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold text-red-600">필드 삭제</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          <div className="rounded-md bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
            <p className="font-medium">게시하면 되돌릴 수 없습니다.</p>
            <p className="mt-1 text-red-600"><span className="font-mono font-semibold">{col.column_name}</span> 필드와 모든 데이터가 영구 삭제됩니다.</p>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">
              확인을 위해 필드명 <span className="font-mono font-semibold text-neutral-700">{col.column_name}</span> 을 입력하세요
            </label>
            <input
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-red-300 focus:border-red-400"
            />
          </div>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button
            onClick={() => { void handleDelete() }}
            disabled={loading || confirm !== col.column_name}
            className="h-9 px-4 rounded-md bg-red-500 text-white text-sm hover:bg-red-600 disabled:opacity-60"
          >
            {loading ? '담는 중…' : '변경 관리에 담기'}
          </button>
        </div>
      </div>
    </div>
  )
}

function EditColumnModal({
  target, onUnauthenticated, col, onClose, onSaved,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  col: Col
  onClose: () => void
  onSaved: () => void
}) {
  const csharpType = PG_TO_CSHARP[col.data_type]
  const [defaultValue, setDefaultValue] = useState(() => (csharpType ? fromPgDefault(col.column_default, csharpType) : ''))
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const missingDefault = csharpType ? scalarDefaultMissing(csharpType, defaultValue) : false

  const handleSubmit = async () => {
    if (!csharpType || missingDefault || loading) return
    setLoading(true); setError('')
    try {
      await callAdmin(target, 'schema.stage', {
        feature: USER_DATA_FIELD, action: 'update', objectName: col.column_name,
        params: {
          colname: col.column_name,
          // NULL 허용은 타입으로 고정 — JSON=허용, 스칼라=비허용.
          nullable: csharpType === 'JSON',
          // JSON 은 이 화면에서 기본값을 편집하지 않으니, 원래 있던 기본값을 그대로 둔다
          // (여기서 null 로 보내면 게시할 때 기존 기본값이 조용히 사라진다).
          default_sql: csharpType === 'JSON' ? col.column_default : toPgDefault(defaultValue, csharpType),
        },
      })
      onSaved()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">필드 수정 — {col.column_name}</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          <div className="flex items-center gap-2 px-3 py-2 rounded-md bg-neutral-50 border border-neutral-200 text-sm">
            <span className="font-medium text-neutral-700">{col.column_name}</span>
            <span className="text-xs text-neutral-400">{col.data_type}</span>
          </div>
          {csharpType ? (
            <div>
              <label className="block text-xs text-neutral-500 mb-1">기본값</label>
              <DefaultValueInput type={csharpType} value={defaultValue} onChange={setDefaultValue} />
              {missingDefault && <p className="mt-1 text-xs text-amber-600">기본값이 필요합니다.</p>}
            </div>
          ) : (
            <p className="text-xs text-amber-600">이 타입({col.data_type})은 여기서 수정할 수 없습니다.</p>
          )}
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button
            onClick={() => { void handleSubmit() }}
            disabled={!csharpType || loading || missingDefault}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? '담는 중…' : '변경 관리에 담기'}
          </button>
        </div>
      </div>
    </div>
  )
}

function AddColumnModal({
  target, onUnauthenticated, onClose, onAdded,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  onClose: () => void
  onAdded: () => void
}) {
  const [name, setName] = useState('')
  const [type, setType] = useState<CSharpType>('int')
  const [defaultValue, setDefaultValue] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const missingDefault = scalarDefaultMissing(type, defaultValue)

  const handleTypeChange = (t: CSharpType) => {
    setType(t)
    setDefaultValue('')
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
          nullable: type === 'JSON',
          default_sql: type === 'JSON' ? null : toPgDefault(defaultValue, type),
        },
      })
      onAdded()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">필드 추가</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          <div>
            <label className="block text-xs text-neutral-500 mb-1">필드명</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
            />
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">타입</label>
            <select
              value={type}
              onChange={(e) => handleTypeChange(e.target.value as CSharpType)}
              className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
            >
              {ALL_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">기본값</label>
            <DefaultValueInput type={type} value={defaultValue} onChange={setDefaultValue} />
            {missingDefault && <p className="mt-1 text-xs text-amber-600">기본값이 필요합니다.</p>}
          </div>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button
            onClick={() => { void handleSubmit() }}
            disabled={loading || !name.trim() || missingDefault}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? '담는 중…' : '변경 관리에 담기'}
          </button>
        </div>
      </div>
    </div>
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
  const [drafts, setDrafts] = useState<DraftRow[]>([])
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [showAdd, setShowAdd] = useState(false)
  const [editCol, setEditCol] = useState<Col | null>(null)
  const [deleteCol, setDeleteCol] = useState<Col | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const reloadCols = useCallback(async () => {
    setLoading(true)
    try {
      // created_at 은 시스템 컬럼이라 필드 목록에서 뺀다 — 게임 데이터가 아니다.
      const rows = await callAdmin<Col[]>(target, 'userData.columns')
      setCols(rows.filter((c) => c.column_name !== 'created_at'))
    } catch (e: unknown) {
      report(e, '필드 목록을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  const reloadDrafts = useCallback(async () => {
    try {
      const draft = await callAdmin<{ rows: DraftRow[] }>(target, 'schema.getDraft')
      setDrafts(draft.rows.filter((d) => d.feature === USER_DATA_FIELD))
    } catch (e: unknown) {
      report(e, '대기 중 변경을 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => {
    void reloadCols()
    void reloadDrafts()
  }, [reloadCols, reloadDrafts])

  const pendingItems: PendingItem[] = drafts.map((d) => ({ id: d.id, label: opLabel(d.feature, d.action), detail: d.object_name }))

  const discardOne = async (id: number) => {
    setBusy(true)
    try { await callAdmin(target, 'schema.discardDraft', { id }); await reloadDrafts() }
    catch (e: unknown) { report(e, '실패했습니다.') }
    finally { setBusy(false) }
  }

  const q = search.trim().toLowerCase()
  const filtered = useMemo(
    () => (q ? cols.filter((c) => c.column_name.toLowerCase().includes(q) || c.data_type.toLowerCase().includes(q)) : cols),
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

      <div className="flex items-center justify-between gap-2">
        <div className="relative w-64">
          <Search className="w-4 h-4 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full h-9 pl-9 pr-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
          />
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setShowAdd(true)}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90"
          >
            <Plus className="w-4 h-4" />필드 추가
          </button>
          <button
            onClick={() => { void reloadCols(); void reloadDrafts() }}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />새로고침
          </button>
        </div>
      </div>

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-5 py-2.5 font-medium">필드</th>
              <th className="text-left px-5 py-2.5 font-medium">타입</th>
              <th className="text-left px-5 py-2.5 font-medium">기본값</th>
              <th className="px-3 py-2.5 w-16" />
            </tr>
          </thead>
          <tbody>
            {loading || filtered.length === 0 ? (
              <TableStatusRow
                loading={loading}
                empty={filtered.length === 0}
                colSpan={4}
                emptyText={cols.length === 0 ? '필드가 없습니다.' : '검색 결과가 없습니다.'}
              />
            ) : (
              pageRows.map((c) => (
                <tr key={c.column_name} className="border-t border-neutral-100">
                  <td className="px-5 py-3 font-medium">{c.column_name}</td>
                  <td className="px-5 py-3">
                    <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">{c.data_type}</span>
                  </td>
                  <td className="px-5 py-3 text-neutral-600 font-mono text-xs">{c.column_default ?? ''}</td>
                  <td className="px-3 py-3">
                    <div className="flex items-center gap-1">
                      <button onClick={() => setEditCol(c)} className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                        <Pencil className="w-3.5 h-3.5" />
                      </button>
                      <button onClick={() => setDeleteCol(c)} className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500">
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
        {total > 0 && (
          <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
            <span>Showing {start}-{end} of {total}</span>
            <div className="flex items-center gap-1">
              <button disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50">
                <ChevronLeft className="w-3.5 h-3.5" />
              </button>
              <span className="px-2">{page} / {totalPages}</span>
              <button disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50">
                <ChevronRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        )}
      </WhiteCard>

      {loading && cols.length === 0 && (
        <div className="flex justify-center py-4"><Loader2 className="w-5 h-5 animate-spin text-neutral-300" /></div>
      )}

      {showAdd && (
        <AddColumnModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          onClose={() => setShowAdd(false)}
          onAdded={() => { setShowAdd(false); void reloadDrafts() }}
        />
      )}
      {editCol && (
        <EditColumnModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          col={editCol}
          onClose={() => setEditCol(null)}
          onSaved={() => { setEditCol(null); void reloadDrafts() }}
        />
      )}
      {deleteCol && (
        <DeleteColumnModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          col={deleteCol}
          onClose={() => setDeleteCol(null)}
          onDeleted={() => { setDeleteCol(null); void reloadDrafts() }}
        />
      )}
    </div>
  )
}
