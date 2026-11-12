import { useCallback, useEffect, useState } from 'react'
import { Plus, Pencil, Trash2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { InfoList } from '../components/ui/InfoList'
import { inputCls } from '../components/ui/Input'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableCard } from '../components/ui/DataTable'
import { StatusText } from '../components/ui/StatusText'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { EditDialog } from '../components/ui/EditDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime } from '../components/ui/format'
import { Button } from '../components/ui/Button'
import { Segmented } from '../components/ui/Segmented'

type Mode = 'competitive' | 'coop'

// 매치는 기록 전용이다 — 서버가 보상을 보내지 않는다. 게임 코드 등록과 결과 확인 방식만 정한다.
type RewardConfig = {
  mode: Mode
  updated_at: string
  updated_by: string | null
}

type RewardRow = {
  game_code: string
  config: RewardConfig | null
  record_count: number
  player_count: number
  pending_count: number
  last_reported_at: string | null
}

const MODE_LABEL: Record<Mode, string> = { competitive: '경쟁', coop: '협동' }
const MODE_HINT: Record<Mode, string> = {
  competitive: '두 사람이 서로 반대 결과(한 명 승리, 한 명 패배)를 보내야 인정합니다.',
  coop: '두 사람이 같은 결과를 보내야 인정합니다.',
}

function RewardModal({ target, onUnauthenticated, row, existingCodes, pendingByCode, onClose, onSaved }: {
  target: ProjectTarget
  onUnauthenticated: () => void
  row: RewardRow | null
  existingCodes: Set<string>
  pendingByCode: Map<string, number>
  onClose: () => void
  onSaved: () => void
}) {
  const cfg = row?.config ?? null
  const isNew = !row
  // 설정이 없는 코드에 다시 만드는 것도 "추가"다 — 그 사이 누가 만들었으면 덮어쓰지 않는다.
  const createOnly = !cfg
  const [gameCode, setGameCode] = useState(row?.game_code ?? '')
  const [mode, setMode] = useState<Mode>(cfg?.mode ?? 'competitive')
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [confirmMode, setConfirmMode] = useState(false)

  const code = gameCode.trim()
  const codeValid = /^[a-z0-9_]{1,64}$/.test(code)
  const codeTaken = isNew && existingCodes.has(code)
  const canSubmit = codeValid && !codeTaken && !saving
  const modeChanged = !!cfg && cfg.mode !== mode
  // 설정을 지운 뒤 다시 만드는 코드는 비교할 이전 방식이 없다. 기다리는 판이 있으면 그것만으로 경고한다.
  // "게임 코드 추가"로 기록만 남은 코드를 적어도 같다.
  const pendingCount = pendingByCode.get(code) ?? 0
  const pendingAtRisk = pendingCount > 0 && (modeChanged || !cfg)

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      await callAdmin(target, 'matchRewards.upsert', { gameCode: code, mode, createOnly })
      onSaved()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setSaving(false)
      setConfirmMode(false)
    }
  }

  const submit = () => {
    if (!canSubmit) return
    if (modeChanged) { setConfirmMode(true); return }
    void save()
  }

  return (
    <>
    <EditDialog
      error={error || undefined}
      title={isNew ? '게임 코드 추가' : '확인 방식'}
      description={isNew ? undefined : row.game_code}
      size="lg"
      busy={saving}
      onClose={onClose}
      footer={<>
        <Button variant="outline" onClick={onClose} disabled={saving}>취소</Button>
        <Button variant="primary" onClick={submit} disabled={!canSubmit}>
          {saving ? '저장 중…' : '저장'}
        </Button>
      </>}
    >
          <div>
            <label className="block text-xs text-neutral-500 mb-1">게임 코드</label>
            <input
              value={gameCode}
              onChange={(e) => setGameCode(e.target.value)}
              disabled={!isNew}
              className={inputCls + ' w-full font-mono disabled:bg-neutral-50 disabled:text-neutral-500'}
            />
            {isNew && !codeTaken && (
              <p className={`mt-1 text-xs ${gameCode === '' || codeValid ? 'text-neutral-400' : 'text-red-500'}`}>
                게임이 결과를 보낼 때 쓰는 코드와 똑같이 적습니다. 영문 소문자·숫자·밑줄만 쓸 수 있습니다.
              </p>
            )}
            {codeTaken && (
              <p className="mt-1 text-xs text-red-500">이미 등록된 코드입니다. 목록에서 그 코드를 수정하세요.</p>
            )}
            {!isNew && !codeValid && (
              <p className="mt-1 text-xs text-red-500">
                예전에 만든 코드라 지금 규칙(영문 소문자·숫자·밑줄)에 맞지 않아 저장할 수 없습니다. 같은 값으로 새 코드를 만들고 게임도 그 코드로 바꿔야 합니다.
              </p>
            )}
          </div>

          <div>
            <label className="block text-xs text-neutral-500 mb-1">결과 확인 방식</label>
            <Segmented
              value={mode}
              onChange={setMode}
              options={(['competitive', 'coop'] as const).map((m) => ({ v: m, label: MODE_LABEL[m] }))}
            />
            <p className="mt-1 text-xs text-neutral-400">{MODE_HINT[mode]} 게임이 보내는 결과와 맞지 않으면 모든 판이 무효가 됩니다.</p>
            {modeChanged && (
              <p className="mt-1 text-xs text-amber-600">방식을 바꾸면 게임이 그에 맞게 결과를 보내기 전까지 이 코드의 모든 판이 무효가 됩니다.</p>
            )}
            {pendingAtRisk && (
              <p className="mt-1 text-xs text-amber-600">상대 신고를 기다리는 판 {pendingCount.toLocaleString()}건도 이 방식으로 확인합니다.</p>
            )}
          </div>

          {cfg?.updated_by && (
            <p className="text-xs text-neutral-400">마지막 변경: {cfg.updated_by} · {formatDateTime(cfg.updated_at)}</p>
          )}

    </EditDialog>

      <ConfirmDialog
        open={confirmMode}
        title="확인 방식 변경"
        confirmLabel="바꾸기"
        danger
        busy={saving}
        onConfirm={() => { void save() }}
        onCancel={() => setConfirmMode(false)}
        description="게임이 이 방식에 맞는 결과를 보내지 않으면, 지금부터 이 코드의 모든 판이 무효로 기록됩니다."
      >
        {cfg && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-2">
            <div className="font-mono text-neutral-800">{code}</div>
            <InfoList items={[
              { label: '확인 방식', value: `${MODE_LABEL[cfg.mode]} → ${MODE_LABEL[mode]}` },
              { label: '상대 신고를 기다리는 판', value: `${pendingCount.toLocaleString()}건` },
            ]} />
          </div>
        )}
      </ConfirmDialog>
    </>
  )
}

