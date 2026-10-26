import { useCallback, useEffect, useState } from 'react'
import { Trash2, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { useServerName } from '../lib/serverNames'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime, itemsText } from '../components/ui/format'

type ScheduleRow = {
  id: string; schedule_type: string; target_mode: string; title: string
  items: { key: string; count: number }[] | null; category: string; scheduled_at: string | null
  repeat_time: string | null; repeat_unit: string | null; repeat_dow: number | null; repeat_dom: number | null
  next_run_at: string; is_active: boolean; run_count: number
  created_by: string | null; server_code: string | null
}
type GameItem = { key: string; display_name: string }

const MODE_LABEL: Record<string, string> = { all: '전체', server: '서버', players: '플레이어' }
const SCHED_LABEL: Record<string, string> = { scheduled: '예약 발송', repeat: '반복 발송' }
const DOW_LABEL = ['일', '월', '화', '수', '목', '금', '토']

/** "매일 09:00", "매주 월요일 09:00", "매월 30일 09:00" */
function repeatText(r: ScheduleRow): string {
  const time = r.repeat_time ? ` ${r.repeat_time.slice(0, 5)}` : ''
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
      <PageHeader title="예약 목록" description="예약·반복 발송으로 등록된 우편 목록입니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-20">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">종류</th>
                <th className="text-left px-4 py-2.5 font-medium w-44">다음 실행</th>
                <th className="text-left px-4 py-2.5 font-medium w-20">대상</th>
                <th className="text-left px-4 py-2.5 font-medium">제목</th>
                <th className="text-left px-4 py-2.5 font-medium">아이템</th>
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
                    <td className="px-4 py-3">
                      <span className={'inline-flex items-center px-2 py-0.5 rounded text-xs ' + (r.is_active ? 'bg-emerald-100 text-emerald-700' : 'bg-neutral-200 text-neutral-500')}>
                        {r.is_active ? '켜짐' : '꺼짐'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">
                      {SCHED_LABEL[r.schedule_type] ?? r.schedule_type}
                      {r.schedule_type === 'repeat' && <div className="text-xs text-neutral-500">{repeatText(r)}</div>}
                    </td>
                    <td className="px-4 py-3 text-neutral-600">{formatDateTime(r.next_run_at)}</td>
                    <td className="px-4 py-3">{MODE_LABEL[r.target_mode] ?? r.target_mode}{r.server_code ? ` (${serverName(r.server_code)})` : ''}</td>
                    <td className="px-4 py-3">{r.title}</td>
                    <td className="px-4 py-3 text-neutral-600">{itemsText(r.items, catalog)}</td>
                    <td className="px-4 py-3 text-neutral-600">{r.created_by ?? '-'}</td>
                    <td className="px-4 py-3 text-right whitespace-nowrap">
                      <button
                        onClick={() => void toggleActive(r)}
                        disabled={togglingId !== null}
                        className="mr-3 inline-flex items-center gap-1 h-7 px-2.5 rounded border border-neutral-300 text-xs text-neutral-700 hover:bg-neutral-50 disabled:opacity-50 whitespace-nowrap align-middle"
                      >
                        {togglingId === r.id && <Loader2 className="w-3 h-3 animate-spin" />}
                        {r.is_active ? '끄기' : '켜기'}
                      </button>
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
