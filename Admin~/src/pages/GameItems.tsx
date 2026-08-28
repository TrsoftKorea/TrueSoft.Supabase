import { useCallback, useEffect, useState } from 'react'
import { Check, Loader2, Pencil, Plus, Trash2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { formatDateTime } from '../components/ui/format'

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
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [rowSaving, setRowSaving] = useState(false)
  const [delTarget, setDelTarget] = useState<Item | null>(null)
  const [deleting, setDeleting] = useState(false)

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
    try {
      setRows(await callAdmin<Item[]>(target, 'gameItems.list'))
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setRows([])
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

  const add = () => {
    if (!form.key.trim() || adding) return
    void run(async () => {
      await callAdmin(target, 'gameItems.upsert', {
        key: form.key.trim(),
        displayName: form.display_name,
      })
      setForm(EMPTY)
    }, setAdding)
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

  const confirmDelete = () => {
    if (!delTarget || deleting) return
    const key = delTarget.key
    void run(async () => {
      await callAdmin(target, 'gameItems.delete', { key })
      if (editingKey === key) cancelEdit()
      setDelTarget(null)
    }, setDeleting)
  }

  const inputCls = 'h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white'

  return (
    <div className="space-y-4">
      <PageHeader
        title="아이템 카탈로그"
        description="우편·쿠폰 등 아이템을 지급하는 기능에서 공통으로 사용하는 아이템 목록입니다."
      />

      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
          {error}
        </div>
      )}

      <WhiteCard className="p-4">
        <div className="text-xs font-medium text-neutral-500 mb-2">새 아이템 추가</div>
        <div className="grid grid-cols-[1fr_1fr_auto] gap-2 items-end">
          <div className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">코드</span>
            <input
              value={form.key}
              onChange={(e) => setForm({ ...form, key: e.target.value })}
              onKeyDown={(e) => e.key === 'Enter' && add()}
              className={inputCls}
            />
          </div>
          <div className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">이름</span>
            <input
              value={form.display_name}
              onChange={(e) => setForm({ ...form, display_name: e.target.value })}
              onKeyDown={(e) => e.key === 'Enter' && add()}
              className={inputCls}
            />
          </div>
          <button
            onClick={add}
            disabled={adding || !form.key.trim()}
            className="inline-flex items-center justify-center gap-1 h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-50"
          >
            {adding ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
            추가
          </button>
        </div>
      </WhiteCard>

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-4 py-2.5 font-medium">코드</th>
              <th className="text-left px-4 py-2.5 font-medium">이름</th>
              <th className="text-left px-4 py-2.5 font-medium w-40">등록일자</th>
              <th className="px-4 py-2.5 w-24"></th>
            </tr>
          </thead>
          <tbody>
            {loading || rows.length === 0 ? (
              <TableStatusRow
                loading={loading}
                empty={rows.length === 0}
                colSpan={4}
                emptyText="등록된 아이템이 없습니다."
              />
            ) : (
              rows.map((it) => {
                const isEditing = editingKey === it.key
                return (
                  <tr key={it.key} className="border-t border-neutral-100 hover:bg-neutral-50">
                    <td className="px-4 py-2.5 font-mono text-xs">{it.key}</td>
                    <td className="px-4 py-2.5">
                      {isEditing ? (
                        <input
                          value={editName}
                          onChange={(e) => setEditName(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') saveEdit(it.key)
                            if (e.key === 'Escape') cancelEdit()
                          }}
                          autoFocus
                          className="h-8 px-2 rounded border border-neutral-300 text-sm w-full max-w-xs focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
                        />
                      ) : (
                        <span className="text-neutral-800">{it.display_name || '-'}</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-neutral-600">{formatDateTime(it.created_at)}</td>
                    <td className="px-4 py-2.5">
                      <div className="flex items-center justify-end gap-3">
                        {isEditing ? (
                          <>
                            <button
                              onClick={() => saveEdit(it.key)}
                              disabled={rowSaving}
                              className="text-emerald-600 hover:text-emerald-700 disabled:opacity-50"
                              aria-label="확인"
                            >
                              {rowSaving ? (
                                <Loader2 className="w-4 h-4 animate-spin" />
                              ) : (
                                <Check className="w-4 h-4" />
                              )}
                            </button>
                            <button
                              onClick={cancelEdit}
                              className="text-neutral-400 hover:text-neutral-700"
                              aria-label="취소"
                            >
                              <X className="w-4 h-4" />
                            </button>
                          </>
                        ) : (
                          <>
                            <button
                              onClick={() => startEdit(it)}
                              className="text-neutral-400 hover:text-[#1677ff]"
                              aria-label="수정"
                            >
                              <Pencil className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => setDelTarget(it)}
                              className="text-neutral-400 hover:text-red-500"
                              aria-label="삭제"
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })
            )}
          </tbody>
        </table>
      </WhiteCard>

      <ConfirmDialog
        open={!!delTarget}
        title="아이템 지우기"
        confirmLabel="지우기"
        danger
        busy={deleting}
        onConfirm={confirmDelete}
        onCancel={() => setDelTarget(null)}
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