export default function MatchRewards({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<RewardRow[]>([])
  const [loading, setLoading] = useState(true)
  const [listFailed, setListFailed] = useState(false)
  const [error, setError] = useState('')
  const [editRow, setEditRow] = useState<RewardRow | null>(null)
  const [creating, setCreating] = useState(false)
  const [delRow, setDelRow] = useState<RewardRow | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [delError, setDelError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await callAdmin<RewardRow[]>(target, 'matchRewards.list'))
      setListFailed(false)
    } catch (e: unknown) {
      // 이전 목록을 남기면 프로젝트를 바꾼 직후 실패했을 때 다른 프로젝트의 행을 이 프로젝트에 저장하게 된다.
      setRows([])
      setListFailed(true)
      report(e, '매치 게임 코드를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const confirmDelete = async () => {
    if (!delRow || deleting) return
    setDeleting(true)
    setDelError('')
    try {
      await callAdmin(target, 'matchRewards.delete', { gameCode: delRow.game_code })
      setDelRow(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDelError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setDeleting(false)
    }
  }

  const modalRow = creating ? null : editRow
  const existingCodes = new Set(rows.filter((r) => r.config).map((r) => r.game_code))
  const pendingByCode = new Map(rows.map((r) => [r.game_code, r.pending_count]))

  return (
    <div className="space-y-4">
      <PageHeader
        title="매치 기록"
        description="게임 코드마다 대전 결과를 어떻게 확인할지 정하고 쌓인 기록을 봅니다."
        actions={
          <>
            <RefreshButton onClick={() => void load()} loading={loading} />
            <Button variant="primary" size="compact" onClick={() => setCreating(true)} disabled={loading || listFailed}>
              <Plus className="w-3.5 h-3.5" />게임 코드 추가
            </Button>
          </>
        }
      />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <TableCard
        columns={[
          { key: 'code', header: '게임 코드', kind: 'mono', cell: (r) => r.game_code },
          {
            key: 'mode', header: '확인 방식', kind: 'status', cell: (r) => r.config
              ? MODE_LABEL[r.config.mode] ?? r.config.mode
              : <StatusText tone="bad">등록 안 됨</StatusText>,
          },
          { key: 'records', header: '기록', kind: 'number', cell: (r) => r.record_count.toLocaleString() },
          { key: 'players', header: '플레이어', kind: 'number', cell: (r) => r.player_count.toLocaleString() },
          { key: 'last', header: '최근 신고', kind: 'datetime', cell: (r) => r.last_reported_at ? formatDateTime(r.last_reported_at) : null },
          {
            key: 'actions', header: '', kind: 'actions', cell: (r) => (
              <span className="inline-flex gap-1.5">
                <button onClick={() => setEditRow(r)} title={r.config ? '수정' : '설정 만들기'} aria-label={r.config ? '수정' : '설정 만들기'} className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                  {r.config ? <Pencil className="w-3.5 h-3.5" /> : <Plus className="w-3.5 h-3.5" />}
                </button>
                {r.config ? (
                  <button onClick={() => { setDelError(''); setDelRow(r) }} title="삭제" aria-label="삭제" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500">
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                ) : <span className="w-7 h-7" />}
              </span>
            ),
          },
        ]}
        rows={rows}
        rowKey={(r) => r.game_code}
        loading={loading}
        failed={listFailed}
        emptyText="등록된 게임 코드가 없습니다."
      />

      {(creating || editRow) && (
        <RewardModal
          key={modalRow?.game_code ?? 'new'}
          target={target}
          onUnauthenticated={onUnauthenticated}
          row={modalRow}
          existingCodes={existingCodes}
          pendingByCode={pendingByCode}
          onClose={() => { setCreating(false); setEditRow(null) }}
          onSaved={() => { setCreating(false); setEditRow(null); void load() }}
        />
      )}

      <ConfirmDialog
        open={!!delRow}
        title="게임 코드 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        error={delError || undefined}
        onConfirm={() => { void confirmDelete() }}
        onCancel={() => setDelRow(null)}
        description="이 게임 코드로 오는 대전 신고가 지금부터 모두 실패합니다. 이미 쌓인 기록은 남습니다."
      >
        {delRow && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-2">
            <div className="font-mono text-neutral-800">{delRow.game_code}</div>
            <InfoList items={[
              { label: '기록', value: `${delRow.record_count.toLocaleString()}건` },
              { label: '플레이어', value: `${delRow.player_count.toLocaleString()}명` },
            ]} />
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
