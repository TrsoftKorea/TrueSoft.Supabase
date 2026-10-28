import { useCallback, useEffect, useState } from 'react'
import { Trash2, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { serverTargetLabel, useServerName } from '../lib/serverNames'
import { platformLabel } from '../lib/platforms'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { formatDateTime, itemsText } from '../components/ui/format'
import { timeZoneName } from '../lib/gameTime'

type ScheduleRow = {
  id: string; schedule_type: string; target_mode: string; title: string
  items: { key: string; count: number }[] | null; category: string; scheduled_at: string | null
  repeat_time: string | null; repeat_tz: string | null; repeat_unit: string | null; repeat_dow: number | null; repeat_dom: number | null
  next_run_at: string; is_active: boolean; run_count: number
  created_by: string | null; server_code: string | null; platform: string | null; open_for_new: boolean
}
type GameItem = { key: string; display_name: string }

const MODE_LABEL: Record<string, string> = { all: '전체', server: '서버', players: '플레이어' }
const SCHED_LABEL: Record<string, string> = { scheduled: '예약 발송', repeat: '반복 발송' }
const DOW_LABEL = ['일', '월', '화', '수', '목', '금', '토']

/** "매일 한국 시간 09:00", "매주 월요일 일본 시간 09:00" — 반복 시각은 예약마다 만들 때의 시간대로 돈다. */
function repeatText(r: ScheduleRow): string {
  const time = r.repeat_time ? `${r.repeat_tz ? ` ${timeZoneName(r.repeat_tz)}` : ''} ${r.repeat_time.slice(0, 5)}` : ''
  if (r.repeat_unit === 'week' && r.repeat_dow != null) return `매주 ${DOW_LABEL[r.repeat_dow] ?? '?'}요일${time}`
  if (r.repeat_unit === 'month' && r.repeat_dom != null) return `매월 ${r.repeat_dom}일${time}`
  return `매일${time}`
}

export default function MailSchedules({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const serverName = useServerName(target)
  const [rows, setRows] = useState<ScheduleRow[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [error, setError] = useState('')
  const [catalog, setCatalog] = useState<GameItem[]>([])
  const [delTarget, setDelTarget] = useState<ScheduleRow | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [delError, setDelError] = useState('')
  const [togglingId, setTogglingId] = useState<string | null>(null)

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
    // 시작할 때 지운다 — 성공했을 때 지우면 함께 나간 아이템 조회의 실패 표시까지 사라진다.
    setError('')
    try {
      const res = await callAdmin<{ rows: ScheduleRow[] }>(target, 'mails.getSchedules')
      setRows(res.rows)
      setFailed(false)
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setRows([])
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    void (async () => {
      try {
        setCatalog(await callAdmin<GameItem[]>(target, 'gameItems.list'))
      } catch (e: unknown) {
        report(e, '아이템 목록을 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  const toggleActive = async (r: ScheduleRow) => {
    if (togglingId) return
    setTogglingId(r.id)
    try {
      await callAdmin(target, 'mails.setScheduleActive', { id: r.id, isActive: !r.is_active })
      await load()
    } catch (e: unknown) {
      report(e, r.is_active ? '끄지 못했습니다.' : '켜지 못했습니다.')
    } finally {
      setTogglingId(null)
    }
  }

  const confirmDelete = async () => {
    if (!delTarget || deleting) return
    setDeleting(true)
    setDelError('')
    try {
      await callAdmin(target, 'mails.deleteSchedule', { id: delTarget.id })
      setDelTarget(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDelError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="예약 목록" description="등록한 예약·반복 발송을 켜고 끄거나 지웁니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-20">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">종류</th>
                <th className="text-left px-4 py-2.5 font-medium w-44">다음 실행</th>
                <th className="text-left px-4 py-2.5 font-medium">대상</th>
                {/* 글이 긴 두 칸은 최소 폭을 둔다 — 없으면 좁은 창에서 다른 칸에 밀려 한 글자씩 깨진다(그때는 표가 옆으로 넘어간다). */}
                <th className="text-left px-4 py-2.5 font-medium min-w-[10rem]">제목</th>
                <th className="text-left px-4 py-2.5 font-medium min-w-[7rem]">아이템</th>
                <th className="text-left px-4 py-2.5 font-medium w-20">운영자</th>
                <th className="px-4 py-2.5 w-28"></th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={8} emptyText="예약이 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50">
                    <td className="px-4 py-3 whitespace-nowrap">
                      <span className={'inline-flex items-center px-2 py-0.5 rounded text-xs whitespace-nowrap ' + (r.is_active ? 'bg-emerald-100 text-emerald-700' : 'bg-neutral-200 text-neutral-500')}>
                        {r.is_active ? '켜짐' : '꺼짐'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">
                      {SCHED_LABEL[r.schedule_type] ?? r.schedule_type}
                      {r.schedule_type === 'repeat' && <div className="text-xs text-neutral-500">{repeatText(r)}</div>}
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.next_run_at)}</td>
                    <td className="px-4 py-3">
                      {/* 종류 칸처럼 주 대상 한 줄 + 조건은 작은 글씨 — 한 줄에 다 이으면 좁은 칸에서 글자 단위로 깨진다. */}
                      <div className="whitespace-nowrap">{r.server_code ? serverTargetLabel(serverName(r.server_code)) : MODE_LABEL[r.target_mode] ?? r.target_mode}</div>
                      {(() => {
                        const conds = [r.platform ? platformLabel(r.platform) : null, r.open_for_new && r.schedule_type !== 'repeat' ? '나중에 들어온 플레이어도' : null].filter(Boolean)
                        return conds.length > 0 && <div className="text-xs text-neutral-500">{conds.join(' · ')}</div>
                      })()}
                    </td>
                    <td className="px-4 py-3">{r.title}</td>
                    <td className="px-4 py-3 text-neutral-600">{itemsText(r.items, catalog)}</td>
                    <td className="px-4 py-3 text-neutral-600">{r.created_by ?? '-'}</td>
                    <td className="px-4 py-3 text-right whitespace-nowrap">
                      <Button size="sm" className="mr-3 align-middle" onClick={() => void toggleActive(r)} disabled={togglingId !== null}>
                        {togglingId === r.id && <Loader2 className="w-3 h-3 animate-spin" />}
                        {r.is_active ? '끄기' : '켜기'}
                      </Button>
                      <button onClick={() => { setDelError(''); setDelTarget(r) }} title="삭제" aria-label="삭제" className="text-neutral-400 hover:text-red-500 align-middle">
                        <Trash2 className="w-4 h-4 inline" />
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      <ConfirmDialog
        open={!!delTarget}
        title="예약 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        error={delError || undefined}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDelTarget(null)}
        description="이미 발송된 우편은 그대로 남고, 앞으로 이 예약은 실행되지 않습니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.title}</div>
            <div className="text-xs text-neutral-500 mt-1">
              {delTarget.schedule_type === 'repeat' ? `반복 발송 · ${repeatText(delTarget)}` : SCHED_LABEL[delTarget.schedule_type] ?? delTarget.schedule_type} · 다음 실행 {formatDateTime(delTarget.next_run_at)}
            </div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
