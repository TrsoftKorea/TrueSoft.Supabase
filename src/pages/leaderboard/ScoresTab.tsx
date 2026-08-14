import { useState, useEffect, useCallback, useRef } from 'react'
import { Search, ChevronLeft, ChevronRight, Pencil, Trash2, Check, X, Loader2, SlidersHorizontal } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { formatDateTime } from '../../components/ui/format'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { ErrorBanner } from '../../components/ui/ErrorBanner'

type LbRow = { code: string; display_name: string; rotation: string; scope: string }
type ServerRow = { id: string; server_code: string; display_name: string }
type AttachedCol = { name: string; dataType: string }
type ScoreRow = {
  rank: number
  account_id: string
  user_id: string | null
  display_name: string | null
  score: number
  score_achieved_at: string | null
  score_count: number
  server_code: string | null
  [key: string]: unknown
}
type ScoresData = {
  rows: ScoreRow[]
  total: number
  pageSize: number
  rotationCount: number | null
  maxRotation: number | null
  scope: string | null
  dataColumns: string[]
}
type ColsData = { all?: { column_name: string; data_type: string }[]; attached?: { column_name: string }[] }

// 첨부 컬럼 값을 DB 타입에 맞는 JSON 타입으로 변환한다(jsonb_populate_record 가 타입 일치를 요구).
function coerce(dataType: string, raw: string): unknown {
  const t = (dataType ?? '').toLowerCase()
  if (raw === '') return null
  if (t.includes('int') || t === 'numeric' || t.includes('double') || t === 'real') {
    const n = Number(raw)
    return Number.isFinite(n) ? n : raw
  }
  if (t === 'boolean') return raw === 'true' || raw === '1'
  if (t.includes('json')) {
    try { return JSON.parse(raw) } catch { return raw }
  }
  return raw
}

function cell(v: unknown): string {
  if (v === null || v === undefined || v === '') return '-'
  if (typeof v === 'object') return JSON.stringify(v)
  return String(v)
}

