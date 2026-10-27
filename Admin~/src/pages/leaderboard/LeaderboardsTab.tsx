import { useState, useEffect, useCallback, type ChangeEvent } from 'react'
import { Plus, Trash2, RotateCw, Pencil, Loader2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { usePendingChanges } from '../../lib/pendingChanges'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { formatCount, formatDateTime } from '../../components/ui/format'
import { PendingChanges, type PendingItem } from '../../components/ui/PendingChanges'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { opLabel } from '../../lib/schemaLabels'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { Button } from '../../components/ui/Button'
import { DEFAULT_TIME_ZONE, isoToLocalInput, localInputToIso, timeZoneName, useGameTimeZone, wallClockToIso, zonedParts } from '../../lib/gameTime'

type LeaderboardRow = {
  code: string
  display_name: string
  scope: string
  record_type: string
  sort_type: string
  rotation: string
  rotation_period_seconds: number | null
  rotation_anchor_at: string | null
  rotation_tz: string | null
  rotation_count: number
  next_rotation_at: string | null
  ends_at: string | null
  is_active: boolean
  is_ended: boolean
  current_entries: number
  columns: string
}

type FormState = {
  code: string
  displayName: string
  scope: string
  recordType: string
  sortType: string
  rotation: string
  periodHours: string
  anchorTime: string      // daily·weekly·monthly — 'HH:MM'
  anchorWeekday: string   // weekly — 0(일)~6(토)
  anchorDay: string       // monthly — 1~31
  anchorAt: string        // custom — datetime-local
  hasEndsAt: boolean
  endsAt: string
  isActive: boolean
  tz: string              // 이 리더보드의 시간대 — 입력칸 시각을 이것으로 읽는다
}

const DEFAULT_FORM: FormState = {
  code: '', displayName: '', scope: 'global', recordType: 'highest',
  sortType: 'desc', rotation: 'none', periodHours: '',
  anchorTime: '', anchorWeekday: '1', anchorDay: '1', anchorAt: '',
  hasEndsAt: false, endsAt: '', isActive: true, tz: DEFAULT_TIME_ZONE,
}

const SCOPE_LABEL: Record<string, string> = { global: '전체 통합', server: '서버별' }
const RECORD_LABEL: Record<string, string> = { highest: '최고', lowest: '최저', last: '최신', sum: '누적' }
const SORT_LABEL: Record<string, string> = { desc: '높은 순', asc: '낮은 순' }
const ROTATION_LABEL: Record<string, string> = {
  none: '없음', daily: '일간', weekly: '주간', monthly: '월간', custom: '직접 지정',
}

const ROTATION_HINT: Record<string, string> = {
  none: '초기화하지 않고 계속 누적됩니다.',
  daily: '매일 기준 시각에 초기화됩니다.',
  weekly: '매주 기준 요일의 기준 시각에 초기화됩니다.',
  monthly: '매월 기준 날짜의 기준 시각에 초기화됩니다. 그 달에 없는 날짜는 말일로 맞춰집니다.',
  custom: '시작 일시부터 주기마다 초기화됩니다.',
}

const CODE_PATTERN = /^[a-z][a-z0-9_]*$/

const WEEKDAYS = ['일', '월', '화', '수', '목', '금', '토']

const pad2 = (n: number) => String(n).padStart(2, '0')

// ── 회전 기준시각·종료 시각은 그 리더보드의 시간대(form.tz)로 읽고 쓴다 ──────────────
// 서버가 앵커의 그 시간대 벽시계(시각·요일·일)를 기준으로 회차 경계를 잡으므로,
// 운영자 브라우저 시간대와 무관하게 같은 시간대로 만들고 되읽어야 어긋나지 않는다.
// 새 리더보드는 게임 시간대, 이미 있는 리더보드는 만들 때 정해진 자기 시간대를 쓴다.

/** 저장된 앵커(timestamptz)를 tz 기준 입력값으로 되돌립니다. */
function anchorToFields(iso: string | null | undefined, tz: string) {
  const d = iso ? new Date(iso) : null
  if (!d || Number.isNaN(d.getTime())) return { anchorTime: '', anchorWeekday: '1', anchorDay: '1', anchorAt: '' }
  const p = zonedParts(d, tz)
  return {
    anchorTime: `${pad2(p.hh)}:${pad2(p.mm)}`,
    anchorWeekday: String(p.dow),
    anchorDay: String(p.d),
    anchorAt: isoToLocalInput(iso, tz),
  }
}

/** 주기별 입력값을 서버에 보낼 앵커(ISO)로 조립합니다. form.tz 기준. 지정하지 않았으면 null. */
function fieldsToAnchorIso(form: FormState): string | null {
  if (form.rotation === 'none') return null
  if (form.rotation === 'custom') {
    return form.anchorAt ? localInputToIso(form.anchorAt, form.tz) : null
  }
  if (!form.anchorTime) return null

  const [hh = 0, mm = 0] = form.anchorTime.split(':').map(Number)

  if (form.rotation === 'weekly') {
    // 2000-01-02 = 일요일. 목표 요일(0~6)만큼 더한다(전부 1월 내: 2~8일).
    return wallClockToIso(form.tz, 2000, 1, 2 + Number(form.anchorWeekday), hh, mm)
  }
  if (form.rotation === 'monthly') {
    // 31일까지 있는 1월에 지정 날짜로.
    return wallClockToIso(form.tz, 2000, 1, Number(form.anchorDay), hh, mm)
  }
  // daily — 시각만 의미. 고정 날짜 사용.
  return wallClockToIso(form.tz, 2000, 1, 3, hh, mm)
}

function Toggle({ value, onChange }: { value: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={value}
      onClick={() => onChange(!value)}
      className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${
        value ? 'bg-[#1677ff]' : 'bg-neutral-300'
      }`}
    >
      <span
        className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
          value ? 'translate-x-4' : 'translate-x-0.5'
        }`}
      />
    </button>
  )
}

