import { useEffect, useRef, useState, useCallback } from 'react'
import { Search, Pencil, Loader2, Copy, ClipboardPaste, X, AlertTriangle } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { fetchUserDataFields, isIntegerPgType, pgTypeToValueType, type UserDataField } from '../../lib/userData'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { formatDateTime } from '../../components/ui/format'
import { TypedValueInput, parseTypedValue } from '../../components/ui/TypedValueField'

type Row = { account_id: string; display_name: string; updated_at: string }

const CLIPBOARD_KEY = 'tb_data_clipboard'

type DataClipboard = {
  sourceLabel: string
  sourceAccountId: string
  data: Record<string, unknown>
  copiedAt: string
  updatedAt: string
  appKey: string
}

function readClipboard(): DataClipboard | null {
  try {
    const raw = localStorage.getItem(CLIPBOARD_KEY)
    return raw ? (JSON.parse(raw) as DataClipboard) : null
  } catch {
    return null
  }
}

function valueToString(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (typeof v === 'object') return JSON.stringify(v)
  return String(v)
}

/** accountId 의 필드 값만 뽑는다 — 컬럼 정의는 lib/userData 에서 공유한다. */
async function fetchFields(target: ProjectTarget, accountId: string): Promise<UserDataField[]> {
  return (await fetchUserDataFields(target, accountId)).fields
}

function FieldValueModal({ field, initialValue, onClose, onApply }: {
  field: UserDataField
  initialValue: string
  onClose: () => void
  onApply: (column: string, value: unknown) => void
}) {
  const type = pgTypeToValueType(field.data_type)
  const [text, setText] = useState(initialValue)
  const [boolValue, setBoolValue] = useState(initialValue.trim().toLowerCase() === 'true')
  const [error, setError] = useState('')

  const handleApply = () => {
    const parsed = parseTypedValue(type, text, boolValue)
    if (!parsed.ok) { setError(parsed.error); return }
    onApply(field.column_name, parsed.value)
    onClose()
  }

  return (
    <div className="fixed inset-0 z-[60] bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">필드 값 수정</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          <div>
            <label className="block text-xs text-neutral-500 mb-1">필드명</label>
            <div className="text-sm font-medium">{field.column_name}</div>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">타입</label>
            <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">{field.data_type}</span>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">값</label>
            <TypedValueInput
              type={type}
              text={text}
              onTextChange={setText}
              boolValue={boolValue}
              onBoolChange={setBoolValue}
              integer={isIntegerPgType(field.data_type)}
            />
          </div>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button
            onClick={handleApply}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90"
          >
            적용
          </button>
        </div>
      </div>
    </div>
  )
}

