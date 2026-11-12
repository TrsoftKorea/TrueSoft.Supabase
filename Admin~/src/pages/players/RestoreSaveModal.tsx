import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { Button } from '../../components/ui/Button'
import { EditDialog } from '../../components/ui/EditDialog'
import { formatDateTime } from '../../components/ui/format'
import { DataTable, type Column } from '../../components/ui/DataTable'

type PreviewRow = { column: string; data_type: string; current: unknown; restored: unknown }

function show(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (typeof v === 'object') return JSON.stringify(v)
  if (typeof v === 'boolean') return v ? '참' : '거짓'
  return String(v)
}

// 값 없음(NULL)은 표가 "-" 로 채우고, 빈 글자는 따옴표 둘로 보인다 — 둘 사이만 바뀌는 칸도 무엇이 바뀌는지 갈린다.
function valueCell(v: unknown, tone: string) {
  if (v === null || v === undefined) return null
  const s = show(v)
  return <span className={`font-mono text-xs ${tone}`}>{s === '' ? '""' : s}</span>
}

/** 세이브를 어떤 변경 직전 상태로 되돌린다. 바뀌는 칸을 먼저 보여 주고, 고른 칸만 쓴다. */
export function RestoreSaveModal({
  target,
  onUnauthenticated,
  accountId,
  logId,
  loggedAt,
  playerName,
  onClose,
  onRestored,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  logId: number
  loggedAt: string
  // 누구의 세이브인지 창에서 확인하게 — 여러 계정이 섞인 데이터 로그에서도 열린다.
  playerName: string
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
    <EditDialog
      title="데이터 되돌리기"
      description={playerName}
      size="xl"
      busy={busy}
      onClose={onClose}
      error={error || undefined}
      footer={<>
        <Button variant="outline" onClick={onClose} disabled={busy}>취소</Button>
        <Button variant="primary" onClick={() => { void submit() }} disabled={busy || picked.size === 0}>
          {busy && <Loader2 className="w-4 h-4 animate-spin" />}{picked.size > 0 ? `${picked.size}칸 되돌리기` : '되돌리기'}
        </Button>
      </>}
    >
          <p className="text-sm text-neutral-600">
            {formatDateTime(loggedAt)} 변경 직전 값으로 돌아갑니다. 그 뒤에 바뀐 칸도 함께 돌아가므로, 돌리지 않을 칸은 체크를 끄세요.
          </p>

          {rows === null ? (
            <div className="flex items-center gap-2 text-sm text-neutral-500"><Loader2 className="w-4 h-4 animate-spin" />불러오는 중…</div>
          ) : rows.length === 0 ? (
            !error && <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm text-neutral-600">지금 값이 그때와 같아 되돌릴 칸이 없습니다.</div>
          ) : (
            <div className="border border-neutral-200 rounded-md overflow-hidden">
              <DataTable
                maxHeight="max-h-80"
                columns={[
                  {
                    key: 'pick', header: '', kind: 'status', cell: (r) => (
                      <input type="checkbox" checked={picked.has(r.column)} onChange={() => toggle(r.column)} onClick={(e) => e.stopPropagation()} />
                    ),
                  },
                  { key: 'column', header: '필드', kind: 'mono', cell: (r) => r.column },
                  { key: 'current', header: '지금', kind: 'text', cell: (r) => valueCell(r.current, 'text-neutral-500'), full: (r) => show(r.current) },
                  { key: 'restored', header: '되돌린 뒤', kind: 'text', cell: (r) => valueCell(r.restored, 'text-neutral-800'), full: (r) => show(r.restored) },
                ] satisfies Column<PreviewRow>[]}
                rows={rows}
                rowKey={(r) => r.column}
                onRowClick={(r) => toggle(r.column)}
              />
            </div>
          )}

          <div className="rounded-md bg-amber-50 border border-amber-200 px-3 py-2 text-xs text-amber-800">
            플레이어가 게임을 하고 있으면 게임이 가진 값으로 다시 덮일 수 있습니다.
          </div>
    </EditDialog>
  )
}