export default function LeaderboardsTab({
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
  const [modalOpen, setModalOpen] = useState(false)
  const [editCode, setEditCode] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(DEFAULT_FORM)
  const [touched, setTouched] = useState(false)
  const gameTz = useGameTimeZone(target)
  const [delTarget, setDelTarget] = useState<LeaderboardRow | null>(null)
  const [rotateTarget, setRotateTarget] = useState<LeaderboardRow | null>(null)
  const [acting, setActing] = useState(false)
  const [error, setError] = useState('')
  // 모달·확인 창 안에서 난 실패. 페이지 배너는 창의 어두운 배경에 가려 보이지 않는다.
  const [dialogError, setDialogError] = useState('')
  const [saving, setSaving] = useState(false)

  const [rows, setRows] = useState<LeaderboardRow[]>([])
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const { drafts: allDrafts, error: draftsError, reload: reloadDrafts } = usePendingChanges()
  const drafts = allDrafts ?? []
  const [discarding, setDiscarding] = useState(false)

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

  const reportInDialog = (e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setDialogError(e instanceof Error ? e.message : fallback)
  }

  // 대기 중 변경 조회 실패(인증 실패 제외)는 공유 컨텍스트가 조용히 삼키므로, 여기로 넘어오면
  // 기존 에러 배너에 실어 보여준다.
  useEffect(() => {
    if (draftsError) setError(draftsError)
  }, [draftsError])

  const reloadRows = useCallback(async () => {
    setLoading(true)
    setLoadFailed(false)
    try {
      const lb = await callAdmin<{ rows: LeaderboardRow[] }>(target, 'leaderboard.list')
      setRows(lb.rows)
    } catch (e: unknown) {
      setLoadFailed(true)
      report(e, '불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void reloadRows() }, [reloadRows])

  // 리더보드 생성·수정·삭제 중 게시 대기(draft)인 것만 추린다. 목록 표는 라이브 상태를 그대로 보여준다.
  const pendingItems: PendingItem[] = drafts
    .filter((d) => d.feature === 'leaderboard_table')
    .map((d) => ({ id: d.id, label: opLabel(d.feature, d.action), detail: d.object_name }))
  // 같은 리더보드에 변경이 여러 개 담길 수 있다 — 삭제가 하나라도 있으면 그게 게시 결과이므로 우선 표시한다.
  const draftStateOf = (code: string): 'delete' | 'pending' | null => {
    const mine = drafts.filter((d) => d.feature === 'leaderboard_table' && d.object_name === code)
    if (mine.length === 0) return null
    return mine.some((d) => d.action === 'delete') ? 'delete' : 'pending'
  }

  const discardOne = async (id: number) => {
    setDiscarding(true)
    try {
      await callAdmin(target, 'schema.discardDraft', { id })
      await reloadDrafts()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setDiscarding(false)
    }
  }

  const openAdd = () => {
    setEditCode(null); setForm({ ...DEFAULT_FORM, tz: gameTz ?? DEFAULT_TIME_ZONE }); setTouched(false); setDialogError(''); setModalOpen(true)
  }
  const openEdit = (r: LeaderboardRow) => {
    setDialogError('')
    setEditCode(r.code)
    const tz = r.rotation_tz || gameTz || DEFAULT_TIME_ZONE
    setForm({
      code: r.code, displayName: r.display_name, scope: r.scope,
      recordType: r.record_type, sortType: r.sort_type, rotation: r.rotation,
      periodHours: r.rotation_period_seconds ? String(r.rotation_period_seconds / 3600) : '',
      ...anchorToFields(r.rotation_anchor_at, tz),
      hasEndsAt: !!r.ends_at,
      endsAt: isoToLocalInput(r.ends_at, tz),
      isActive: r.is_active,
      tz,
    })
    setTouched(false)
    setModalOpen(true)
  }
  const closeModal = () => {
    setModalOpen(false); setEditCode(null); setForm(DEFAULT_FORM); setTouched(false)
  }

  const setF = (key: keyof FormState) => (e: ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
    const v = e.target.type === 'checkbox' ? (e.target as HTMLInputElement).checked : e.target.value
    setForm((prev) => ({ ...prev, [key]: v } as FormState))
  }

  const codeValid = editCode !== null || CODE_PATTERN.test(form.code.trim())
  const nameValid = form.displayName.trim().length > 0
  const periodValid = form.rotation !== 'custom' || Number(form.periodHours) > 0
  const endsAtValid = !form.hasEndsAt || form.endsAt.length > 0
  const valid = codeValid && nameValid && periodValid && endsAtValid

  const handleSave = async () => {
    setTouched(true)
    if (!valid || saving) return
    const code = editCode ?? form.code.trim()
    // 이미 있는 코드로 "추가"를 게시하면 기존 리더보드 설정을 덮어쓰고, 되돌리면 기존 기록까지 지워진다.
    if (!editCode) {
      if (loading || loadFailed || allDrafts === null) {
        setDialogError('목록을 불러오지 못해 코드가 겹치는지 확인할 수 없습니다. 새로고침한 뒤 다시 시도해 주세요.')
        return
      }
      if (rows.some((r) => r.code === code)) {
        setDialogError('이미 있는 리더보드 코드입니다. 다른 코드를 입력하거나, 목록에서 그 리더보드를 수정하세요.')
        return
      }
      if (drafts.some((d) => d.feature === 'leaderboard_table' && d.action === 'create' && d.object_name === code)) {
        setDialogError('같은 코드의 리더보드 추가가 이미 게시를 기다리고 있습니다.')
        return
      }
    }
    setSaving(true)
    setDialogError('')
    try {
      await callAdmin(target, 'schema.stage', {
        feature: 'leaderboard_table',
        action: editCode ? 'update' : 'create',
        objectName: code,
        params: {
          code,
          display_name: form.displayName.trim(),
          scope: form.scope,
          record_type: form.recordType,
          sort_type: form.sortType,
          rotation: form.rotation,
          period_seconds: form.rotation === 'custom' && form.periodHours ? Math.round(Number(form.periodHours) * 3600) : null,
          anchor_at: fieldsToAnchorIso(form),
          tz: form.tz,
          ends_at: form.hasEndsAt && form.endsAt ? localInputToIso(form.endsAt, form.tz) : null,
          is_active: form.isActive,
        },
      })
      closeModal()
      await reloadDrafts()
    } catch (e: unknown) {
      reportInDialog(e, '실패했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const confirmDelete = async () => {
    if (!delTarget || acting) return
    setActing(true)
    setDialogError('')
    try {
      await callAdmin(target, 'schema.stage', {
        feature: 'leaderboard_table',
        action: 'delete',
        objectName: delTarget.code,
        params: { code: delTarget.code },
      })
      if (selectedCode === delTarget.code) onSelect('')
      setDelTarget(null)
      await reloadDrafts()
    } catch (e: unknown) {
      reportInDialog(e, '실패했습니다.')
    } finally { setActing(false) }
  }

  const confirmRotate = async () => {
    if (!rotateTarget || acting) return
    setActing(true)
    setDialogError('')
    try {
      await callAdmin(target, 'leaderboard.rotate', { code: rotateTarget.code })
      setRotateTarget(null)
      await reloadRows()
    } catch (e: unknown) {
      reportInDialog(e, '실패했습니다.')
    } finally { setActing(false) }
  }

  const inp = 'w-full h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'
  const lbl = 'block text-xs text-neutral-500 mb-1'
  const hint = 'text-xs text-neutral-400 mt-1'
  const err = 'text-xs text-red-500 mt-1'

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <PendingChanges items={pendingItems} onDiscard={(id) => void discardOne(id)} busy={discarding} />

      <div className="flex justify-end">
        <Button variant="primary" size="compact"
          onClick={openAdd}
          disabled={!gameTz}
        >
          <Plus className="w-3.5 h-3.5" />리더보드 추가
        </Button>
      </div>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium whitespace-nowrap">상태</th>
                <th className="text-left px-4 py-2.5 font-medium">코드</th>
                <th className="text-left px-4 py-2.5 font-medium">이름</th>
                <th className="text-left px-4 py-2.5 font-medium whitespace-nowrap">범위</th>
                <th className="text-left px-4 py-2.5 font-medium whitespace-nowrap">기록</th>
                <th className="text-left px-4 py-2.5 font-medium whitespace-nowrap">정렬</th>
                <th className="text-left px-4 py-2.5 font-medium whitespace-nowrap">주기</th>
                <th className="text-right px-4 py-2.5 font-medium">회차</th>
                <th className="text-right px-4 py-2.5 font-medium">참여</th>
                <th className="text-left px-4 py-2.5 font-medium">다음 초기화</th>
                <th className="text-left px-4 py-2.5 font-medium">작업</th>
              </tr>
            </thead>
            <tbody>
              {loading || loadFailed || rows.length === 0 ? (
                <TableStatusRow
                  loading={loading}
                  failed={loadFailed}
                  empty={rows.length === 0}
                  colSpan={11}
                  emptyText="등록된 리더보드가 없습니다."
                />
              ) : (
                rows.map((r) => {
                  const draftState = draftStateOf(r.code)
                  const noCycle = r.rotation === 'none'
                  return (
                  <tr
                    key={r.code}
                    className={
                      'border-t border-neutral-100 hover:bg-neutral-50 cursor-pointer' +
                      (selectedCode === r.code ? ' bg-brand-soft' : '')
                    }
                    onClick={() => onSelect(selectedCode === r.code ? '' : r.code)}
                  >
                    <td className="px-4 py-3 whitespace-nowrap">
                      <div className="flex items-center gap-1.5">
                        {r.is_ended ? (
                          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-neutral-100 text-neutral-600">종료</span>
                        ) : (
                          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-emerald-100 text-emerald-700">진행 중</span>
                        )}
                        {draftState === 'delete' ? (
                          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-red-100 text-red-700">삭제 대기</span>
                        ) : draftState ? (
                          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-amber-100 text-amber-700" title="게시하면 적용됩니다">대기 중</span>
                        ) : null}
                      </div>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs">{r.code}</td>
                    <td className="px-4 py-3">{r.display_name}</td>
                    <td className="px-4 py-3 whitespace-nowrap">{SCOPE_LABEL[r.scope] ?? r.scope}</td>
                    <td className="px-4 py-3 whitespace-nowrap">{RECORD_LABEL[r.record_type] ?? r.record_type}</td>
                    <td className="px-4 py-3 whitespace-nowrap">{SORT_LABEL[r.sort_type] ?? r.sort_type}</td>
                    <td className="px-4 py-3 whitespace-nowrap">{ROTATION_LABEL[r.rotation] ?? r.rotation}</td>
                    <td className="px-4 py-3 text-right">{formatCount(r.rotation_count)}</td>
                    <td className="px-4 py-3 text-right">{formatCount(r.current_entries)}</td>
                    <td className="px-4 py-3">{formatDateTime(r.next_rotation_at)}</td>
                    <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => openEdit(r)}
                          title="수정"
                          className="p-1.5 rounded hover:bg-neutral-100 text-neutral-500 hover:text-neutral-800"
                        >
                          <Pencil className="w-3.5 h-3.5" />
                        </button>
                        {/* 주기 없는 리더보드는 기록 탭에 회차 선택이 없어, 넘기면 이전 기록을 다시 볼 길이 없다. */}
                        <button
                          onClick={() => { setDialogError(''); setRotateTarget(r) }}
                          disabled={noCycle}
                          title={noCycle ? '초기화 주기가 없는 리더보드는 회차를 넘길 수 없습니다' : '회차 넘기기'}
                          aria-label="회차 넘기기"
                          className="p-1.5 rounded hover:bg-neutral-100 text-neutral-500 hover:text-neutral-800 disabled:opacity-30 disabled:hover:bg-transparent disabled:cursor-not-allowed"
                        >
                          <RotateCw className="w-3.5 h-3.5" />
                        </button>
                        <button
                          onClick={() => { setDialogError(''); setDelTarget(r) }}
                          title="삭제"
                          className="p-1.5 rounded hover:bg-red-50 text-neutral-500 hover:text-red-600"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      {modalOpen && (
        <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl max-h-[90vh] flex flex-col">
            <div className="flex items-center justify-between px-5 py-4 border-b">
              <h3 className="text-base font-semibold">
                {editCode ? '리더보드 수정' : '리더보드 추가'}
              </h3>
              <button onClick={closeModal} disabled={saving} className="text-neutral-400 hover:text-neutral-700" title="닫기" aria-label="닫기">
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="p-5 space-y-5 overflow-auto">
              {/* 1행 — 코드 · 표시 이름 */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className={lbl}>
                    코드 <span className="text-red-500">*</span>
                  </label>
                  <input
                    value={form.code}
                    onChange={setF('code')}
                    disabled={editCode !== null}
                    className={inp + (editCode ? ' opacity-50 cursor-not-allowed' : '')}
                  />
                  {editCode ? (
                    <p className={hint}>코드는 만든 뒤 바꿀 수 없습니다.</p>
                  ) : touched && !codeValid ? (
                    <p className={err}>영문 소문자로 시작하고 소문자·숫자·_ 만 사용하세요.</p>
                  ) : (
                    <p className={hint}>게임 코드에서 이 리더보드를 부르는 이름입니다. 예: weekly_score</p>
                  )}
                </div>

                <div>
                  <label className={lbl}>
                    표시 이름 <span className="text-red-500">*</span>
                  </label>
                  <input
                    value={form.displayName}
                    onChange={setF('displayName')}
                    className={inp}
                  />
                  {touched && !nameValid
                    ? <p className={err}>표시 이름을 입력해주세요.</p>
                    : <p className={hint}>운영 화면에 표시되는 이름입니다.</p>}
                </div>
              </div>

              {/* 2행 — 집계 범위 · 점수 기록 방식 · 순위 정렬 */}
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div>
                  <label className={lbl}>집계 범위</label>
                  <select value={form.scope} onChange={setF('scope')} className={inp}>
                    <option value="global">전체 통합</option>
                    <option value="server">서버별</option>
                  </select>
                </div>

                <div>
                  <label className={lbl}>점수 기록 방식</label>
                  <select value={form.recordType} onChange={setF('recordType')} className={inp}>
                    <option value="highest">최고</option>
                    <option value="lowest">최저</option>
                    <option value="last">최신</option>
                    <option value="sum">누적</option>
                  </select>
                </div>

                <div>
                  <label className={lbl}>순위 정렬</label>
                  <select value={form.sortType} onChange={setF('sortType')} className={inp}>
                    <option value="desc">높은 순</option>
                    <option value="asc">낮은 순</option>
                  </select>
                </div>
              </div>

              {/* 3행 — 초기화 주기 */}
              <div className="border-t border-neutral-100 pt-4">
                <div className={`grid grid-cols-1 gap-4 ${form.rotation === 'custom' ? 'sm:grid-cols-4' : 'sm:grid-cols-3'}`}>
                  <div>
                    <label className={lbl}>초기화 주기</label>
                    <select value={form.rotation} onChange={setF('rotation')} className={inp}>
                      <option value="none">없음</option>
                      <option value="daily">일간</option>
                      <option value="weekly">주간</option>
                      <option value="monthly">월간</option>
                      <option value="custom">직접 지정</option>
                    </select>
                  </div>

                  {form.rotation === 'custom' && (
                    <div>
                      <label className={lbl}>주기</label>
                      <div className="relative">
                        <input
                          type="number"
                          min="0"
                          step="0.5"
                          value={form.periodHours}
                          onChange={setF('periodHours')}
                          className={inp + ' pr-12'}
                        />
                        <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm text-neutral-400 pointer-events-none">
                          시간
                        </span>
                      </div>
                      {touched && !periodValid
                        ? <p className={err}>0보다 큰 숫자를 입력해주세요.</p>
                        : <p className={hint}>일주일은 168시간입니다.</p>}
                    </div>
                  )}

                  {form.rotation === 'weekly' && (
                    <div>
                      <label className={lbl}>기준 요일</label>
                      <select value={form.anchorWeekday} onChange={setF('anchorWeekday')} className={inp}>
                        {WEEKDAYS.map((w, i) => (
                          <option key={i} value={String(i)}>{w}요일</option>
                        ))}
                      </select>
                    </div>
                  )}

                  {form.rotation === 'monthly' && (
                    <div>
                      <label className={lbl}>기준 날짜</label>
                      <select value={form.anchorDay} onChange={setF('anchorDay')} className={inp}>
                        {Array.from({ length: 31 }, (_, i) => i + 1).map((d) => (
                          <option key={d} value={String(d)}>{d}일</option>
                        ))}
                      </select>
                    </div>
                  )}

                  {(form.rotation === 'daily' || form.rotation === 'weekly' || form.rotation === 'monthly') && (
                    <div>
                      <label className={lbl}>기준 시각</label>
                      <input
                        type="time"
                        value={form.anchorTime}
                        onChange={setF('anchorTime')}
                        className={inp}
                      />
                    </div>
                  )}

                  {form.rotation === 'custom' && (
                    <div className="sm:col-span-2">
                      <label className={lbl}>시작 일시</label>
                      <input
                        type="datetime-local"
                        value={form.anchorAt}
                        onChange={setF('anchorAt')}
                        className={inp}
                      />
                    </div>
                  )}
                </div>
                <p className={hint}>{ROTATION_HINT[form.rotation] ?? ROTATION_HINT['none']}</p>
                {form.rotation !== 'none' && (
                  <p className={hint}>기준 시각은 {timeZoneName(form.tz)}으로 저장됩니다.</p>
                )}
                {form.rotation !== 'none' && !fieldsToAnchorIso(form) && (
                  <p className={hint}>기준을 비우면 저장하는 시각이 기준이 됩니다.</p>
                )}
              </div>

              {/* 4행 — 종료 예약 */}
              <div className="border-t border-neutral-100 pt-4">
                <div className="flex items-center gap-3">
                  <label className="text-xs text-neutral-500">종료 예약</label>
                  <Toggle
                    value={form.hasEndsAt}
                    onChange={(v) =>
                      setForm((prev) => ({ ...prev, hasEndsAt: v, endsAt: v ? prev.endsAt : '' }))
                    }
                  />
                  {form.hasEndsAt && (
                    <input
                      type="datetime-local"
                      value={form.endsAt}
                      onChange={setF('endsAt')}
                      className={inp + ' max-w-[240px]'}
                    />
                  )}
                </div>
                {form.hasEndsAt && touched && !endsAtValid && (
                  <p className={err}>종료 시각을 지정해주세요.</p>
                )}
                <p className={hint}>
                  리더보드가 완전히 끝나는 시점입니다. 초기화 주기와는 따로 동작하며, 종료 후에는 순위 조회만 됩니다. 시각은 {timeZoneName(form.tz)} 기준입니다.
                </p>
              </div>

              {/* 사용 여부 — 수정할 때만 노출. 새로 만드는 리더보드는 항상 사용 중으로 시작합니다. */}
              {editCode && (
                <div className="border-t border-neutral-100 pt-4">
                  <div className="flex items-center gap-3 mb-1">
                    <label className="text-xs text-neutral-500">사용 중</label>
                    <Toggle
                      value={form.isActive}
                      onChange={(v) => setForm((prev) => ({ ...prev, isActive: v }))}
                    />
                  </div>
                  <p className={hint}>
                    끄면 즉시 종료 처리됩니다. 게임의 리더보드 목록에서 빠지고 새 기록도 받지 않으며, 회차 전환도 멈춥니다. 순위 조회는 계속 됩니다.
                  </p>
                </div>
              )}
            </div>

            {dialogError && (
              <div className="mx-5 mb-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{dialogError}</div>
            )}
            <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
              <Button variant="outline"
                onClick={closeModal}
                disabled={saving}
              >
                <X className="w-3.5 h-3.5" />
                취소
              </Button>
              <Button variant="primary"
                onClick={() => { void handleSave() }}
                disabled={saving}
              >
                {saving
                  ? <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  : <Plus className="w-3.5 h-3.5" />}
                변경 담기
              </Button>
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={!!delTarget}
        title="리더보드 삭제"
        confirmLabel="삭제"
        danger
        busy={acting}
        error={dialogError}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDelTarget(null)}
        description="게시하면 지난 회차까지 포함해 이 리더보드의 기록이 모두 사라집니다. 게시 전에는 변경 관리에서 취소할 수 있습니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.display_name}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{delTarget.code}</div>
          </div>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!rotateTarget}
        title="회차 넘기기"
        confirmLabel="회차 넘기기"
        danger
        busy={acting}
        error={dialogError}
        onConfirm={() => void confirmRotate()}
        onCancel={() => setRotateTarget(null)}
        description="현재 순위가 지난 회차로 넘어가고 새 회차가 시작됩니다. 즉시 적용되며 변경 관리·롤백 대상이 아닙니다."
      >
        {rotateTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{rotateTarget.display_name}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{rotateTarget.code}</div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
