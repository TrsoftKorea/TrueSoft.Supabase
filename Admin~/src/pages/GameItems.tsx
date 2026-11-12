import { useCallback, useEffect, useState } from 'react'
import { Check, Loader2, Pencil, Plus, Trash2, X } from 'lucide-react'
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

type Item = { key: string; display_name: string; created_at: string }
type FormItem = { key: string; display_name: string }
const EMPTY: FormItem = { key: '', display_name: '' }

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

  const add = async () => {
    const key = form.key.trim()
    if (!key || adding) return
    // 서버는 같은 코드면 이름을 덮어쓴다 — 추가 창에서 기존 아이템 이름이 몰래 바뀌지 않게 막는다.
    if (rows.some((r) => r.key === key)) {
      setAddError('이미 있는 아이템 코드입니다.')
      return
    }
    setAdding(true)
    setAddError('')
    try {
      await callAdmin(target, 'gameItems.upsert', { key, displayName: form.display_name })
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
          <Button variant="primary" onClick={() => { setForm(EMPTY); setAddError(''); setAddOpen(true) }}>
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
            <Button variant="primary" onClick={() => void add()} disabled={adding || !form.key.trim()}>
              {adding && <Loader2 className="w-4 h-4 animate-spin" />}추가
            </Button>
          </>}
        >
          <div className="space-y-4">
            <label className="flex flex-col gap-1">
              <span className="text-xs font-medium text-neutral-500">코드</span>
              <input
                value={form.key}
                onChange={(e) => setForm({ ...form, key: e.target.value })}
                onKeyDown={(e) => e.key === 'Enter' && void add()}
                className={inputCls + ' w-full'}
                autoFocus
              />
            </label>
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
