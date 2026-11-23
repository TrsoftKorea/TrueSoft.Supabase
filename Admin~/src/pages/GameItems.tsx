import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Check, CircleHelp, Loader2, Pencil, Plus, Trash2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { EditDialog } from '../components/ui/EditDialog'
import { inputCls, inputCompactCls } from '../components/ui/Input'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableCard } from '../components/ui/DataTable'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { formatDateTime } from '../components/ui/format'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { StatusText } from '../components/ui/StatusText'
import { isBrokenGrant, type GrantMode } from '../lib/gameItems'

type Item = { key: string; display_name: string; created_at: string; source?: 'save' | 'manual'; grant_mode?: GrantMode }
// grantable: 서버가 수량을 더할 수 있는 숫자 데이터 필드. 예약 필드(id·account_id)와 숫자가 아닌 필드는 false.
type DataField = { column_name: string; grantable: boolean; registered: boolean }
type FormItem = { key: string; display_name: string }
const EMPTY: FormItem = { key: '', display_name: '' }

const GRANT_LABEL: Record<GrantMode, ReactNode> = {
  auto: <StatusText tone="ok">자동 지급</StatusText>,
  game: <StatusText tone="plain">게임 처리</StatusText>,
  missing: <StatusText tone="bad">필드 없음</StatusText>,
  blocked: <StatusText tone="bad">형식 다름</StatusText>,
}
// 콘솔은 게임에 처리 코드가 등록됐는지 알 수 없다 — 그 한계를 물음표에 숨겨 둔다.
const GRANT_HELP = [
  '자동 지급: 플레이어가 받으면 같은 이름의 숫자 데이터 필드에 수량이 더해집니다. 게임이 이 코드에 처리 코드를 넣어 두었으면 그 코드가 대신 지급합니다.',
  '게임 처리: 게임에 이 코드의 처리 코드가 있어야 받을 수 있습니다.',
  '필드 없음: 이 이름의 데이터 필드가 사라졌습니다. 게임에 처리 코드가 없으면 받지 못합니다.',
  '형식 다름: 이 이름의 데이터 필드가 숫자가 아닙니다. 게임에 처리 코드가 없으면 받지 못합니다.',
  '콘솔은 게임에 처리 코드가 있는지 알 수 없습니다.',
].join('\n')

