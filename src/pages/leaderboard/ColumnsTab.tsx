import { useEffect, useMemo, useState, useCallback } from 'react'
import { Plus, RefreshCw, X, ChevronLeft, ChevronRight, Loader2, Pencil, Trash2, Search } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { usePendingChanges } from '../../lib/pendingChanges'
import { WhiteCard } from '../../components/ui/Card'
import { PendingChanges, type PendingItem } from '../../components/ui/PendingChanges'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { opLabel } from '../../lib/schemaLabels'

type LbRow = { code: string; display_name: string; columns: string }
type Col = {
  column_name: string
  data_type: string
  is_nullable: 'YES' | 'NO' | string
  column_default: string | null
}
type AttachedRow = { column_name: string; sort_order: number }
type ColsData = { all?: Col[]; attached?: AttachedRow[] }

const PAGE_SIZE = 10

type CSharpType =
  | 'string' | 'short' | 'int' | 'long'
  | 'float' | 'double' | 'decimal'
  | 'bool'
  | 'DateTimeOffset' | 'DateTime' | 'DateOnly' | 'TimeOnly'
  | 'JSON'

const TYPE_TO_PG: Record<CSharpType, string> = {
  string: 'text',
  short: 'int2',
  int: 'int4',
  long: 'int8',
  float: 'float4',
  double: 'float8',
  decimal: 'numeric',
  bool: 'boolean',
  DateTimeOffset: 'timestamptz',
  DateTime: 'timestamp',
  DateOnly: 'date',
  TimeOnly: 'time',
  JSON: 'jsonb',
}

const PG_TO_CSHARP: Partial<Record<string, CSharpType>> = {
  text: 'string',
  'character varying': 'string',
  varchar: 'string',
  int2: 'short',
  smallint: 'short',
  int4: 'int',
  integer: 'int',
  int8: 'long',
  bigint: 'long',
  float4: 'float',
  real: 'float',
  float8: 'double',
  'double precision': 'double',
  numeric: 'decimal',
  boolean: 'bool',
  bool: 'bool',
  timestamptz: 'DateTimeOffset',
  'timestamp with time zone': 'DateTimeOffset',
  timestamp: 'DateTime',
  'timestamp without time zone': 'DateTime',
  date: 'DateOnly',
  time: 'TimeOnly',
  'time without time zone': 'TimeOnly',
  jsonb: 'JSON',
  json: 'JSON',
}

const ALL_TYPES: CSharpType[] = [
  'string', 'short', 'int', 'long', 'float', 'double', 'decimal',
  'bool', 'DateTimeOffset', 'DateTime', 'DateOnly', 'TimeOnly',
  'JSON',
]

const isIntegerType = (t: CSharpType) => ['short', 'int', 'long'].includes(t)
const isDecimalType = (t: CSharpType) => ['float', 'double', 'decimal'].includes(t)
const isDateTimeType = (t: CSharpType) => ['DateTimeOffset', 'DateTime'].includes(t)

// 스칼라(비 JSON)는 NOT NULL로 만들므로 기본값이 반드시 필요합니다(빈 값이면 ALTER ADD COLUMN 실패).
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