export default function ScoresTab({
  target,
  onUnauthenticated,
  selectedCode,
  onSelect,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  selectedCode: string
  onSelect: (code: string) => void
}) {
  // null = use latest rotation
  const [selectedRotation, setSelectedRotation] = useState<number | null>(null)
  const [selectedServerId, setSelectedServerId] = useState<string>('')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editScore, setEditScore] = useState('')
  const [dataEditRow, setDataEditRow] = useState<ScoreRow | null>(null)
  const [dataForm, setDataForm] = useState<Record<string, string>>({})
  const [delTarget, setDelTarget] = useState<ScoreRow | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const [leaderboards, setLeaderboards] = useState<LbRow[]>([])
  const [listLoading, setListLoading] = useState(true)
  const [servers, setServers] = useState<ServerRow[]>([])
  const [colsData, setColsData] = useState<ColsData | undefined>(undefined)
  const [data, setData] = useState<ScoresData | undefined>(undefined)
  const [scoresLoading, setScoresLoading] = useState(false)
  const scoresReqRef = useRef(0)

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

  useEffect(() => {
    void (async () => {
      setListLoading(true)
      try {
        const [lb, srv] = await Promise.all([
          callAdmin<{ rows: LbRow[] }>(target, 'leaderboard.list'),
          callAdmin<{ rows: ServerRow[] }>(target, 'mails.getServers'),
        ])
        setLeaderboards(lb.rows)
        setServers(srv.rows)
      } catch (e: unknown) {
        report(e, '불러오지 못했습니다.')
      } finally {
        setListLoading(false)
      }
    })()
  }, [target, report])

  const selectedBoard = leaderboards.find((lb) => lb.code === selectedCode)
  const isServerScoped = selectedBoard?.scope === 'server'
  // 초기화 주기가 없는 리더보드는 회차가 1개뿐이라 선택할 것이 없습니다.
  const hasRotation = !!selectedBoard && selectedBoard.rotation !== 'none'

  const typeByName = new Map<string, string>(
    (colsData?.all ?? []).map((c) => [c.column_name, c.data_type] as [string, string]),
  )
  const attachedCols: AttachedCol[] = (colsData?.attached ?? []).map((c) => ({
    name: c.column_name,
    dataType: typeByName.get(c.column_name) ?? 'text',
  }))

  // 선택한 리더보드가 서버 스코프면 서버를 골라야 게임과 같은 순위가 나옵니다. 기본값은 첫 서버.
  useEffect(() => {
    const first = servers[0]
    if (isServerScoped && !selectedServerId && first) {
      setSelectedServerId(first.id)
    }
  }, [isServerScoped, selectedServerId, servers])

  useEffect(() => {
    if (!selectedCode) { setColsData(undefined); return }
    void (async () => {
      try {
        setColsData(await callAdmin<ColsData>(target, 'leaderboard.columns', { code: selectedCode }))
      } catch (e: unknown) {
        report(e, '필드를 불러오지 못했습니다.')
      }
    })()
  }, [target, selectedCode, report])

  const loadScores = useCallback(async () => {
    if (!selectedCode) { setData(undefined); return }
    const reqId = ++scoresReqRef.current
    setScoresLoading(true)
    try {
      const result = await callAdmin<ScoresData>(target, 'leaderboard.scores', {
        code: selectedCode,
        rotationCount: selectedRotation,
        search,
        page,
        serverId: isServerScoped ? (selectedServerId || null) : null,
      })
      if (reqId !== scoresReqRef.current) return
      setData(result)
    } catch (e: unknown) {
      if (reqId !== scoresReqRef.current) return
      report(e, '기록을 불러오지 못했습니다.')
      setData(undefined)
    } finally {
      if (reqId === scoresReqRef.current) setScoresLoading(false)
    }
  }, [target, selectedCode, selectedRotation, search, page, selectedServerId, isServerScoped, report])

  useEffect(() => { void loadScores() }, [loadScores])

  // Reset when code changes
  useEffect(() => {
    setSelectedRotation(null)
    setSelectedServerId('')
    setPage(1)
    setSearch('')
    setSearchInput('')
  }, [selectedCode])

  const rows: ScoreRow[] = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const maxRotation = data?.maxRotation ?? null
  const displayedRotation = data?.rotationCount ?? null

  const submitSearch = () => { setSearch(searchInput); setPage(1) }

  const startEdit = (row: ScoreRow) => {
    setEditingId(row.account_id)
    setEditScore(String(row.score))
  }
  const cancelEdit = () => setEditingId(null)

  const confirmEdit = async (accountId: string) => {
    if (!selectedCode || displayedRotation === null || saving) return
    setSaving(true)
    try {
      await callAdmin(target, 'leaderboard.setScore', {
        code: selectedCode,
        accountId,
        rotationCount: displayedRotation,
        score: Number(editScore),
      })
      setEditingId(null)
      await loadScores()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const confirmDelete = async () => {
    if (!delTarget || deleting) return
    if (!selectedCode || displayedRotation === null) return
    setDeleting(true)
    try {
      await callAdmin(target, 'leaderboard.deleteScore', {
        code: selectedCode,
        accountId: delTarget.account_id,
        rotationCount: displayedRotation,
      })
      setDelTarget(null)
      await loadScores()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally { setDeleting(false) }
  }

  const openDataEdit = (row: ScoreRow) => {
    const form: Record<string, string> = {}
    for (const c of attachedCols) {
      const v = row[c.name]
      form[c.name] = v === null || v === undefined ? '' : typeof v === 'object' ? JSON.stringify(v) : String(v)
    }
    setDataForm(form)
    setDataEditRow(row)
  }

  const saveData = async () => {
    if (!dataEditRow || !selectedCode || displayedRotation === null || saving) return
    const payload: Record<string, unknown> = {}
    for (const c of attachedCols) payload[c.name] = coerce(c.dataType, dataForm[c.name] ?? '')
    setSaving(true)
    try {
      await callAdmin(target, 'leaderboard.setPlayerData', {
        code: selectedCode,
        accountId: dataEditRow.account_id,
        rotationCount: displayedRotation,
        data: payload,
      })
      setDataEditRow(null)
      await loadScores()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const inp = 'h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white'

  // Dropdown value: use selectedRotation if set, otherwise fall back to maxRotation
  const rotVal = selectedRotation ?? maxRotation

  const rotationOptions: number[] =
    maxRotation !== null ? Array.from({ length: maxRotation }, (_, i) => maxRotation - i) : []

  const colSpan = 7 + attachedCols.length

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {/* 필터 영역 */}
      <WhiteCard className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <select
            value={selectedCode}
            onChange={(e) => onSelect(e.target.value)}
            disabled={listLoading}
            className={inp + ' min-w-[160px] disabled:opacity-60 disabled:cursor-not-allowed'}
          >
            {listLoading ? (
              <option value={selectedCode}>불러오는 중…</option>
            ) : (
              <>
                <option value="">리더보드 선택</option>
                {leaderboards.map((lb) => (
                  <option key={lb.code} value={lb.code}>{lb.display_name}</option>
                ))}
              </>
            )}
          </select>
          {isServerScoped && (
            <select
              value={selectedServerId}
              onChange={(e) => { setSelectedServerId(e.target.value); setPage(1) }}
              className={inp + ' w-44'}
            >
              {servers.length === 0 && <option value="">서버 없음</option>}
              {servers.map((sv) => (
                <option key={sv.id} value={sv.id}>{sv.display_name || sv.server_code}</option>
              ))}
            </select>
          )}
          {hasRotation && (
            <select
              value={rotVal ?? ''}
              onChange={(e) => { setSelectedRotation(Number(e.target.value)); setPage(1) }}
              disabled={maxRotation === null}
              className={inp + ' w-36'}
            >
              {maxRotation === null && <option value="">-</option>}
              {rotationOptions.map((n) => (
                <option key={n} value={n}>
                  {n}회차{n === maxRotation ? ' (현재)' : ''}
                </option>
              ))}
            </select>
          )}
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
            placeholder="닉네임 / 계정 ID"
            className={inp + ' flex-1 min-w-[180px]'}
          />
          <button
            onClick={submitSearch}
            disabled={!selectedCode || scoresLoading}
            className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-60"
          >
            {scoresLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}
            검색
          </button>
        </div>
      </WhiteCard>

      {/* 기록 테이블 */}
      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-right px-4 py-2.5 font-medium">순위</th>
                <th className="text-left px-4 py-2.5 font-medium">플레이어</th>
                <th className="text-left px-4 py-2.5 font-medium whitespace-nowrap">서버</th>
                <th className="text-right px-4 py-2.5 font-medium">점수</th>
                <th className="text-right px-4 py-2.5 font-medium">기록 수</th>
                <th className="text-left px-4 py-2.5 font-medium">점수 달성</th>
                {attachedCols.map((c) => (
                  <th key={c.name} className="text-left px-4 py-2.5 font-medium whitespace-nowrap">{c.name}</th>
                ))}
                <th className="text-left px-4 py-2.5 font-medium">작업</th>
              </tr>
            </thead>
            <tbody>
              {scoresLoading || rows.length === 0 ? (
                <TableStatusRow
                  loading={scoresLoading}
                  empty={rows.length === 0}
                  colSpan={colSpan}
                  emptyText={selectedCode ? '기록이 없습니다.' : '리더보드를 선택하세요.'}
                />
              ) : (
                rows.map((r) => (
                  <tr key={r.account_id} className="border-t border-neutral-100 hover:bg-neutral-50">
                    <td className="px-4 py-3 text-right">{r.rank}</td>
                    <td className="px-4 py-3">
                      {r.display_name ?? (
                        <span className="font-mono text-xs text-neutral-500">{r.account_id}</span>
                      )}
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap">{r.server_code ?? '-'}</td>
                    <td className="px-4 py-3 text-right">
                      {editingId === r.account_id ? (
                        <div className="flex items-center justify-end gap-1">
                          <input
                            type="number"
                            value={editScore}
                            onChange={(e) => setEditScore(e.target.value)}
                            onKeyDown={(e) => {
                              if (e.key === 'Enter') void confirmEdit(r.account_id)
                              if (e.key === 'Escape') cancelEdit()
                            }}
                            className="w-28 h-7 px-2 rounded border border-neutral-300 text-sm text-right"
                            autoFocus
                          />
                          <button
                            onClick={() => void confirmEdit(r.account_id)}
                            title="확인"
                            className="p-1 rounded hover:bg-emerald-50 text-emerald-600"
                          >
                            <Check className="w-3.5 h-3.5" />
                          </button>
                          <button
                            onClick={cancelEdit}
                            title="취소"
                            className="p-1 rounded hover:bg-neutral-100 text-neutral-500"
                          >
                            <X className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      ) : (
                        r.score
                      )}
                    </td>
                    <td className="px-4 py-3 text-right">{r.score_count}</td>
                    <td className="px-4 py-3">{formatDateTime(r.score_achieved_at)}</td>
                    {attachedCols.map((c) => (
                      <td key={c.name} className="px-4 py-3 whitespace-nowrap">{cell(r[c.name])}</td>
                    ))}
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        {editingId !== r.account_id && (
                          <button
                            onClick={() => startEdit(r)}
                            title="점수 수정"
                            className="p-1.5 rounded hover:bg-neutral-100 text-neutral-500 hover:text-neutral-800"
                          >
                            <Pencil className="w-3.5 h-3.5" />
                          </button>
                        )}
                        {attachedCols.length > 0 && (
                          <button
                            onClick={() => openDataEdit(r)}
                            title="플레이어 데이터 수정"
                            className="p-1.5 rounded hover:bg-neutral-100 text-neutral-500 hover:text-neutral-800"
                          >
                            <SlidersHorizontal className="w-3.5 h-3.5" />
                          </button>
                        )}
                        <button
                          onClick={() => setDelTarget(r)}
                          title="삭제"
                          className="p-1.5 rounded hover:bg-red-50 text-neutral-500 hover:text-red-600"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* 페이지네이션 */}
        <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          <span>{total} result{total === 1 ? '' : 's'}</span>
          <div className="flex items-center gap-1">
            <button
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"
            >
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
            <span className="px-2">{page} / {totalPages}</span>
            <button
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"
            >
              <ChevronRight className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </WhiteCard>

      {/* 플레이어 데이터 수정 모달 */}
      {dataEditRow && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/30"
          onClick={() => setDataEditRow(null)}
        >
          <div className="w-[420px] max-w-[92vw] rounded-lg bg-white p-5 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <div className="mb-3 flex items-center justify-between">
              <h3 className="text-sm font-semibold">플레이어 데이터 수정</h3>
              <button onClick={() => setDataEditRow(null)} className="p-1 rounded hover:bg-neutral-100 text-neutral-500">
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="mb-3 text-xs text-neutral-500">
              {dataEditRow.display_name ?? dataEditRow.account_id} · {displayedRotation}회차
            </p>
            <div className="space-y-2.5">
              {attachedCols.map((c) => (
                <div key={c.name} className="flex items-center gap-3">
                  <label className="w-32 shrink-0 text-xs text-neutral-600">
                    {c.name}
                    <span className="ml-1 text-neutral-400">{c.dataType}</span>
                  </label>
                  {c.dataType.toLowerCase() === 'boolean' ? (
                    <select
                      value={dataForm[c.name] ?? ''}
                      onChange={(e) => setDataForm((f) => ({ ...f, [c.name]: e.target.value }))}
                      className={inp + ' flex-1'}
                    >
                      <option value="">-</option>
                      <option value="true">true</option>
                      <option value="false">false</option>
                    </select>
                  ) : (
                    <input
                      value={dataForm[c.name] ?? ''}
                      onChange={(e) => setDataForm((f) => ({ ...f, [c.name]: e.target.value }))}
                      className={inp + ' flex-1'}
                    />
                  )}
                </div>
              ))}
              {attachedCols.length === 0 && (
                <p className="text-xs text-neutral-500">등록된 플레이어 데이터 컬럼이 없습니다.</p>
              )}
            </div>
            <div className="mt-5 flex justify-end gap-2">
              <button
                onClick={() => setDataEditRow(null)}
                className="h-9 px-3 rounded-md border border-neutral-300 text-sm hover:bg-neutral-50"
              >
                취소
              </button>
              <button
                onClick={() => void saveData()}
                disabled={saving}
                className="h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-60"
              >
                저장
              </button>
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={!!delTarget}
        title="기록 지우기"
        confirmLabel="지우기"
        danger
        busy={deleting}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDelTarget(null)}
        description={`${displayedRotation}회차에서만 지웁니다. 다른 회차 기록은 그대로 남습니다.`}
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.display_name ?? delTarget.account_id}</div>
            <div className="text-xs text-neutral-500 mt-1">{delTarget.rank}위 · {delTarget.score}점</div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
