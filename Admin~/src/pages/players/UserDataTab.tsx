import { useEffect, useMemo, useState, useCallback } from 'react'
import { Link } from 'react-router-dom'
import { Search, Pencil, ExternalLink, History, Trash2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { fetchUserDataFields, isIntegerPgType, pgTypeToValueType, type UserDataField } from '../../lib/userData'
import { dangerInputCls, inputCls } from '../../components/ui/Input'
import { TableCard, type Column } from '../../components/ui/DataTable'
import { TableFooter } from '../../components/ui/TableFooter'
import { formatDateTime, diffPairs, diffSummary, formatTotal } from '../../components/ui/format'
import { Pairs } from '../../components/ui/Pairs'
import { pgTypeLabel } from '../../lib/schemaLabels'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { TypedValueInput, parseTypedValue } from '../../components/ui/TypedValueField'
import { Button } from '../../components/ui/Button'
import { withPlayer } from '../../components/ui/PlayerLink'
import { EditDialog } from '../../components/ui/EditDialog'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { RestoreSaveModal } from './RestoreSaveModal'
import { RefreshButton } from '../../components/ui/FilterBar'

type LogRow = { id: number; diff: Record<string, unknown>; source: string | null; created_at: string }

const PAGE_SIZE = 15

function formatValue(v: unknown, dataType: string): string {
  if (v === null || v === undefined) return ''
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

const RESET_CONFIRM_WORD = '초기화'

export function ResetSaveDialog({
  target,
  onUnauthenticated,
  accountId,
  playerName,
  onClose,
  onDone,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  playerName: string
  onClose: () => void
  onDone: () => void
}) {
  const [word, setWord] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const handleConfirm = async () => {
    setBusy(true)
    setError('')
    try {
      await callAdmin(target, 'userData.reset', { accountId })
      onDone()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) {
        onUnauthenticated()
        return
      }
      setError(e instanceof Error ? e.message : '초기화에 실패했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <ConfirmDialog
      open
      danger
      title="저장 데이터 초기화"
      confirmLabel="초기화"
      busy={busy}
      confirmDisabled={word.trim() !== RESET_CONFIRM_WORD}
      error={error || undefined}
      onConfirm={() => { void handleConfirm() }}
      onCancel={onClose}
    >
      <p className="text-sm text-neutral-600">대상 플레이어: <span className="font-medium text-neutral-800">{playerName}</span></p>
      <ul className="list-disc pl-5 space-y-1 text-sm text-neutral-600">
        <li>모든 필드가 처음 값으로 돌아가고, 다음 접속부터 새로 시작한 플레이어로 처리됩니다.</li>
        <li>이 기능이 들어간 게임 빌드 전까지는 새로 시작한 플레이어로 처리되지 않고, 플레이나누를 함께 쓰는 게임은 다음 접속에 옛 데이터로 돌아갑니다.</li>
        <li>게임에 접속 중이면 게임을 다시 켜야 반영되고, 그 전에 저장되는 필드는 게임의 값으로 다시 바뀝니다.</li>
        <li>7일 안에는 변경 이력의 &lsquo;이 변경 전으로&rsquo;로 되돌릴 수 있습니다.</li>
      </ul>
      <div>
        <div className="text-xs font-medium text-neutral-500 mb-1">확인을 위해 &lsquo;{RESET_CONFIRM_WORD}&rsquo;를 입력합니다</div>
        <input
          value={word}
          onChange={(e) => setWord(e.target.value)}
          autoFocus
          className={dangerInputCls + ' w-full'}
        />
      </div>
    </ConfirmDialog>
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
  const [resetOpen, setResetOpen] = useState(false)
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
      report(e, '플레이어 데이터를 불러오지 못했습니다.')
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
            to={withPlayer('/player-data', accountId, displayName)}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50"
          >
            <ExternalLink className="w-3.5 h-3.5" />
            플레이어 데이터에서 열기
          </Link>
          <Link
            to={withPlayer('/data-logs', accountId, displayName)}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50 text-neutral-600 whitespace-nowrap"
          >
            <History className="w-3.5 h-3.5" />
            데이터 로그
          </Link>
          <RefreshButton onClick={() => { void reload(); void reloadLogs() }} loading={loading} />
          <Button variant="dangerOutline" onClick={() => setResetOpen(true)} disabled={loading || failed || !hasRow}>
            <Trash2 className="w-3.5 h-3.5" />
            데이터 초기화
          </Button>
        </div>
      </div>

      {!loading && !hasRow && !failed && (
        <div className="rounded-md border border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-800">
          이 플레이어는 아직 저장된 데이터가 없습니다. 아래 값은 비어 있습니다.
        </div>
      )}

      <TableCard
        columns={[
          { key: 'name', header: '필드', kind: 'mono', cell: (f) => f.column_name },
          {
            key: 'type', header: '타입', kind: 'name', cell: (f) => (
              <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">{pgTypeLabel(f.data_type)}</span>
            ),
          },
          {
            key: 'value', header: '값', kind: 'text',
            cell: (f) => {
              const v = formatValue(f.value, f.data_type)
              return v === '' ? null : <span className="font-mono text-xs text-neutral-600">{v}</span>
            },
            full: (f) => formatValue(f.value, f.data_type),
          },
          {
            key: 'actions', header: '', kind: 'actions', cell: (f) => (
              <span className="inline-flex gap-1.5">
                <button
                  onClick={() => setEditField(f)}
                  title="값 수정"
                  aria-label="값 수정"
                  className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700"
                >
                  <Pencil className="w-3.5 h-3.5" />
                </button>
              </span>
            ),
          },
        ] satisfies Column<UserDataField>[]}
        rows={pageRows}
        rowKey={(f) => f.column_name}
        loading={loading}
        failed={failed}
        emptyText={q ? '검색 결과가 없습니다.' : '필드가 없습니다.'}
        footer={total > 0 && (
          <TableFooter summary={<>{start}–{end} / {formatTotal(total)}</>} page={page} totalPages={totalPages} onPageChange={setPage} />
        )}
      />

      <TableCard
        title="변경 이력"
        description="바뀌기 전 값을 최근 순으로 봅니다."
        columns={[
          { key: 'at', header: '시각', kind: 'datetime', cell: (l) => formatDateTime(l.created_at) },
          { key: 'source', header: '출처', kind: 'name', cell: (l) => l.source },
          {
            key: 'diff', header: '바뀌기 전 값', kind: 'text',
            cell: (l) => diffPairs(l.diff).length ? <Pairs items={diffPairs(l.diff)} /> : null,
            full: (l) => diffSummary(l.diff),
          },
          {
            key: 'actions', header: '', kind: 'actions', cell: (l) => (
              <Button variant="outline" size="sm" onClick={() => setRestoreLog(l)}>이 변경 전으로</Button>
            ),
          },
        ] satisfies Column<LogRow>[]}
        rows={logs}
        rowKey={(l) => String(l.id)}
        loading={logsLoading}
        failed={logsFailed}
        emptyText="변경 이력이 없습니다."
      />

      {resetOpen && (
        <ResetSaveDialog
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={accountId}
          playerName={displayName || accountId}
          onClose={() => setResetOpen(false)}
          onDone={() => { setResetOpen(false); void reload(); void reloadLogs() }}
        />
      )}

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