// 여러 필드를 모아뒀다가 한 번에 저장한다 — 필드 하나 고칠 때마다 저장하는 UserDataTab 과 달리,
// 여기는 클립보드로 복사해 온 데이터를 한 번에 붙여넣는 시나리오가 있어 일괄 저장이 필요하다.
function PlayerEditModal({
  target, onUnauthenticated, accountId, displayName, onClose, onChanged,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  displayName: string
  onClose: () => void
  onChanged: () => void
}) {
  const [rows, setRows] = useState<UserDataField[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [editField, setEditField] = useState<UserDataField | null>(null)
  const [edits, setEdits] = useState<Record<string, unknown>>({})
  const [colSearch, setColSearch] = useState('')
  const [askDiscard, setAskDiscard] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await fetchFields(target, accountId))
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, accountId, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  const filteredRows = rows.filter((r) => r.column_name.toLowerCase().includes(colSearch.trim().toLowerCase()))
  const pendingCount = Object.keys(edits).length

  const requestClose = () => {
    if (pendingCount > 0) { setAskDiscard(true); return }
    onClose()
  }

  const handleSave = async () => {
    if (pendingCount === 0 || saving) return
    setSaving(true); setError('')
    try {
      await callAdmin(target, 'userData.update', { accountId, patch: edits })
      onChanged()
      onClose()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '저장에 실패했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl h-[600px] max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <div>
            <h3 className="text-base font-semibold">데이터 수정</h3>
            <p className="text-xs text-neutral-500 mt-0.5">{displayName}</p>
          </div>
          <button onClick={requestClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        {!loading && (
          <div className="px-5 pt-3 pb-3 border-b">
            <div className="relative">
              <Search className="w-4 h-4 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                value={colSearch}
                onChange={(e) => setColSearch(e.target.value)}
                className="w-full border border-neutral-300 rounded-md pl-9 pr-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
              />
            </div>
          </div>
        )}
        {error && <div className="px-5 pt-3 text-sm text-red-500">{error}</div>}
        <div className="flex-1 overflow-y-auto">
          {loading ? (
            <div className="p-8 text-center text-sm text-neutral-500"><Loader2 className="w-5 h-5 animate-spin text-neutral-300 mx-auto" /></div>
          ) : (
            <table className="w-full text-sm table-fixed">
              <thead className="bg-neutral-50 text-neutral-500 text-xs">
                <tr>
                  <th className="text-left px-5 py-2 font-medium w-1/4">필드</th>
                  <th className="text-left px-5 py-2 font-medium w-28">타입</th>
                  <th className="text-left px-5 py-2 font-medium">값</th>
                  <th className="text-right px-5 py-2 font-medium w-16" />
                </tr>
              </thead>
              <tbody>
                {filteredRows.map((r) => {
                  const isEdited = r.column_name in edits
                  const displayValue = isEdited ? valueToString(edits[r.column_name]) : valueToString(r.value)
                  return (
                    <tr key={r.column_name} className="border-t border-neutral-100">
                      <td className="px-5 py-3 font-medium">{r.column_name}</td>
                      <td className="px-5 py-3">
                        <span className="inline-block px-2 py-0.5 text-xs rounded bg-neutral-100 text-neutral-700">{r.data_type}</span>
                      </td>
                      <td className={`px-5 py-3 font-mono text-xs truncate ${isEdited ? 'text-amber-700' : 'text-neutral-700'}`}>
                        {displayValue || <span className="text-neutral-400">null</span>}
                        {isEdited && <span className="text-[10px] bg-amber-100 text-amber-700 px-1 rounded ml-1">수정됨</span>}
                      </td>
                      <td className="px-5 py-3 text-right">
                        <button onClick={() => setEditField(r)} className="inline-flex items-center justify-center w-7 h-7 rounded hover:bg-neutral-100 text-neutral-500 hover:text-[#1677ff]">
                          <Pencil className="w-3.5 h-3.5" />
                        </button>
                      </td>
                    </tr>
                  )
                })}
                {!loading && rows.length === 0 && (
                  <tr><td colSpan={4} className="px-5 py-8 text-center text-sm text-neutral-500">편집 가능한 필드가 없습니다.</td></tr>
                )}
                {rows.length > 0 && filteredRows.length === 0 && (
                  <tr><td colSpan={4} className="px-5 py-8 text-center text-sm text-neutral-500">검색 결과가 없습니다.</td></tr>
                )}
              </tbody>
            </table>
          )}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={requestClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button
            onClick={() => { void handleSave() }}
            disabled={pendingCount === 0 || saving}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {saving ? '저장 중…' : `저장 (${pendingCount})`}
          </button>
        </div>
      </div>

      {editField && (
        <FieldValueModal
          field={editField}
          initialValue={editField.column_name in edits ? valueToString(edits[editField.column_name]) : valueToString(editField.value)}
          onClose={() => setEditField(null)}
          onApply={(column, coerced) => {
            const original = rows.find((r) => r.column_name === column)?.value
            setEdits((prev) => {
              const next = { ...prev }
              if (JSON.stringify(coerced) === JSON.stringify(original)) delete next[column]
              else next[column] = coerced
              return next
            })
          }}
        />
      )}

      <ConfirmDialog
        open={askDiscard}
        title="변경 버리기"
        confirmLabel="버리고 닫기"
        danger
        onConfirm={() => { setAskDiscard(false); onClose() }}
        onCancel={() => setAskDiscard(false)}
        description={`저장하지 않은 변경 ${pendingCount}건이 사라집니다.`}
      />
    </div>
  )
}

function PasteConfirmModal({
  clipboard, targetRow, onConfirm, onClose, loading, compareLoading, changedCount, sourceStale,
}: {
  clipboard: DataClipboard
  targetRow: Row
  onConfirm: () => void
  onClose: () => void
  loading: boolean
  compareLoading: boolean
  changedCount: number
  sourceStale: boolean
}) {
  const fieldCount = Object.keys(clipboard.data).length
  const noChanges = !compareLoading && changedCount === 0
  return (
    <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50 p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6 flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <p className="text-sm font-semibold text-neutral-800">데이터 덮어쓰기</p>
          <button onClick={onClose} className="p-1 rounded hover:bg-neutral-100"><X className="w-4 h-4" /></button>
        </div>
        <div className="text-sm text-neutral-600 space-y-1">
          <p>클립보드 출처: <span className="font-medium text-neutral-800">{clipboard.sourceLabel}</span></p>
          <p>대상 플레이어: <span className="font-medium text-neutral-800">{targetRow.display_name}</span></p>
          <p>필드 수: <span className="font-medium">{fieldCount}개</span></p>
          {compareLoading ? <p className="text-neutral-500">비교 중…</p> : <p>변경될 필드: <span className="font-medium">{changedCount}개</span></p>}
        </div>
        {noChanges ? (
          <p className="text-xs text-neutral-600 bg-neutral-100 rounded-md px-3 py-2">현재 데이터와 동일하여 변경점이 없습니다.</p>
        ) : (
          <p className="text-xs text-amber-700 bg-amber-50 rounded-md px-3 py-2">
            위 플레이어의 {fieldCount}개 필드를 클립보드 데이터로 일괄 덮어씁니다. 이 작업은 되돌릴 수 없습니다.
          </p>
        )}
        {!compareLoading && sourceStale && (
          <p className="flex items-start gap-1.5 text-xs text-red-700 bg-red-50 rounded-md px-3 py-2">
            <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0" />
            복사한 뒤 원본 플레이어의 데이터가 바뀌었습니다. 지금 붙여넣으면 그 사이의 변경분은 반영되지 않습니다.
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} disabled={loading} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-neutral-50">취소</button>
          <button
            onClick={onConfirm}
            disabled={loading || compareLoading || noChanges}
            className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-blue-600 disabled:opacity-50 inline-flex items-center gap-1.5"
          >
            {loading && <Loader2 className="w-3.5 h-3.5 animate-spin" />}덮어쓰기
          </button>
        </div>
      </div>
    </div>
  )
}

export default function PlayersTab({
  target, onUnauthenticated, initialAccountId, initialName,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  initialAccountId?: string | undefined
  initialName?: string | undefined
}) {
  const [mode, setMode] = useState<'nickname' | 'id'>('nickname')
  const [searchInput, setSearchInput] = useState('')
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<Row | null>(null)

  const [clipboard, setClipboard] = useState<DataClipboard | null>(() => readClipboard())
  const [copyingId, setCopyingId] = useState<string | null>(null)
  const [pasteTarget, setPasteTarget] = useState<Row | null>(null)
  const [pasting, setPasting] = useState(false)
  const [compareLoading, setCompareLoading] = useState(false)
  const [changedCount, setChangedCount] = useState(0)
  const [sourceStale, setSourceStale] = useState(false)
  const copyTimerRef = useRef<number | null>(null)
  useEffect(() => () => { if (copyTimerRef.current) window.clearTimeout(copyTimerRef.current) }, [])

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const search = useCallback(async (q: string) => {
    setLoading(true)
    try {
      setRows(await callAdmin<Row[]>(target, 'dataManagement.searchPlayers', { search: q, mode }))
    } catch (e: unknown) {
      report(e, '검색에 실패했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, mode, report])

  // searchInput 은 의도적으로 뺐다 — 입력마다 재검색하지 않고 mode·target 이 바뀔 때만 다시 돈다.
  useEffect(() => { void search(searchInput) }, [target, mode]) // eslint-disable-line react-hooks/exhaustive-deps

  // 플레이어 상세에서 계정을 지정해 들어온 경우 바로 편집 모달을 연다.
  useEffect(() => {
    if (initialAccountId) setSelected({ account_id: initialAccountId, display_name: initialName || initialAccountId, updated_at: '' })
  }, [initialAccountId, initialName])

  const clipboardMismatch = !!clipboard && clipboard.appKey !== target

  const handleCopy = useCallback(async (row: Row) => {
    setCopyingId(row.account_id)
    try {
      const fields = await fetchFields(target, row.account_id)
      const data = Object.fromEntries(fields.map((f) => [f.column_name, f.value]))
      const cb: DataClipboard = {
        sourceLabel: row.display_name, sourceAccountId: row.account_id, data,
        copiedAt: new Date().toISOString(), updatedAt: row.updated_at, appKey: target,
      }
      localStorage.setItem(CLIPBOARD_KEY, JSON.stringify(cb))
      setClipboard(cb)
      // 연속으로 다른 행을 복사하면 이전 타이머가 방금 켠 체크표시를 꺼버릴 수 있어 — 새로 잡기 전에 지운다.
      if (copyTimerRef.current) window.clearTimeout(copyTimerRef.current)
      copyTimerRef.current = window.setTimeout(() => setCopyingId(null), 1500)
    } catch (e: unknown) {
      report(e, '복사에 실패했습니다.')
      setCopyingId(null)
    }
  }, [target, report])

  useEffect(() => {
    if (!pasteTarget || !clipboard) { setCompareLoading(false); setChangedCount(0); setSourceStale(false); return }
    let cancelled = false
    setCompareLoading(true)
    void (async () => {
      try {
        // 대상과의 차이뿐 아니라, 복사한 뒤 원본 쪽이 바뀌었는지도 함께 확인한다 —
        // 안 그러면 오래된 스냅샷을 아무 경고 없이 덮어쓰게 된다.
        const [targetFields, sourceFields] = await Promise.all([
          fetchFields(target, pasteTarget.account_id),
          fetchFields(target, clipboard.sourceAccountId),
        ])
        const current = Object.fromEntries(targetFields.map((f) => [f.column_name, f.value]))
        const changed = Object.keys(clipboard.data).filter((k) => JSON.stringify(clipboard.data[k]) !== JSON.stringify(current[k])).length
        const sourceNow = Object.fromEntries(sourceFields.map((f) => [f.column_name, f.value]))
        const stale = Object.keys(clipboard.data).some((k) => JSON.stringify(clipboard.data[k]) !== JSON.stringify(sourceNow[k]))
        if (!cancelled) { setChangedCount(changed); setSourceStale(stale) }
      } finally {
        if (!cancelled) setCompareLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [target, pasteTarget, clipboard])

  const handlePaste = useCallback(async () => {
    if (!pasteTarget || !clipboard || clipboardMismatch || changedCount === 0) return
    setPasting(true)
    try {
      await callAdmin(target, 'userData.update', { accountId: pasteTarget.account_id, patch: clipboard.data })
      setPasteTarget(null)
      void search(searchInput)
    } catch (e: unknown) {
      report(e, '붙여넣기에 실패했습니다.')
    } finally {
      setPasting(false)
    }
  }, [target, pasteTarget, clipboard, clipboardMismatch, changedCount, search, searchInput, report])

  const clearClipboard = () => {
    localStorage.removeItem(CLIPBOARD_KEY)
    setClipboard(null)
  }

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-neutral-500">검색 기준</span>
          <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
            <button
              type="button"
              onClick={() => setMode('nickname')}
              className={`px-3 py-1 rounded-md text-xs transition-colors ${mode === 'nickname' ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'}`}
            >
              닉네임
            </button>
            <button
              type="button"
              onClick={() => setMode('id')}
              className={`px-3 py-1 rounded-md text-xs transition-colors ${mode === 'id' ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'}`}
            >
              ID
            </button>
          </div>
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && void search(searchInput)}
            className="flex-1 min-w-[240px] h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
          />
          <button
            onClick={() => void search(searchInput)}
            disabled={loading}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
          >
            {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
          </button>
          <button
            onClick={() => { setSearchInput(''); void search('') }}
            className="h-9 px-3 rounded-md border border-neutral-300 bg-white text-sm hover:bg-neutral-50"
          >
            초기화
          </button>
        </div>
      </WhiteCard>

      {clipboard && (clipboardMismatch ? (
        <div className="flex items-center gap-2 px-4 py-2.5 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">
          <ClipboardPaste className="w-4 h-4 shrink-0" />
          <span className="flex-1">클립보드: <span className="font-medium">{clipboard.sourceLabel}</span> — 다른 프로젝트에서 복사된 데이터라 붙여넣을 수 없습니다.</span>
          <button onClick={clearClipboard} className="p-0.5 rounded hover:bg-red-100"><X className="w-4 h-4" /></button>
        </div>
      ) : (
        <div className="flex items-center gap-2 px-4 py-2.5 bg-blue-50 border border-blue-200 rounded-lg text-sm text-blue-700">
          <ClipboardPaste className="w-4 h-4 shrink-0" />
          <span className="flex-1">클립보드: <span className="font-medium">{clipboard.sourceLabel}</span> ({formatDateTime(clipboard.updatedAt ?? clipboard.copiedAt)})</span>
          <button onClick={clearClipboard} className="p-0.5 rounded hover:bg-blue-100"><X className="w-4 h-4" /></button>
        </div>
      ))}

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-5 py-2.5 font-medium">ID</th>
              <th className="text-left px-5 py-2.5 font-medium">닉네임</th>
              <th className="text-left px-5 py-2.5 font-medium">업데이트 시각</th>
              <th className="text-right px-5 py-2.5 font-medium w-28" />
            </tr>
          </thead>
          <tbody>
            {loading || rows.length === 0 ? (
              <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={4} emptyText="결과가 없습니다." />
            ) : (
              rows.map((r) => (
                <tr key={r.account_id} className="border-t border-neutral-100 hover:bg-neutral-50/50">
                  <td className="px-5 py-3 font-mono text-xs text-neutral-600">{r.account_id}</td>
                  <td className="px-5 py-3">{r.display_name}</td>
                  <td className="px-5 py-3 text-neutral-600">{formatDateTime(r.updated_at)}</td>
                  <td className="px-5 py-3 text-right">
                    <div className="inline-flex items-center gap-1">
                      <button
                        onClick={() => void handleCopy(r)}
                        className={`inline-flex items-center justify-center w-7 h-7 rounded hover:bg-neutral-100 ${copyingId === r.account_id ? 'text-green-600' : 'text-neutral-500 hover:text-[#1677ff]'}`}
                        title="클립보드에 복사"
                      >
                        {copyingId === r.account_id ? <span className="text-[10px] font-bold">✓</span> : <Copy className="w-3.5 h-3.5" />}
                      </button>
                      <button
                        onClick={() => { if (clipboard && !clipboardMismatch) setPasteTarget(r) }}
                        disabled={!clipboard || clipboardMismatch}
                        className={`inline-flex items-center justify-center w-7 h-7 rounded ${clipboard && !clipboardMismatch ? 'text-neutral-500 hover:bg-neutral-100 hover:text-[#1677ff]' : 'text-neutral-300 cursor-not-allowed'}`}
                        title={!clipboard ? '클립보드가 비어있습니다' : clipboardMismatch ? '다른 프로젝트에서 복사된 데이터입니다' : '클립보드 데이터 붙여넣기'}
                      >
                        <ClipboardPaste className="w-3.5 h-3.5" />
                      </button>
                      <button
                        onClick={() => setSelected(r)}
                        className="inline-flex items-center justify-center w-7 h-7 rounded hover:bg-neutral-100 text-neutral-500 hover:text-[#1677ff]"
                      >
                        <Pencil className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
        <div className="border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          {rows.length} result{rows.length === 1 ? '' : 's'}
        </div>
      </WhiteCard>

      {selected && (
        <PlayerEditModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={selected.account_id}
          displayName={selected.display_name}
          onClose={() => setSelected(null)}
          onChanged={() => void search(searchInput)}
        />
      )}

      {pasteTarget && clipboard && (
        <PasteConfirmModal
          clipboard={clipboard}
          targetRow={pasteTarget}
          onConfirm={() => void handlePaste()}
          onClose={() => setPasteTarget(null)}
          loading={pasting}
          compareLoading={compareLoading}
          changedCount={changedCount}
          sourceStale={sourceStale}
        />
      )}
    </div>
  )
}
