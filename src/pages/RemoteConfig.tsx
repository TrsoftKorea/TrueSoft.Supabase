import { useEffect, useMemo, useState, useCallback } from 'react'
import { Plus, RefreshCw, Pencil, Trash2, X, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { WhiteCard } from '../components/ui/Card'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { PendingChanges, type PendingItem } from '../components/ui/PendingChanges'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { opLabel } from '../lib/schemaLabels'

type ConfigRow = {
  key: string
  value_json: Record<string, unknown>
  enabled: boolean
  requires_auth: boolean
  description: string | null
  version: number
}
type DraftRow = { id: number; feature: string; action: string; object_name: string; params: Record<string, unknown> }
type Effective = { value_json: Record<string, unknown>; enabled: boolean; requires_auth: boolean; deleted: boolean; pendingId: number | null }

type ValueType = 'string' | 'number' | 'boolean' | 'json'
const detectType = (v: unknown): ValueType => {
  if (typeof v === 'boolean') return 'boolean'
  if (typeof v === 'number') return 'number'
  if (typeof v === 'string') return 'string'
  return 'json'
}
const formatItemValue = (v: unknown): string => (typeof v === 'string' ? v : JSON.stringify(v))

function NewConfigModal({ target, onUnauthenticated, onClose, onCreated }: {
  target: ProjectTarget
  onUnauthenticated: () => void
  onClose: () => void
  onCreated: () => void
}) {
  const [key, setKey] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [requiresAuth, setRequiresAuth] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async () => {
    if (!key.trim() || loading) return
    setLoading(true); setError('')
    try {
      await callAdmin(target, 'remoteConfig.stageNew', { key: key.trim(), enabled, requiresAuth })
      onCreated()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">설정 추가</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          <div>
            <label className="block text-xs text-neutral-500 mb-1">키</label>
            <input value={key} onChange={(e) => setKey(e.target.value)} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} className="rounded" />
            활성
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={requiresAuth} onChange={(e) => setRequiresAuth(e.target.checked)} className="rounded" />
            인증 필요
          </label>
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button onClick={() => { void submit() }} disabled={loading || !key.trim()} className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60">
            {loading ? '추가 중…' : '추가'}
          </button>
        </div>
      </div>
    </div>
  )
}

function ItemModal({ target, onUnauthenticated, configKey, itemKey, currentValue, onClose, onSaved }: {
  target: ProjectTarget
  onUnauthenticated: () => void
  configKey: string
  itemKey: string | null
  currentValue: unknown
  onClose: () => void
  onSaved: () => void
}) {
  const isNew = itemKey === null
  const [key, setKey] = useState(itemKey ?? '')
  const [type, setType] = useState<ValueType>(() => (isNew ? 'string' : detectType(currentValue)))
  const [text, setText] = useState(() => {
    if (isNew) return ''
    if (type === 'json') return JSON.stringify(currentValue, null, 2)
    return String(currentValue)
  })
  const [boolValue, setBoolValue] = useState(() => (typeof currentValue === 'boolean' ? currentValue : true))
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async () => {
    if (!key.trim() || loading) return
    setError('')
    let value: unknown
    if (type === 'boolean') {
      value = boolValue
    } else if (type === 'json') {
      try { value = text.trim() === '' ? null : JSON.parse(text) } catch { setError('올바른 JSON이 아닙니다.'); return }
    } else if (type === 'number') {
      const n = Number(text)
      if (text.trim() === '' || Number.isNaN(n)) { setError('숫자를 입력하세요.'); return }
      value = n
    } else {
      value = text
    }
    setLoading(true)
    try {
      await callAdmin(target, 'remoteConfig.stageItem', { key: configKey, itemKey: key.trim(), itemValue: value })
      onSaved()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '저장에 실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">{isNew ? '필드 추가' : `필드 수정 — ${itemKey}`}</h3>
          <button onClick={onClose} className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          {isNew && (
            <div>
              <label className="block text-xs text-neutral-500 mb-1">필드명</label>
              <input value={key} onChange={(e) => setKey(e.target.value)} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
            </div>
          )}
          <div>
            <label className="block text-xs text-neutral-500 mb-1">타입</label>
            <select
              value={type}
              onChange={(e) => { setType(e.target.value as ValueType); setText('') }}
              className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
            >
              <option value="string">문자열</option>
              <option value="number">숫자</option>
              <option value="boolean">불리언</option>
              <option value="json">JSON</option>
            </select>
          </div>
          {type === 'boolean' ? (
            <select value={boolValue ? 'true' : 'false'} onChange={(e) => setBoolValue(e.target.value === 'true')} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]">
              <option value="true">참</option>
              <option value="false">거짓</option>
            </select>
          ) : type === 'json' ? (
            <textarea value={text} onChange={(e) => setText(e.target.value)} rows={6} className="w-full px-3 py-2 rounded-md border border-neutral-300 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]" />
          ) : (
            <input
              type={type === 'number' ? 'number' : 'text'}
              value={text}
              onChange={(e) => setText(e.target.value)}
              className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
            />
          )}
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <button onClick={onClose} className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white">취소</button>
          <button onClick={() => { void submit() }} disabled={loading || !key.trim()} className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60">
            {loading ? '저장 중…' : '저장'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function RemoteConfig({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<ConfigRow[]>([])
  const [drafts, setDrafts] = useState<DraftRow[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedKey, setSelectedKey] = useState('')
  const [showNew, setShowNew] = useState(false)
  const [deleteKey, setDeleteKey] = useState('')
  const [itemModal, setItemModal] = useState<{ itemKey: string | null; value: unknown } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const reload = useCallback(async () => {
    setLoading(true)
    try {
      const [cfg, draft] = await Promise.all([
        callAdmin<{ rows: ConfigRow[] }>(target, 'remoteConfig.list'),
        callAdmin<{ rows: DraftRow[] }>(target, 'schema.getDraft'),
      ])
      setRows(cfg.rows)
      setDrafts(draft.rows.filter((d) => d.feature === 'remote_config'))
    } catch (e: unknown) {
      report(e, '원격 설정을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void reload() }, [reload])

  const draftOf = (key: string) => drafts.find((d) => d.object_name === key)

  const effectiveOf = (key: string): Effective => {
    const d = draftOf(key)
    const row = rows.find((r) => r.key === key)
    if (d) {
      if (d.action === 'delete') return { value_json: row?.value_json ?? {}, enabled: row?.enabled ?? true, requires_auth: row?.requires_auth ?? false, deleted: true, pendingId: d.id }
      const p = d.params
      return {
        value_json: (p['value_json'] as Record<string, unknown>) ?? {},
        enabled: p['enabled'] !== false,
        requires_auth: p['requires_auth'] === true,
        deleted: false,
        pendingId: d.id,
      }
    }
    return { value_json: row?.value_json ?? {}, enabled: row?.enabled ?? true, requires_auth: row?.requires_auth ?? false, deleted: false, pendingId: null }
  }

  // 라이브에 있거나 아직 대기(add) 중인 키를 모두 보여준다.
  const keys = useMemo(() => {
    const set = new Set(rows.map((r) => r.key))
    drafts.forEach((d) => set.add(d.object_name))
    return Array.from(set).sort()
  }, [rows, drafts])

  const pendingItems: PendingItem[] = drafts.map((d) => ({ id: d.id, label: opLabel(d.feature, d.action), detail: d.object_name }))

  const discardOne = async (id: number) => {
    setBusy(true)
    try { await callAdmin(target, 'schema.discardDraft', { id }); await reload() }
    catch (e: unknown) { report(e, '실패했습니다.') }
    finally { setBusy(false) }
  }

  const toggleMeta = async (key: string, field: 'enabled' | 'requires_auth') => {
    const eff = effectiveOf(key)
    setBusy(true)
    try {
      await callAdmin(target, 'remoteConfig.stageMeta', {
        key,
        enabled: field === 'enabled' ? !eff.enabled : eff.enabled,
        requiresAuth: field === 'requires_auth' ? !eff.requires_auth : eff.requires_auth,
      })
      await reload()
    } catch (e: unknown) { report(e, '실패했습니다.') }
    finally { setBusy(false) }
  }

  const confirmDelete = async () => {
    setBusy(true)
    try {
      await callAdmin(target, 'remoteConfig.stageDelete', { key: deleteKey })
      setDeleteKey('')
      if (selectedKey === deleteKey) setSelectedKey('')
      await reload()
    } catch (e: unknown) { report(e, '실패했습니다.') }
    finally { setBusy(false) }
  }

  const deleteItem = async (key: string, itemKey: string) => {
    setBusy(true)
    try { await callAdmin(target, 'remoteConfig.stageItemDelete', { key, itemKey }); await reload() }
    catch (e: unknown) { report(e, '실패했습니다.') }
    finally { setBusy(false) }
  }

  const selected = selectedKey ? effectiveOf(selectedKey) : null
  const selectedItems = selected ? Object.entries(selected.value_json).filter(([k]) => k !== '__meta') : []

  return (
    <div className="space-y-4">
      <PageHeader title="원격 설정" description="게임 클라이언트가 읽는 키-값 설정을 관리합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <PendingChanges items={pendingItems} onDiscard={(id) => void discardOne(id)} busy={busy} />

      <div className="flex justify-end">
        <button onClick={() => setShowNew(true)} className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90">
          <Plus className="w-4 h-4" />설정 추가
        </button>
      </div>

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-5 py-2.5 font-medium">키</th>
              <th className="text-left px-4 py-2.5 font-medium">상태</th>
              <th className="text-left px-4 py-2.5 font-medium">필드 수</th>
              <th className="px-3 py-2.5 w-20" />
            </tr>
          </thead>
          <tbody>
            {loading || keys.length === 0 ? (
              <TableStatusRow loading={loading} empty={keys.length === 0} colSpan={4} emptyText="설정이 없습니다." />
            ) : (
              keys.map((key) => {
                const eff = effectiveOf(key)
                const itemCount = Object.keys(eff.value_json).filter((k) => k !== '__meta').length
                return (
                  <tr
                    key={key}
                    onClick={() => setSelectedKey(key)}
                    className={`border-t border-neutral-100 cursor-pointer hover:bg-neutral-50 ${selectedKey === key ? 'bg-[#e8f0fe]' : ''}`}
                  >
                    <td className="px-5 py-3 font-medium font-mono">{key}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1.5">
                        {eff.deleted ? (
                          <span className="px-2 py-0.5 text-xs rounded bg-red-100 text-red-700">삭제 대기</span>
                        ) : (
                          <span className={`px-2 py-0.5 text-xs rounded ${eff.enabled ? 'bg-emerald-100 text-emerald-700' : 'bg-neutral-100 text-neutral-600'}`}>
                            {eff.enabled ? '활성' : '비활성'}
                          </span>
                        )}
                        {eff.requires_auth && <span className="px-2 py-0.5 text-xs rounded bg-[#e8f0fe] text-[#1677ff]">인증 필요</span>}
                        {eff.pendingId != null && !eff.deleted && <span className="px-2 py-0.5 text-xs rounded bg-amber-100 text-amber-700">대기 중</span>}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-neutral-600">{itemCount}</td>
                    <td className="px-3 py-3 text-right" onClick={(e) => e.stopPropagation()}>
                      <button
                        onClick={() => setDeleteKey(key)}
                        className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </td>
                  </tr>
                )
              })
            )}
          </tbody>
        </table>
      </WhiteCard>

      {selected && selectedKey && (
        <WhiteCard className="p-5 space-y-4">
          <div className="flex items-center justify-between">
            <h3 className="text-base font-semibold font-mono">{selectedKey}</h3>
            <button onClick={() => void reload()} className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50">
              <RefreshCw className="w-3.5 h-3.5" />새로고침
            </button>
          </div>

          <div className="flex flex-wrap items-center gap-6">
            <label className="flex items-center gap-2 text-sm cursor-pointer">
              <input type="checkbox" checked={selected.enabled} disabled={busy || selected.deleted} onChange={() => void toggleMeta(selectedKey, 'enabled')} className="rounded" />
              활성
            </label>
            <label className="flex items-center gap-2 text-sm cursor-pointer">
              <input type="checkbox" checked={selected.requires_auth} disabled={busy || selected.deleted} onChange={() => void toggleMeta(selectedKey, 'requires_auth')} className="rounded" />
              인증 필요
            </label>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm font-medium text-neutral-800">필드</span>
            <button
              onClick={() => setItemModal({ itemKey: null, value: undefined })}
              disabled={selected.deleted}
              className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50 disabled:opacity-50"
            >
              <Plus className="w-3.5 h-3.5" />필드 추가
            </button>
          </div>

          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2 font-medium">필드명</th>
                <th className="text-left px-4 py-2 font-medium">값</th>
                <th className="px-3 py-2 w-20" />
              </tr>
            </thead>
            <tbody>
              {selectedItems.length === 0 ? (
                <tr><td colSpan={3} className="px-4 py-6 text-center text-neutral-500">필드가 없습니다.</td></tr>
              ) : (
                selectedItems.map(([k, v]) => (
                  <tr key={k} className="border-t border-neutral-100">
                    <td className="px-4 py-2.5 font-mono">{k}</td>
                    <td className="px-4 py-2.5 text-neutral-600 font-mono text-xs max-w-[360px] truncate" title={formatItemValue(v)}>{formatItemValue(v)}</td>
                    <td className="px-3 py-2.5">
                      <div className="flex items-center gap-1 justify-end">
                        <button onClick={() => setItemModal({ itemKey: k, value: v })} disabled={selected.deleted} className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700 disabled:opacity-40">
                          <Pencil className="w-3.5 h-3.5" />
                        </button>
                        <button onClick={() => void deleteItem(selectedKey, k)} disabled={selected.deleted || busy} className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500 disabled:opacity-40">
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </WhiteCard>
      )}

      {showNew && (
        <NewConfigModal target={target} onUnauthenticated={onUnauthenticated} onClose={() => setShowNew(false)} onCreated={() => { setShowNew(false); void reload() }} />
      )}

      {itemModal && selectedKey && (
        <ItemModal
          target={target}
          onUnauthenticated={onUnauthenticated}
          configKey={selectedKey}
          itemKey={itemModal.itemKey}
          currentValue={itemModal.value}
          onClose={() => setItemModal(null)}
          onSaved={() => { setItemModal(null); void reload() }}
        />
      )}

      <ConfirmDialog
        open={!!deleteKey}
        title="설정 삭제"
        confirmLabel="삭제"
        danger
        busy={busy}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDeleteKey('')}
        description="게시하면 이 키와 모든 필드가 삭제됩니다. 변경 관리에서 게시 전까지는 되돌릴 수 있습니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm font-mono">{deleteKey}</div>
      </ConfirmDialog>

      {loading && rows.length === 0 && (
        <div className="flex justify-center py-4"><Loader2 className="w-5 h-5 animate-spin text-neutral-300" /></div>
      )}
    </div>
  )
}
