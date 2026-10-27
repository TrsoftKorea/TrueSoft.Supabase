import { useEffect, useState } from 'react'
import { Loader2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { Button } from '../../components/ui/Button'
import { TargetGameStrip } from '../../components/ui/TargetGameStrip'
import { formatDateTime } from '../../components/ui/format'

type PreviewRow = { column: string; data_type: string; current: unknown; restored: unknown }

function show(v: unknown): string {
  if (v === null || v === undefined) return '—'
  if (typeof v === 'object') return JSON.stringify(v)
  if (typeof v === 'boolean') return v ? '참' : '거짓'
  return String(v)
}

/** 세이브를 어떤 변경 직전 상태로 되돌린다. 바뀌는 칸을 먼저 보여 주고, 고른 칸만 쓴다. */
export function RestoreSaveModal({
  target,
  onUnauthenticated,
  accountId,
  logId,
  loggedAt,
  onClose,
  onRestored,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  logId: number
  loggedAt: string
  onClose: () => void
  onRestored: () => void
}) {
  const [rows, setRows] = useState<PreviewRow[] | null>(null)
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void (async () => {
      try {
        const res = await callAdmin<PreviewRow[]>(target, 'userData.getRestorePreview', { accountId, logId })
        setRows(res)
        setPicked(new Set(res.map((r) => r.column)))
      } catch (e: unknown) {
        if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
        setError(e instanceof Error ? e.message : '되돌릴 내용을 불러오지 못했습니다.')
        setRows([])
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target, accountId, logId])

  const toggle = (c: string) => setPicked((s) => { const n = new Set(s); if (n.has(c)) n.delete(c); else n.add(c); return n })

  const submit = async () => {
    if (busy || picked.size === 0) return
    setBusy(true); setError('')
    try {
      await callAdmin(target, 'userData.restore', { accountId, logId, columns: [...picked] })
      onRestored()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '되돌리지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-start justify-center p-6 overflow-auto" onClick={() => { if (!busy) onClose() }}>
      <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">이 변경 전으로 되돌리기</h3>
          <button onClick={onClose} disabled={busy} className="text-neutral-400 hover:text-neutral-700 disabled:opacity-40" aria-label="닫기">
            <X className="w-4 h-4" />
          </button>
        </div>
        <div className="p-5 space-y-4">
          <TargetGameStrip />
          <p className="text-sm text-neutral-600">
            {formatDateTime(loggedAt)} 변경 직전 값으로 돌아갑니다. 그 뒤에 바뀐 칸도 함께 돌아가므로, 돌리지 않을 칸은 체크를 끄세요.
          </p>

          {rows === null ? (
            <div className="flex items-center gap-2 text-sm text-neutral-500"><Loader2 className="w-4 h-4 animate-spin" />불러오는 중…</div>
          ) : rows.length === 0 ? (
            !error && <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm text-neutral-600">지금 값이 그때와 같아 되돌릴 칸이 없습니다.</div>
          ) : (
            <div className="max-h-80 overflow-auto border border-neutral-200 rounded-md">
              <table className="w-full text-sm">
                <thead className="bg-neutral-50 text-neutral-500 text-xs sticky top-0">
                  <tr>
                    <th className="w-8" />
                    <th className="text-left px-3 py-2 font-medium">필드</th>
                    <th className="text-left px-3 py-2 font-medium">지금</th>
                    <th className="text-left px-3 py-2 font-medium">되돌린 뒤</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.column} className="border-t border-neutral-100 cursor-pointer" onClick={() => toggle(r.column)}>
                      <td className="pl-3"><input type="checkbox" checked={picked.has(r.column)} onChange={() => toggle(r.column)} onClick={(e) => e.stopPropagation()} /></td>
                      <td className="px-3 py-2 font-medium">{r.column}</td>
                      <td className="px-3 py-2 font-mono text-xs text-neutral-500 max-w-[220px] truncate" title={show(r.current)}>{show(r.current)}</td>
                      <td className="px-3 py-2 font-mono text-xs text-neutral-800 max-w-[220px] truncate" title={show(r.restored)}>{show(r.restored)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="rounded-md bg-amber-50 border border-amber-200 px-3 py-2 text-xs text-amber-800">
            플레이어가 게임을 하고 있으면 게임이 가진 값으로 다시 덮일 수 있습니다. 접속하지 않았을 때 하세요.
          </div>
          {error && <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <Button variant="outline" onClick={onClose} disabled={busy}>취소</Button>
          <Button variant="primary" onClick={() => { void submit() }} disabled={busy || picked.size === 0}>
            {busy && <Loader2 className="w-4 h-4 animate-spin" />}되돌리기{picked.size > 0 ? ` (${picked.size}칸)` : ''}
          </Button>
        </div>
      </div>
    </div>
  )
}
