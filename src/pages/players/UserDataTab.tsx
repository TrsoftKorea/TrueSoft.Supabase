import { useEffect, useMemo, useState, useCallback } from 'react'
import { Link } from 'react-router-dom'
import { RefreshCw, Search, Pencil, X, ExternalLink } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { fetchUserDataFields, isIntegerPgType, pgTypeToValueType, type UserDataField } from '../../lib/userData'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { formatDateTime, diffSummary } from '../../components/ui/format'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { TypedValueInput, parseTypedValue } from '../../components/ui/TypedValueField'

type LogRow = { id: number; diff: Record<string, unknown>; source: string | null; created_at: string }

const PAGE_SIZE = 15

function formatValue(v: unknown, dataType: string): string {
  if (v === null || v === undefined) return '—'
  if (dataType === 'jsonb') return JSON.stringify(v)
  if (dataType === 'boolean') return v ? '참' : '거짓'
  return String(v)
}

function EditValueModal({
  target,
  onUnauthenticated,
  accountId,
  field,
  onClose,
  onSaved,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  field: UserDataField
  onClose: () => void
  onSaved: (row: Record<string, unknown>) => void
}) {
  const type = pgTypeToValueType(field.data_type)
  const isJson = type === 'json'

  const [text, setText] = useState(() =>
    isJson ? JSON.stringify(field.value ?? null, null, 2) : field.value === null || field.value === undefined ? '' : String(field.value),
  )
  const [boolValue, setBoolValue] = useState(() => Boolean(field.value))
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const handleSubmit = async () => {
    setError('')
    const parsed = parseTypedValue(type, text, boolValue)
    if (!parsed.ok) {
      setError(parsed.error)
      return
    }

    setLoading(true)
    try {
      const row = await callAdmin<Record<string, unknown>>(target, 'userData.update', {
        accountId,
        patch: { [field.column_name]: parsed.value },
      })
      onSaved(row)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) {
        onUnauthenticated()
        return
      }
      setError(e instanceof Error ? e.message : '저장에 실패했습니다.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">값 수정 — {field.column_name}</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700">
            <X className="w-4 h-4" />
          </button>
        </div>
        <div className="p-5 space-y-4">
          <div className="flex items-center gap-2 px-3 py-2 rounded-md bg-neutral-50 border border-neutral-200 text-sm">
            <span className="font-medium text-neutral-700">{field.column_name}</span>
            <span className="text-xs text-neutral-400">{field.data_type}</span>
          </div>
          <TypedValueInput
            type={type}
            text={text}
            onTextChange={setText}
            boolValue={boolValue}
            onBoolChange={setBoolValue}
            integer={isIntegerPgType(field.data_type)}
          />
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">
            취소
          </button>
          <button
            onClick={() => { void handleSubmit() }}
            disabled={loading}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? '저장 중…' : '저장'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function UserDataTab({
  target,
  onUnauthenticated,
  accountId,
  displayName,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  displayName?: string
}) {
  const [fields, setFields] = useState<UserDataField[]>([])
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [logs, setLogs] = useState<LogRow[]>([])
  const [logsLoading, setLogsLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [editField, setEditField] = useState<UserDataField | null>(null)
  const [error, setError] = useState('')

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

  const reload = useCallback(async () => {
    setLoading(true)
    try {
      const { fields: f, row } = await fetchUserDataFields(target, accountId)
      setFields(f)
      setUpdatedAt(row?.['updated_at'] != null ? String(row['updated_at']) : null)
    } catch (e: unknown) {
      report(e, '유저 데이터를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, accountId, report])

  const reloadLogs = useCallback(async () => {
    setLogsLoading(true)
    try {
      setLogs(await callAdmin<LogRow[]>(target, 'userData.logs', { accountId, limit: 20 }))
    } catch (e: unknown) {
      report(e, '변경 이력을 불러오지 못했습니다.')
    } finally {
      setLogsLoading(false)
    }
  }, [target, accountId, report])

  useEffect(() => {
    void reload()
    void reloadLogs()
  }, [reload, reloadLogs])

  const q = search.trim().toLowerCase()
  const filtered = useMemo(
    () => (q ? fields.filter((f) => f.column_name.toLowerCase().includes(q)) : fields),
    [fields, q],
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

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <div className="relative w-64">
            <Search className="w-4 h-4 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="필드 검색"
              className="w-full h-9 pl-9 pr-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
            />
          </div>
          {updatedAt && (
            <span className="text-xs text-neutral-500">마지막 저장: {formatDateTime(updatedAt)}</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Link
            to={`/data-management?tab=players&account=${encodeURIComponent(accountId)}&name=${encodeURIComponent(displayName ?? '')}`}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50"
          >
            <ExternalLink className="w-3.5 h-3.5" />
            데이터 관리에서 열기
          </Link>
          <button
            onClick={() => { void reload(); void reloadLogs() }}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            새로고침
          </button>
        </div>
      </div>

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-5 py-2.5 font-medium">필드</th>
              <th className="text-left px-4 py-2.5 font-medium">타입</th>
              <th className="text-left px-4 py-2.5 font-medium">값</th>
              <th className="px-3 py-2.5 w-16" />
            </tr>
          </thead>
          <tbody>
            {loading || pageRows.length === 0 ? (
              <TableStatusRow
                loading={loading}
                empty={pageRows.length === 0}
                colSpan={4}
                emptyText={fields.length === 0 ? '이 플레이어의 저장된 데이터가 없습니다.' : '검색 결과가 없습니다.'}
              />
            ) : (
              pageRows.map((f) => (
                <tr key={f.column_name} className="border-t border-neutral-100">
                  <td className="px-5 py-3 font-medium">{f.column_name}</td>
                  <td className="px-4 py-3">
                    <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">
                      {f.data_type}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-neutral-600 font-mono text-xs max-w-[360px] truncate" title={formatValue(f.value, f.data_type)}>
                    {formatValue(f.value, f.data_type)}
                  </td>
                  <td className="px-3 py-3 text-right">
                    <button
                      onClick={() => setEditField(f)}
                      className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700"
                    >
                      <Pencil className="w-3.5 h-3.5" />
                    </button>
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
              <button
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="px-2 py-1 rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"
              >
                이전
              </button>
              <span className="px-2">{page} / {totalPages}</span>
              <button
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="px-2 py-1 rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"
              >
                다음
              </button>
            </div>
          </div>
        )}
      </WhiteCard>

      <WhiteCard>
        <div className="border-b border-neutral-100 px-5 py-3">
          <span className="text-sm font-medium text-neutral-800">최근 변경 이력</span>
        </div>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-5 py-2.5 font-medium whitespace-nowrap">시각</th>
                <th className="text-left px-4 py-2.5 font-medium">출처</th>
                <th className="text-left px-4 py-2.5 font-medium">바뀐 값</th>
              </tr>
            </thead>
            <tbody>
              {logsLoading || logs.length === 0 ? (
                <TableStatusRow loading={logsLoading} empty={logs.length === 0} colSpan={3} emptyText="변경 이력이 없습니다." />
              ) : (
                logs.map((l) => (
                  <tr key={l.id} className="border-t border-neutral-100">
                    <td className="px-5 py-3 whitespace-nowrap">{formatDateTime(l.created_at)}</td>
                    <td className="px-4 py-3 text-xs text-neutral-500">{l.source ?? '—'}</td>
                    <td className="px-4 py-3 text-xs text-neutral-500 max-w-[420px] truncate" title={diffSummary(l.diff)}>
                      {diffSummary(l.diff)}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      {editField && (
        <EditValueModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={accountId}
          field={editField}
          onClose={() => setEditField(null)}
          onSaved={(updated) => {
            setFields((prev) => prev.map((f) => ({ ...f, value: updated[f.column_name] ?? null })))
            setUpdatedAt(updated['updated_at'] != null ? String(updated['updated_at']) : null)
            setEditField(null)
            void reloadLogs()
          }}
        />
      )}
    </div>
  )
}
