import { useEffect, useMemo, useState, useCallback } from 'react'
import { Link } from 'react-router-dom'
import { RefreshCw, Search, Pencil, ExternalLink } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { fetchUserDataFields, isIntegerPgType, pgTypeToValueType, type UserDataField } from '../../lib/userData'
import { WhiteCard } from '../../components/ui/Card'
import { inputCls } from '../../components/ui/Input'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { formatDateTime, diffSummary, formatTotal } from '../../components/ui/format'
import { pgTypeLabel } from '../../lib/schemaLabels'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { TypedValueInput, parseTypedValue } from '../../components/ui/TypedValueField'
import { Button } from '../../components/ui/Button'
import { EditDialog } from '../../components/ui/EditDialog'
import { TargetGameStrip } from '../../components/ui/TargetGameStrip'
import { RestoreSaveModal } from './RestoreSaveModal'

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
    <EditDialog
      error={error || undefined}
      title="값 수정"
      description={`${field.column_name} · ${pgTypeLabel(field.data_type)}`}
      busy={loading}
      onClose={onClose}
      footer={<>
        <Button variant="outline" onClick={onClose} disabled={loading}>취소</Button>
        <Button variant="primary" onClick={() => { void handleSubmit() }} disabled={loading}>
          {loading ? '저장 중…' : '저장'}
        </Button>
      </>}
    >
          <TargetGameStrip />
          <TypedValueInput
            type={type}
            text={text}
            onTextChange={setText}
            boolValue={boolValue}
            onBoolChange={setBoolValue}
            integer={isIntegerPgType(field.data_type)}
          />
    </EditDialog>
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
  // 필드 목록(fields)은 스키마의 컬럼 수만큼 항상 채워지므로(값이 null이어도 항목은 남는다),
  // "저장된 데이터가 아예 없음"은 fields.length가 아니라 이걸로 따로 추적해야 한다.
  const [hasRow, setHasRow] = useState(true)
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [logs, setLogs] = useState<LogRow[]>([])
  const [logsLoading, setLogsLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [editField, setEditField] = useState<UserDataField | null>(null)
  const [restoreLog, setRestoreLog] = useState<LogRow | null>(null)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)
  const [logsFailed, setLogsFailed] = useState(false)

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
    setFailed(false)
    try {
      const { fields: f, row } = await fetchUserDataFields(target, accountId)
      setFields(f)
      setHasRow(row !== null)
      setUpdatedAt(row?.['updated_at'] != null ? String(row['updated_at']) : null)
    } catch (e: unknown) {
      setFields([])
      setFailed(true)
      report(e, '유저 데이터를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, accountId, report])

  const reloadLogs = useCallback(async () => {
    setLogsLoading(true)
    setLogsFailed(false)
    try {
      setLogs(await callAdmin<LogRow[]>(target, 'userData.logs', { accountId, limit: 20 }))
    } catch (e: unknown) {
      setLogsFailed(true)
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
              className={inputCls + ' w-full pl-9'}
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
          <Button variant="outline" size="compact"
            onClick={() => { void reload(); void reloadLogs() }}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            새로고침
          </Button>
        </div>
      </div>

      {!loading && !hasRow && !failed && (
        <div className="rounded-md border border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-800">
          이 플레이어는 아직 저장된 데이터가 없습니다. 아래 값은 비어 있습니다.
        </div>
      )}

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
                failed={failed}
                colSpan={4}
                emptyText={q ? '검색 결과가 없습니다.' : '필드가 없습니다.'}
              />
            ) : (
              pageRows.map((f) => (
                <tr key={f.column_name} className="border-t border-neutral-100">
                  <td className="px-5 py-3 font-medium">{f.column_name}</td>
                  <td className="px-4 py-3">
                    <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700 whitespace-nowrap">
                      {pgTypeLabel(f.data_type)}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-neutral-600 font-mono text-xs max-w-[360px] truncate" title={formatValue(f.value, f.data_type)}>
                    {formatValue(f.value, f.data_type)}
                  </td>
                  <td className="px-3 py-3 text-right">
                    <button
                      onClick={() => setEditField(f)}
                      title="값 수정"
                      aria-label="값 수정"
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
            <span>{start}–{end} / {formatTotal(total)}</span>
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
                <th className="text-left px-4 py-2.5 font-medium">바뀌기 전 값</th>
                <th className="w-28" />
              </tr>
            </thead>
            <tbody>
              {logsLoading || logs.length === 0 ? (
                <TableStatusRow loading={logsLoading} empty={logs.length === 0} failed={logsFailed} colSpan={4} emptyText="변경 이력이 없습니다." />
              ) : (
                logs.map((l) => (
                  <tr key={l.id} className="border-t border-neutral-100">
                    <td className="px-5 py-3 whitespace-nowrap">{formatDateTime(l.created_at)}</td>
                    <td className="px-4 py-3 text-xs text-neutral-500">{l.source ?? '—'}</td>
                    <td className="px-4 py-3 text-xs text-neutral-500 max-w-[420px] truncate" title={diffSummary(l.diff)}>
                      {diffSummary(l.diff)}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Button variant="outline" size="sm" onClick={() => setRestoreLog(l)}>이 변경 전으로</Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      {restoreLog && (
        <RestoreSaveModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={accountId}
          logId={restoreLog.id}
          loggedAt={restoreLog.created_at}
          playerName={displayName || accountId}
          onClose={() => setRestoreLog(null)}
          onRestored={() => { setRestoreLog(null); void reload(); void reloadLogs() }}
        />
      )}

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