export default function GameItems({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Item[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [form, setForm] = useState<FormItem>(EMPTY)
  const [adding, setAdding] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  // 추가 실패는 창 안에 보인다 — 페이지 띠는 창 뒤에 가린다.
  const [addError, setAddError] = useState('')
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [rowSaving, setRowSaving] = useState(false)
  const [delTarget, setDelTarget] = useState<Item | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState('')
  const [loadFailed, setLoadFailed] = useState(false)
  const [sugIndex, setSugIndex] = useState(-1)
  // null = 불러오는 중. 실패는 colError 로 따로 — 실패를 "제안할 필드가 없음"으로 보이게 하지 않는다.
  const [columns, setColumns] = useState<DataField[] | null>(null)
  const [colError, setColError] = useState('')
  const colReq = useRef(0)

  // 토큰 만료는 화면에 오류로 띄우지 않고 로그인으로 돌려보낸다.
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

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    setLoadFailed(false)
    try {
      setRows(await callAdmin<Item[]>(target, 'gameItems.list'))
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setRows([])
      setLoadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => {
    void load()
  }, [load])

  const run = async (fn: () => Promise<void>, setBusy: (b: boolean) => void) => {
    setBusy(true)
    setError('')
    try {
      await fn()
      await load()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setBusy(false)
    }
  }

  // 창을 다시 열면 앞서 보낸 요청의 늦은 응답은 버린다.
  const loadColumns = async () => {
    const req = ++colReq.current
    setColumns(null)
    setColError('')
    try {
      const cols = await callAdmin<DataField[]>(target, 'gameItems.columns')
      if (req === colReq.current) setColumns(cols)
    } catch (e: unknown) {
      if (req !== colReq.current) return
      if (e instanceof NotAuthenticatedError) { setAddOpen(false); onUnauthenticated(); return }
      setColError(e instanceof Error ? e.message : '데이터 필드를 불러오지 못했습니다.')
    }
  }

  const openAdd = () => {
    setForm(EMPTY)
    setAddError('')
    setSugIndex(-1)
    setAddOpen(true)
    void loadColumns()
  }

  const brokenCount = rows.filter((r) => isBrokenGrant(r.grant_mode)).length

  const typedKey = form.key.trim()
  // 서버 판정은 정확 일치다 — 대소문자가 다르면 다른 필드다(Gold 는 gold 필드가 아님).
  const exactField = columns?.find((c) => c.column_name === typedKey)
  const isSaveColumn = !!exactField?.grantable
  // 숫자가 아닌 데이터 필드 이름은 서버가 새 코드로 받지 않는다 — 미리 막는다.
  const isNonNumeric = !!exactField && !exactField.grantable
  const isDuplicate = rows.some((r) => r.key === typedKey)
  const suggestions = typedKey
    ? (columns ?? []).filter((c) => c.grantable && !c.registered && c.column_name !== typedKey
        && c.column_name.toLowerCase().includes(typedKey.toLowerCase()))
    : []

  const pick = (name: string) => {
    setForm({ ...form, key: name })
    setSugIndex(-1)
  }

  const onKeyKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (suggestions.length > 0 && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
      e.preventDefault()
      const step = e.key === 'ArrowDown' ? 1 : -1
      setSugIndex((i) => (i + step + suggestions.length) % suggestions.length)
      return
    }
    if (e.key !== 'Enter') return
    if (sugIndex >= 0 && suggestions[sugIndex]) pick(suggestions[sugIndex].column_name)
    else void add()
  }

  // 코드 아래 한 줄 — 등록 전에 지급 방식을 알게 한다. 필드 목록을 못 불러왔으면 지급 방식 대신 그 사실을 보인다.
  const keyStatus = colError ? (
    <span className="text-xs text-red-600">데이터 필드를 불러오지 못했습니다. {colError}</span>
  ) : !typedKey || columns === null ? null : isDuplicate ? (
    <span className="text-xs text-red-600">이미 있는 아이템 코드입니다.</span>
  ) : isNonNumeric ? (
    <span className="text-xs text-red-600">숫자가 아닌 데이터 필드 이름이라 쓸 수 없습니다.</span>
  ) : (
    <span className="text-xs text-neutral-500 inline-flex items-center gap-1.5">
      지급 방식 {isSaveColumn ? <StatusText tone="ok">자동 지급</StatusText> : <StatusText tone="plain">게임 처리</StatusText>}
    </span>
  )

  const add = async () => {
    const key = form.key.trim()
    // 필드 목록이 오기 전에는 데이터 필드인지 몰라 source 를 정할 수 없다. 실패했으면 manual 로 보낸다(형식 다름은 서버가 거절).
    if (!key || adding || (columns === null && !colError) || isNonNumeric) return
    // 서버는 같은 코드면 이름을 덮어쓴다 — 추가 창에서 기존 아이템 이름이 몰래 바뀌지 않게 막는다.
    if (rows.some((r) => r.key === key)) {
      setAddError('이미 있는 아이템 코드입니다.')
      return
    }
    setAdding(true)
    setAddError('')
    try {
      await callAdmin(target, 'gameItems.upsert', { key, displayName: form.display_name, source: isSaveColumn ? 'save' : 'manual' })
      setForm(EMPTY)
      setAddOpen(false)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { setAddOpen(false); onUnauthenticated(); return }
      setAddError(e instanceof Error ? e.message : '추가하지 못했습니다.')
    } finally {
      setAdding(false)
    }
  }

  const startEdit = (it: Item) => {
    setEditingKey(it.key)
    setEditName(it.display_name)
  }
  const cancelEdit = () => {
    setEditingKey(null)
    setEditName('')
  }
  const saveEdit = (key: string) => {
    if (rowSaving) return
    void run(async () => {
      await callAdmin(target, 'gameItems.upsert', { key, displayName: editName })
      cancelEdit()
    }, setRowSaving)
  }

  const confirmDelete = async () => {
    if (!delTarget || deleting) return
    const key = delTarget.key
    setDeleting(true)
    setDeleteError('')
    try {
      await callAdmin(target, 'gameItems.delete', { key })
      if (editingKey === key) cancelEdit()
      setDelTarget(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) onUnauthenticated()
      else setDeleteError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setDeleting(false)
    }
  }


  return (
    <div className="space-y-4">
      <PageHeader
        title="아이템 카탈로그"
        description="우편·쿠폰으로 지급할 아이템을 등록합니다."
        actions={<>
          <RefreshButton onClick={() => void load()} loading={loading} />
          <Button variant="primary" onClick={openAdd} disabled={loadFailed}>
            <Plus className="w-4 h-4" />아이템 추가
          </Button>
        </>}
      />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      {addOpen && (
        <EditDialog
          title="아이템 추가"
          busy={adding}
          onClose={() => setAddOpen(false)}
          error={addError || undefined}
          footer={<>
            <Button variant="outline" onClick={() => setAddOpen(false)} disabled={adding}>취소</Button>
            <Button variant="primary" onClick={() => void add()} disabled={adding || !form.key.trim() || (columns === null && !colError) || isNonNumeric || isDuplicate}>
              {adding && <Loader2 className="w-4 h-4 animate-spin" />}추가
            </Button>
          </>}
        >
          <div className="space-y-4">
            <div className="flex flex-col gap-1">
              <label htmlFor="item-key" className="text-xs font-medium text-neutral-500">코드</label>
              <input
                id="item-key"
                value={form.key}
                onChange={(e) => { setForm({ ...form, key: e.target.value }); setSugIndex(-1) }}
                onKeyDown={onKeyKeyDown}
                className={inputCls + ' w-full font-mono'}
                autoComplete="off"
                role="combobox"
                aria-expanded={suggestions.length > 0}
                aria-controls="item-key-suggestions"
                autoFocus
              />
              {suggestions.length > 0 && (
                <ul id="item-key-suggestions" role="listbox"
                  className="rounded-md border border-neutral-200 bg-surface py-1 max-h-48 overflow-y-auto">
                  {suggestions.map((c, i) => (
                    <li key={c.column_name} role="option" aria-selected={i === sugIndex}>
                      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => pick(c.column_name)}
                        className={`w-full text-left px-3 py-1.5 font-mono text-sm hover:bg-neutral-50 ${i === sugIndex ? 'bg-brand-soft' : ''}`}>
                        {c.column_name}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              {keyStatus}
            </div>
            <label className="flex flex-col gap-1">
              <span className="text-xs font-medium text-neutral-500">이름</span>
              <input
                value={form.display_name}
                onChange={(e) => setForm({ ...form, display_name: e.target.value })}
                onKeyDown={(e) => e.key === 'Enter' && void add()}
                className={inputCls + ' w-full'}
              />
            </label>
          </div>
        </EditDialog>
      )}

      {brokenCount > 0 && (
        <div className="text-sm"><StatusText tone="bad">필드 없음·형식 다름 {brokenCount}개</StatusText></div>
      )}

      <TableCard
        columns={[
          { key: 'key', header: '코드', kind: 'mono', cell: (it) => it.key },
          {
            key: 'name', header: '이름', kind: 'name', cell: (it) => editingKey === it.key ? (
              <input
                value={editName}
                onChange={(e) => setEditName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') saveEdit(it.key)
                  if (e.key === 'Escape') cancelEdit()
                }}
                autoFocus
                className={inputCompactCls + ' w-full max-w-xs'}
              />
            ) : it.display_name || null,
          },
          {
            key: 'grant', kind: 'status',
            header: (
              <span className="inline-flex items-center gap-1">
                지급 방식
                <span title={GRANT_HELP} aria-label={GRANT_HELP} className="cursor-help text-neutral-400">
                  <CircleHelp className="w-3.5 h-3.5" />
                </span>
              </span>
            ),
            cell: (it) => it.grant_mode ? GRANT_LABEL[it.grant_mode] : null,
          },
          { key: 'created', header: '등록 시각', kind: 'datetime', cell: (it) => formatDateTime(it.created_at) },
          {
            key: 'actions', header: '', kind: 'actions', cell: (it) => editingKey === it.key ? (
              <span className="inline-flex gap-1.5">
                <button
                  onClick={() => saveEdit(it.key)}
                  disabled={rowSaving}
                  className="text-emerald-600 hover:text-emerald-700 disabled:opacity-50"
                  aria-label="확인"
                  title="저장"
                >
                  {rowSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Check className="w-4 h-4" />}
                </button>
                <button
                  onClick={cancelEdit}
                  disabled={rowSaving}
                  className="text-neutral-400 hover:text-neutral-700 disabled:opacity-50"
                  aria-label="취소"
                  title="취소"
                >
                  <X className="w-4 h-4" />
                </button>
              </span>
            ) : (
              <span className="inline-flex gap-1.5">
                <button
                  onClick={() => startEdit(it)}
                  className="text-neutral-400 hover:text-[#1677ff]"
                  aria-label="수정"
                  title="이름 수정"
                >
                  <Pencil className="w-4 h-4" />
                </button>
                <button
                  onClick={() => setDelTarget(it)}
                  className="text-neutral-400 hover:text-red-500"
                  aria-label="삭제"
                  title="삭제"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </span>
            ),
          },
        ]}
        rows={rows}
        rowKey={(it) => it.key}
        loading={loading}
        failed={loadFailed}
        emptyText="등록된 아이템이 없습니다."
      />

      <ConfirmDialog
        open={!!delTarget}
        title="아이템 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        error={deleteError}
        onConfirm={() => void confirmDelete()}
        onCancel={() => { setDelTarget(null); setDeleteError('') }}
        description="이미 발송된 우편·쿠폰의 아이템은 그대로 남고, 앞으로 이 코드를 고를 수 없게 됩니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.display_name || '(이름 없음)'}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{delTarget.key}</div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