function DefaultValueInput({
  type,
  value,
  onChange,
}: {
  type: CSharpType
  value: string
  onChange: (v: string) => void
}) {
  if (type === 'bool') {
    return (
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
      >
        <option value="">지정 안 함</option>
        <option value="true">true</option>
        <option value="false">false</option>
      </select>
    )
  }

  if (isIntegerType(type) || isDecimalType(type)) {
    return (
      <input
        type="number"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        step={isIntegerType(type) ? 1 : 'any'}
        className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
      />
    )
  }

  if (isDateTimeType(type)) {
    return (
      <div className="space-y-1.5">
        <input
          type="datetime-local"
          value={value === 'now()' ? '' : value}
          onChange={(e) => onChange(e.target.value)}
          disabled={value === 'now()'}
          className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff] disabled:bg-neutral-50 disabled:text-neutral-400"
        />
        <label className="flex items-center gap-1.5 text-xs text-neutral-500 cursor-pointer">
          <input
            type="checkbox"
            checked={value === 'now()'}
            onChange={(e) => onChange(e.target.checked ? 'now()' : '')}
            className="rounded"
          />
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
          className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff] disabled:bg-neutral-50 disabled:text-neutral-400"
        />
        <label className="flex items-center gap-1.5 text-xs text-neutral-500 cursor-pointer">
          <input
            type="checkbox"
            checked={value === 'CURRENT_DATE'}
            onChange={(e) => onChange(e.target.checked ? 'CURRENT_DATE' : '')}
            className="rounded"
          />
          CURRENT_DATE 사용
        </label>
      </div>
    )
  }

  if (type === 'TimeOnly') {
    return (
      <input
        type="time"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
      />
    )
  }

  if (type === 'JSON') {
    return (
      <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2">
        <p className="text-xs text-neutral-500 leading-relaxed">
          JSON 필드는 기본값을 지정하지 않습니다. 시작값은 게임에서 관리됩니다.
        </p>
      </div>
    )
  }

  return (
    <input
      type="text"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
    />
  )
}

