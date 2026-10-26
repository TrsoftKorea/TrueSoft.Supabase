import { useCallback, useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, X, RefreshCw } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime, itemsText } from '../components/ui/format'
import { Button } from '../components/ui/Button'

type ItemRow = { key: string; count: number }
type GameItem = { key: string; display_name: string }
type Mode = 'competitive' | 'coop'

type RewardConfig = {
  mode: Mode
  win_items: ItemRow[]
  mail_title: string
  mail_content: string
  mail_expires_days: number
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

// SDK 가 우편 아이템 수량을 int 로 읽는다. 넘으면 그 플레이어의 우편함이 통째로 안 열린다.
const MAX_COUNT = 2147483647

const MODE_LABEL: Record<Mode, string> = { competitive: '경쟁', coop: '협동' }
const MODE_HINT: Record<Mode, string> = {
  competitive: '두 사람이 서로 반대 결과(한 명 승리, 한 명 패배)를 보내야 인정합니다.',
  coop: '두 사람이 같은 결과를 보내야 인정합니다.',
}

const inputCls = 'h-9 px-2.5 rounded-md border border-neutral-300 text-sm bg-white outline-none focus:border-[#1677ff] focus:ring-1 focus:ring-[#1677ff]'

function RewardModal({ target, onUnauthenticated, row, existingCodes, pendingByCode, catalog, catalogFailed, onClose, onSaved }: {
  target: ProjectTarget
  onUnauthenticated: () => void
  row: RewardRow | null
  existingCodes: Set<string>
  pendingByCode: Map<string, number>
  catalog: GameItem[]
  catalogFailed: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const cfg = row?.config ?? null
  const isNew = !row
  // 설정이 없는 코드에 다시 만드는 것도 "추가"다 — 그 사이 누가 만들었으면 덮어쓰지 않는다.
  const createOnly = !cfg
  const [gameCode, setGameCode] = useState(row?.game_code ?? '')
  const [mode, setMode] = useState<Mode>(cfg?.mode ?? 'competitive')
  const [items, setItems] = useState<ItemRow[]>(
    (cfg?.win_items ?? []).map((it) => ({ key: it.key, count: Number(it.count) })),
  )
  const [mailTitle, setMailTitle] = useState(cfg?.mail_title ?? '매치 보상')
  const [mailContent, setMailContent] = useState(cfg?.mail_content ?? '')
  const [mailExpiresDays, setMailExpiresDays] = useState(cfg?.mail_expires_days ?? 7)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [confirmMode, setConfirmMode] = useState(false)

  const addItem = () => setItems((x) => [...x, { key: catalog[0]?.key ?? '', count: 1 }])
  const setItem = (i: number, patch: Partial<ItemRow>) => setItems((x) => x.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  const delItem = (i: number) => setItems((x) => x.filter((_, j) => j !== i))

  const code = gameCode.trim()
  const codeValid = /^[a-z0-9_]{1,64}$/.test(code)
  const codeTaken = isNew && existingCodes.has(code)
  const daysValid = Number.isInteger(mailExpiresDays) && mailExpiresDays >= 1 && mailExpiresDays <= 365
  const inCatalog = (key: string) => catalog.some((c) => c.key === key)
  const countValid = (n: number) => Number.isInteger(n) && n >= 1 && n <= MAX_COUNT
  const itemsValid = items.every((it) => it.key !== '' && inCatalog(it.key) && countValid(it.count))
  const canSubmit = codeValid && !codeTaken && daysValid && itemsValid && !saving
  const modeChanged = !!cfg && cfg.mode !== mode
  // 설정을 지운 뒤 다시 만드는 코드는 비교할 이전 방식이 없다. 기다리는 판이 있으면 그것만으로 경고한다.
  // "게임 코드 추가"로 기록만 남은 코드를 적어도 같다.
  const pendingCount = pendingByCode.get(code) ?? 0
  const pendingAtRisk = pendingCount > 0 && (modeChanged || !cfg)

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      await callAdmin(target, 'matchRewards.upsert', {
        gameCode: code,
        mode,
        winItems: items.map((it) => ({ key: it.key, count: Number(it.count) })),
        mailTitle,
        mailContent,
        mailExpiresDays,
        createOnly,
      })
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
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4 overflow-auto">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-lg my-6">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">{isNew ? '게임 코드 추가' : `${row.game_code} 보상 설정`}</h3>
          <button onClick={onClose} disabled={saving} title="닫기" aria-label="닫기" className="text-neutral-400 hover:text-neutral-700 disabled:opacity-40"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
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
            <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
              {(['competitive', 'coop'] as const).map((m) => (
                <button key={m} type="button" onClick={() => setMode(m)}
                  className={['px-4 py-1.5 rounded-md text-sm transition-colors whitespace-nowrap', mode === m ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'].join(' ')}>
                  {MODE_LABEL[m]}
                </button>
              ))}
            </div>
            <p className="mt-1 text-xs text-neutral-400">{MODE_HINT[mode]} 게임이 보내는 결과와 맞지 않으면 모든 판이 무효가 됩니다.</p>
            {modeChanged && (
              <p className="mt-1 text-xs text-amber-600">방식을 바꾸면 게임이 그에 맞게 결과를 보내기 전까지 이 코드의 모든 판이 무효가 됩니다.</p>
            )}
            {pendingAtRisk && (
              <p className="mt-1 text-xs text-amber-600">상대 신고를 기다리는 판 {pendingCount.toLocaleString()}건도 이 방식으로 확인합니다.</p>
            )}
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs text-neutral-500">승리 보상 아이템</span>
              <button onClick={addItem} disabled={catalog.length === 0} className="inline-flex items-center gap-1 text-xs text-[#1677ff] disabled:opacity-40"><Plus className="w-3 h-3" />추가</button>
            </div>
            {catalogFailed && (
              <p className="text-xs text-red-500">아이템 카탈로그를 불러오지 못해 아이템을 고를 수 없습니다. 창을 닫고 새로고침하세요.</p>
            )}
            {items.map((it, i) => (
              <div key={i}>
                <div className="flex items-center gap-2">
                  <select value={it.key} onChange={(e) => setItem(i, { key: e.target.value })} className={inputCls + ' flex-1'}>
                    {!inCatalog(it.key) && (<option value={it.key}>(카탈로그에 없음) {it.key}</option>)}
                    {catalog.map((c) => (<option key={c.key} value={c.key}>{c.display_name} ({c.key})</option>))}
                  </select>
                  <input type="number" min={1} max={MAX_COUNT} value={it.count} onChange={(e) => setItem(i, { count: Number(e.target.value) })} className={inputCls + ' w-28'} />
                  <button onClick={() => delItem(i)} title="이 보상 삭제" aria-label="이 보상 삭제" className="text-neutral-400 hover:text-red-500 shrink-0"><X className="w-4 h-4" /></button>
                </div>
                {!inCatalog(it.key) && !catalogFailed && (
                  <p className="mt-1 text-xs text-red-500">카탈로그에서 삭제된 아이템입니다. 다른 아이템을 고르거나 이 줄을 삭제하세요.</p>
                )}
                {!countValid(it.count) && (
                  <p className="mt-1 text-xs text-red-500">수량은 1 이상 {MAX_COUNT.toLocaleString()} 이하의 정수여야 합니다.</p>
                )}
              </div>
            ))}
            {items.length === 0 && (
              <p className="text-xs text-neutral-400">비워 두면 기록만 남고 우편은 가지 않습니다.</p>
            )}
          </div>

          <div>
            <label className="block text-xs text-neutral-500 mb-1">우편 제목</label>
            <input value={mailTitle} onChange={(e) => setMailTitle(e.target.value)} className={inputCls + ' w-full'} />
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">우편 본문</label>
            <textarea value={mailContent} onChange={(e) => setMailContent(e.target.value)} rows={3} className={inputCls + ' w-full h-auto py-2'} />
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">우편 수령 기한(일)</label>
            <input type="number" min={1} max={365} value={mailExpiresDays} onChange={(e) => setMailExpiresDays(Number(e.target.value))} className={inputCls + ' w-40'} />
            {!daysValid && <p className="mt-1 text-xs text-red-500">1일에서 365일 사이로 정합니다.</p>}
          </div>

          {cfg?.updated_by && (
            <p className="text-xs text-neutral-400">마지막 변경: {cfg.updated_by} · {formatDateTime(cfg.updated_at)}</p>
          )}

          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <Button variant="outline" onClick={onClose} disabled={saving}>취소</Button>
          <Button variant="primary" onClick={submit} disabled={!canSubmit}>
            {saving ? '저장 중…' : '저장'}
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={confirmMode}
        title="결과 확인 방식 변경"
        confirmLabel="바꾸기"
        danger
        busy={saving}
        onConfirm={() => { void save() }}
        onCancel={() => setConfirmMode(false)}
        description="게임이 이 방식에 맞는 결과를 보내지 않으면, 지금부터 이 코드의 모든 판이 무효가 되고 보상이 나가지 않습니다."
      >
        {cfg && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="font-mono text-neutral-800">{code}</div>
            <div className="text-xs text-neutral-500 mt-1">
              {MODE_LABEL[cfg.mode]} → {MODE_LABEL[mode]} · 상대 신고를 기다리는 판 {pendingCount.toLocaleString()}건
            </div>
          </div>
        )}
      </ConfirmDialog>
    </div>
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
  const [catalog, setCatalog] = useState<GameItem[]>([])
  const [loading, setLoading] = useState(true)
  const [listFailed, setListFailed] = useState(false)
  const [catalogFailed, setCatalogFailed] = useState(false)
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

  // 카탈로그와 목록을 따로 받는다 — 카탈로그만 실패해도 목록이 비어 "코드가 없다"로 보이면 안 된다.
  const load = useCallback(async () => {
    setLoading(true)
    const [list, items] = await Promise.allSettled([
      callAdmin<RewardRow[]>(target, 'matchRewards.list'),
      callAdmin<GameItem[]>(target, 'gameItems.list'),
    ])
    if (list.status === 'fulfilled') {
      setRows(list.value)
      setListFailed(false)
    } else {
      // 이전 목록을 남기면 프로젝트를 바꾼 직후 실패했을 때 다른 프로젝트의 행을 이 프로젝트에 저장하게 된다.
      setRows([])
      setListFailed(true)
      report(list.reason, '매치 보상 설정을 불러오지 못했습니다.')
    }
    if (items.status === 'fulfilled') {
      setCatalog(items.value)
      setCatalogFailed(false)
    } else {
      setCatalog([])
      setCatalogFailed(true)
      report(items.reason, '아이템 카탈로그를 불러오지 못했습니다.')
    }
    setLoading(false)
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
      <PageHeader title="매치 보상" description="게임 코드마다 대전 결과를 어떻게 인정할지와 이긴 쪽에 보낼 보상을 정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="flex items-center gap-2">
        <Button variant="primary" size="compact"
          onClick={() => setCreating(true)}
          disabled={loading || listFailed}
        >
          <Plus className="w-3.5 h-3.5" />
          게임 코드 추가
        </Button>
        <Button variant="outline" size="compact"
          onClick={() => void load()}
          disabled={loading}
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          새로고침
        </Button>
      </div>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-44">게임 코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-24 whitespace-nowrap">확인 방식</th>
                <th className="text-left px-4 py-2.5 font-medium">승리 보상</th>
                <th className="text-right px-4 py-2.5 font-medium w-24">기록</th>
                <th className="text-right px-4 py-2.5 font-medium w-24">플레이어</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">최근 신고</th>
                <th className="px-4 py-2.5 w-20" />
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow
                  loading={loading}
                  empty={rows.length === 0}
                  failed={listFailed}
                  colSpan={7}
                  emptyText="등록된 게임 코드가 없습니다."
                />
              ) : (
                rows.map((r) => (
                  <tr key={r.game_code} className="border-t border-neutral-100">
                    <td className="px-4 py-3 font-mono text-neutral-800 whitespace-nowrap">{r.game_code}</td>
                    {r.config ? (
                      <>
                        <td className="px-4 py-3 text-neutral-700 whitespace-nowrap">{MODE_LABEL[r.config.mode] ?? r.config.mode}</td>
                        <td className="px-4 py-3 text-neutral-700">
                          {(r.config.win_items ?? []).length > 0
                            ? itemsText(r.config.win_items, catalog)
                            : <span className="text-neutral-400">우편 없음</span>}
                        </td>
                      </>
                    ) : (
                      <td colSpan={2} className="px-4 py-3 text-red-600">
                        설정이 없어 이 코드로 오는 신고가 모두 실패합니다.
                      </td>
                    )}
                    <td className="px-4 py-3 text-right text-neutral-600 whitespace-nowrap">{r.record_count.toLocaleString()}</td>
                    <td className="px-4 py-3 text-right text-neutral-600 whitespace-nowrap">{r.player_count.toLocaleString()}</td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.last_reported_at)}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-1">
                        <button onClick={() => setEditRow(r)} title={r.config ? '수정' : '설정 만들기'} aria-label={r.config ? '수정' : '설정 만들기'} className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                          {r.config ? <Pencil className="w-3.5 h-3.5" /> : <Plus className="w-3.5 h-3.5" />}
                        </button>
                        {r.config && (
                          <button onClick={() => { setDelError(''); setDelRow(r) }} title="삭제" aria-label="삭제" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500">
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      {(creating || editRow) && (
        <RewardModal
          key={modalRow?.game_code ?? 'new'}
          target={target}
          onUnauthenticated={onUnauthenticated}
          row={modalRow}
          existingCodes={existingCodes}
          pendingByCode={pendingByCode}
          catalog={catalog}
          catalogFailed={catalogFailed}
          onClose={() => { setCreating(false); setEditRow(null) }}
          onSaved={() => { setCreating(false); setEditRow(null); void load() }}
        />
      )}

      <ConfirmDialog
        open={!!delRow}
        title="보상 설정 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        error={delError || undefined}
        onConfirm={() => { void confirmDelete() }}
        onCancel={() => setDelRow(null)}
        description="이 게임 코드로 오는 대전 신고가 지금부터 모두 실패합니다. 이미 쌓인 기록은 남습니다."
      >
        {delRow && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="font-mono text-neutral-800">{delRow.game_code}</div>
            <div className="text-xs text-neutral-500 mt-1">기록 {delRow.record_count.toLocaleString()}건 · 플레이어 {delRow.player_count.toLocaleString()}명</div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