function AddFieldModal({ target, onClose, onAdded }: { target: ProjectTarget; onClose: () => void; onAdded: () => void }) {
  const [name, setName] = useState('')
  const [type, setType] = useState<CSharpType>('int')
  const [defaultValue, setDefaultValue] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const missingDefault = scalarDefaultMissing(type, defaultValue)

  const handleTypeChange = (t: CSharpType) => {
    setType(t)
    setDefaultValue('')
  }

  const handleSubmit = async () => {
    if (!name.trim() || missingDefault || loading) return
    setError('')
    setLoading(true)
    try {
      await callAdmin(target, 'schema.stage', {
        feature: 'leaderboard_field',
        action: 'add',
        objectName: name.trim(),
        params: {
          colname: name.trim(),
          coltype: TYPE_TO_PG[type],
          // NULL 허용은 타입으로 고정 — JSON=허용, 스칼라=비허용.
          nullable: type === 'JSON',
          // JSON은 기본값 없음. 스칼라는 지정된 기본값.
          default_sql: type === 'JSON' ? null : toPgDefault(defaultValue, type),
        },
      })
      onAdded()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">필드 추가</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700">
            <X className="w-4 h-4" />
          </button>
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
              {ALL_TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            <p className="mt-1 text-xs text-neutral-400">DB 타입: {TYPE_TO_PG[type]}</p>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">기본값</label>
            <DefaultValueInput type={type} value={defaultValue} onChange={setDefaultValue} />
            {missingDefault && (
              <p className="mt-1 text-xs text-amber-600">기본값이 필요합니다.</p>
            )}
          </div>
          <p className="text-xs text-neutral-500">
            필드는 모든 리더보드가 공유하는 목록에 만들어집니다. 만든 뒤 목록에서 이 리더보드에 사용하도록 켜세요.
          </p>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button
            onClick={onClose}
            className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white"
          >
            취소
          </button>
          <button
            onClick={() => { void handleSubmit() }}
            disabled={loading || !name.trim() || missingDefault}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? '추가 중…' : '추가'}
          </button>
        </div>
      </div>
    </div>
  )
}

function EditFieldModal({ target, col, onClose, onSaved }: {
  target: ProjectTarget
  col: Col
  onClose: () => void
  onSaved: () => void
}) {
  const csharpType = PG_TO_CSHARP[col.data_type] ?? 'string'
  const [defaultValue, setDefaultValue] = useState(() => fromPgDefault(col.column_default, csharpType))
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const missingDefault = scalarDefaultMissing(csharpType, defaultValue)

  const handleSubmit = async () => {
    if (missingDefault || loading) return
    setError('')
    setLoading(true)
    try {
      await callAdmin(target, 'schema.stage', {
        feature: 'leaderboard_field',
        action: 'update',
        objectName: col.column_name,
        params: {
          colname: col.column_name,
          // NULL 허용은 타입으로 고정 — JSON=허용, 스칼라=비허용.
          nullable: csharpType === 'JSON',
          // JSON은 기본값 없음. 스칼라는 지정된 기본값.
          default_sql: csharpType === 'JSON' ? null : toPgDefault(defaultValue, csharpType),
        },
      })
      onSaved()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">필드 수정 — {col.column_name}</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700">
            <X className="w-4 h-4" />
          </button>
        </div>
        <div className="p-5 space-y-4">
          <div className="flex items-center gap-2 px-3 py-2 rounded-md bg-neutral-50 border border-neutral-200 text-sm">
            <span className="font-medium text-neutral-700">{col.column_name}</span>
            <span className="text-xs text-neutral-400">{col.data_type}</span>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">기본값</label>
            <DefaultValueInput
              type={csharpType}
              value={defaultValue}
              onChange={setDefaultValue}
            />
            {missingDefault && (
              <p className="mt-1 text-xs text-amber-600">기본값이 필요합니다.</p>
            )}
          </div>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button
            onClick={onClose}
            className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white"
          >
            취소
          </button>
          <button
            onClick={() => { void handleSubmit() }}
            disabled={loading || missingDefault}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? '저장 중…' : '저장'}
          </button>
        </div>
      </div>
    </div>
  )
}

function DeleteFieldModal({ target, col, onClose, onDeleted }: {
  target: ProjectTarget
  col: Col
  onClose: () => void
  onDeleted: () => void
}) {
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const handleDelete = async () => {
    if (confirm !== col.column_name || loading) return
    setError('')
    setLoading(true)
    try {
      await callAdmin(target, 'schema.stage', {
        feature: 'leaderboard_field',
        action: 'drop',
        objectName: col.column_name,
        params: { colname: col.column_name },
      })
      onDeleted()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold text-red-600">필드 삭제</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700">
            <X className="w-4 h-4" />
          </button>
        </div>
        <div className="p-5 space-y-4">
          <div className="rounded-md bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
            <p className="font-medium">이 작업은 되돌릴 수 없습니다.</p>
            <p className="mt-1 text-red-600">
              <span className="font-mono font-semibold">{col.column_name}</span> 필드와 모든 리더보드에 쌓인 그 값이 영구 삭제됩니다.
            </p>
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
          <button
            onClick={onClose}
            className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white"
          >
            취소
          </button>
          <button
            onClick={() => { void handleDelete() }}
            disabled={loading || confirm !== col.column_name}
            className="h-9 px-4 rounded-md bg-red-500 text-white text-sm hover:bg-red-600 disabled:opacity-60"
          >
            {loading ? '삭제 중…' : '삭제'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function ColumnsTab({
  target,
  onUnauthenticated,
  selectedCode,
  onSelect,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  selectedCode: string
  onSelect: (code: string) => void
}) {
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [showAdd, setShowAdd] = useState(false)
  const [editCol, setEditCol] = useState<Col | null>(null)
  const [deleteCol, setDeleteCol] = useState<Col | null>(null)
  const [error, setError] = useState('')

  const [leaderboards, setLeaderboards] = useState<LbRow[]>([])
  const [listLoading, setListLoading] = useState(true)
  const [colsData, setColsData] = useState<ColsData | undefined>(undefined)
  const [colsLoading, setColsLoading] = useState(false)
  const { drafts: allDrafts, reload: reloadDrafts } = usePendingChanges()
  const drafts = allDrafts ?? []
  const [busy, setBusy] = useState(false)

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) {
        onUnauthenticated()
        return
      }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const reloadBoards = useCallback(async () => {
    setListLoading(true)
    try {
      setLeaderboards((await callAdmin<{ rows: LbRow[] }>(target, 'leaderboard.list')).rows)
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
    } finally {
      setListLoading(false)
    }
  }, [target, report])

  const reloadCols = useCallback(async () => {
    if (!selectedCode) {
      setColsData(undefined)
      return
    }
    setColsLoading(true)
    try {
      setColsData(await callAdmin<ColsData>(target, 'leaderboard.columns', { code: selectedCode }))
    } catch (e: unknown) {
      report(e, '필드를 불러오지 못했습니다.')
    } finally {
      setColsLoading(false)
    }
  }, [target, selectedCode, report])

  const refresh = useCallback(() => {
    void reloadBoards(); void reloadCols(); void reloadDrafts()
  }, [reloadBoards, reloadCols, reloadDrafts])

  useEffect(() => {
    void reloadBoards()
  }, [reloadBoards])

  useEffect(() => {
    void reloadCols()
    setPage(1)
  }, [reloadCols])

  const all: Col[] = colsData?.all ?? []
  const attached: AttachedRow[] = colsData?.attached ?? []

  const usedFields = new Set<string>()
  leaderboards.forEach((lb) => {
    lb.columns.split(',').forEach((c) => { if (c.trim()) usedFields.add(c.trim()) })
  })

  const isAttached = (name: string) => attached.some((a) => a.column_name === name)

  // 대기 중(draft) 변경. 사용/해제는 이 리더보드 것만, 필드 추가/수정/삭제는 카탈로그 공용이라 모두.
  const pendingByCol: Record<string, { id: number; action: 'attach' | 'detach' }> = {}
  for (const d of drafts) {
    if (d.feature !== 'leaderboard_field') continue
    if (d.action !== 'attach' && d.action !== 'detach') continue
    if (String(d.params?.['code'] ?? '') !== selectedCode) continue
    pendingByCol[d.object_name] = { id: d.id, action: d.action as 'attach' | 'detach' }
  }
  const pendingItems: PendingItem[] = drafts
    .filter((d) =>
      d.feature === 'leaderboard_field' &&
      (['add', 'update', 'drop'].includes(d.action) ||
        (['attach', 'detach'].includes(d.action) && String(d.params?.['code'] ?? '') === selectedCode)),
    )
    .map((d) => ({ id: d.id, label: opLabel(d.feature, d.action), detail: d.object_name }))

  const discardOne = async (id: number) => {
    setBusy(true)
    try {
      await callAdmin(target, 'schema.discardDraft', { id })
      refresh()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const handleToggle = async (
    col: Col,
    live: boolean,
    pend: { id: number; action: 'attach' | 'detach' } | undefined,
  ) => {
    if (!selectedCode) return
    setBusy(true)
    try {
      if (pend) {
        // 대기 중이던 변경을 다시 누르면 취소(중복 draft 방지). 패널의 취소 버튼과 동일.
        await callAdmin(target, 'schema.discardDraft', { id: pend.id })
      } else if (live) {
        await callAdmin(target, 'schema.stage', {
          feature: 'leaderboard_field', action: 'detach', objectName: col.column_name,
          params: { code: selectedCode, colname: col.column_name },
        })
      } else {
        await callAdmin(target, 'schema.stage', {
          feature: 'leaderboard_field', action: 'attach', objectName: col.column_name,
          params: { code: selectedCode, colname: col.column_name, sort_order: attached.length },
        })
      }
      refresh()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const q = search.trim().toLowerCase()
  const filtered = useMemo(
    () =>
      q
        ? all.filter((c) => c.column_name.toLowerCase().includes(q) || c.data_type.toLowerCase().includes(q))
        : all,
    [all, q],
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
        <div className="flex items-center gap-2">
          <select
            value={selectedCode}
            onChange={(e) => onSelect(e.target.value)}
            disabled={listLoading}
            className="h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white min-w-[180px] focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff] disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {listLoading ? (
              <option value={selectedCode}>불러오는 중…</option>
            ) : (
              <>
                <option value="">리더보드 선택</option>
                {leaderboards.map((lb) => (
                  <option key={lb.code} value={lb.code}>{lb.display_name}</option>
                ))}
              </>
            )}
          </select>
          <div className="relative w-64">
            <Search className="w-4 h-4 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="필드 검색"
              className="w-full h-9 pl-9 pr-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
            />
          </div>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setShowAdd(true)}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90"
          >
            <Plus className="w-4 h-4" />
            필드 추가
          </button>
          <button
            onClick={refresh}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${colsLoading ? 'animate-spin' : ''}`} />
            새로고침
          </button>
        </div>
      </div>

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-5 py-2.5 font-medium w-28">사용</th>
              <th className="text-left px-5 py-2.5 font-medium">필드</th>
              <th className="text-left px-5 py-2.5 font-medium">타입</th>
              <th className="text-left px-5 py-2.5 font-medium">기본값</th>
              <th className="px-3 py-2.5" />
            </tr>
          </thead>
          <tbody>
            {colsLoading ? (
              <tr>
                <td colSpan={5} className="px-5 py-14 text-center">
                  <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
                </td>
              </tr>
            ) : filtered.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-5 py-10 text-center text-sm text-neutral-500">
                  {!selectedCode
                    ? '리더보드를 선택하면 필드가 표시됩니다.'
                    : all.length === 0 ? '필드가 없습니다.' : '검색 결과가 없습니다.'}
                </td>
              </tr>
            ) : (
              pageRows.map((c) => {
                const att = isAttached(c.column_name) // 라이브 상태만 표시
                const pend = pendingByCol[c.column_name]
                const inUse = usedFields.has(c.column_name)
                return (
                  <tr key={c.column_name} className="border-t border-neutral-100">
                    <td className="px-5 py-3">
                      <button
                        onClick={() => { void handleToggle(c, att, pend) }}
                        disabled={!selectedCode || busy}
                        className={[
                          'inline-flex items-center px-2.5 py-1 rounded text-xs font-medium transition-colors whitespace-nowrap',
                          att
                            ? 'bg-emerald-100 text-emerald-700 hover:bg-emerald-200'
                            : 'bg-neutral-100 text-neutral-600 hover:bg-neutral-200',
                          !selectedCode || busy ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer',
                        ].join(' ')}
                      >
                        {att ? '사용 중' : '사용 안 함'}
                      </button>
                    </td>
                    <td className="px-5 py-3 font-medium">{c.column_name}</td>
                    <td className="px-5 py-3">
                      <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">
                        {c.data_type}
                      </span>
                    </td>
                    <td className="px-5 py-3 text-neutral-600 font-mono text-xs">
                      {c.column_default ?? ''}
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => setEditCol(c)}
                          className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700"
                        >
                          <Pencil className="w-3.5 h-3.5" />
                        </button>
                        <button
                          onClick={() => setDeleteCol(c)}
                          disabled={inUse}
                          title={inUse ? '사용 중인 리더보드가 있습니다. 먼저 각 리더보드에서 사용 해제 → 변경 관리에서 게시한 뒤 삭제하세요.' : '삭제'}
                          className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500 disabled:opacity-40 disabled:cursor-not-allowed"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })
            )}
          </tbody>
        </table>
        <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          <span>Showing {start}-{end} of {total}</span>
          <div className="flex items-center gap-1">
            <button
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"
            >
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
            <span className="px-2">{page} / {totalPages}</span>
            <button
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"
            >
              <ChevronRight className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </WhiteCard>

      <p className="text-xs text-neutral-500">
        이 리더보드에서 사용할 필드는 '사용'으로 켜 두세요. 켠 필드만 순위와 함께 주고받을 수 있습니다.
      </p>

      {showAdd && (
        <AddFieldModal target={target} onClose={() => setShowAdd(false)} onAdded={() => { setShowAdd(false); refresh() }} />
      )}
      {editCol && (
        <EditFieldModal target={target} col={editCol} onClose={() => setEditCol(null)} onSaved={() => { setEditCol(null); refresh() }} />
      )}
      {deleteCol && (
        <DeleteFieldModal target={target} col={deleteCol} onClose={() => setDeleteCol(null)} onDeleted={() => { setDeleteCol(null); refresh() }} />
      )}
    </div>
  )
}
