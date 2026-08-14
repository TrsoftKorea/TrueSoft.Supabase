import { useCallback, useEffect, useState } from 'react'
import { Trash2, Power } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { formatDateTime } from '../components/ui/format'

type ScheduleRow = {
  id: string; schedule_type: string; target_mode: string; title: string
  items: { key: string; count: number }[] | null; category: string; scheduled_at: string | null
  repeat_time: string | null; next_run_at: string; is_active: boolean; run_count: number
  created_by: string | null; server_code: string | null
}

const MODE_LABEL: Record<string, string> = { all: '전체', server: '서버', players: '플레이어' }
const SCHED_LABEL: Record<string, string> = { scheduled: '예약 발송', repeat: '반복 발송' }

export default function MailSchedules({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<ScheduleRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [delTarget, setDelTarget] = useState<ScheduleRow | null>(null)
  const [deleting, setDeleting] = useState(false)

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
      const res = await callAdmin<{ rows: ScheduleRow[] }>(target, 'mails.getSchedules')
      setRows(res.rows)
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

  const toggleActive = async (r: ScheduleRow) => {
    try {
      await callAdmin(target, 'mails.setScheduleActive', { id: r.id, isActive: !r.is_active })
      await load()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    }
  }

  const confirmDelete = async () => {
    if (!delTarget || deleting) return
    setDeleting(true)
    try {
      await callAdmin(target, 'mails.deleteSchedule', { id: delTarget.id })
      setDelTarget(null)
      await load()
    } catch (e: unknown) {
      report(e, '삭제에 실패했습니다.')
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="예약 목록" description="예약·반복 발송으로 등록된 우편 목록입니다." />

      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
          {error}
        </div>
      )}

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-20">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">타입</th>
                <th className="text-left px-4 py-2.5 font-medium w-44">다음 실행</th>
                <th className="text-left px-4 py-2.5 font-medium w-20">대상</th>
                <th className="text-left px-4 py-2.5 font-medium">제목</th>
                <th className="text-left px-4 py-2.5 font-medium">아이템</th>
                <th className="text-left px-4 py-2.5 font-medium w-20">운영자</th>
                <th className="px-4 py-2.5 w-24"></th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={8} emptyText="예약이 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr key={r.id} className="border-t border-neutral-100 hover:bg-neutral-50">
                    <td className="px-4 py-3">
                      <span className={'inline-flex items-center px-2 py-0.5 rounded text-xs ' + (r.is_active ? 'bg-emerald-100 text-emerald-700' : 'bg-neutral-200 text-neutral-500')}>
                        {r.is_active ? '활성' : '비활성'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-neutral-600">
                      {SCHED_LABEL[r.schedule_type] ?? r.schedule_type}
                      {r.schedule_type === 'repeat' && r.repeat_time ? ` (${r.repeat_time.slice(0, 5)})` : ''}
                    </td>
                    <td className="px-4 py-3 text-neutral-600">{formatDateTime(r.next_run_at)}</td>
                    <td className="px-4 py-3">{MODE_LABEL[r.target_mode] ?? r.target_mode}{r.server_code ? ` (${r.server_code})` : ''}</td>
                    <td className="px-4 py-3">{r.title}</td>
                    <td className="px-4 py-3 text-neutral-600">{(r.items ?? []).map((it) => `${it.key}×${it.count}`).join(', ') || '-'}</td>
                    <td className="px-4 py-3 text-neutral-600">{r.created_by ?? '-'}</td>
                    <td className="px-4 py-3 text-right whitespace-nowrap">
                      <button
                        onClick={() => void toggleActive(r)}
                        title={r.is_active ? '비활성화' : '활성화'}
                        className={'mr-2 ' + (r.is_active ? 'text-emerald-600 hover:text-emerald-700' : 'text-neutral-400 hover:text-neutral-600')}
                      >
                        <Power className="w-4 h-4 inline" />
                      </button>
                      <button onClick={() => setDelTarget(r)} title="삭제" className="text-neutral-400 hover:text-red-500">
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
        title="예약 지우기"
        confirmLabel="지우기"
        danger
        busy={deleting}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDelTarget(null)}
        description="이미 발송된 우편은 그대로 남고, 앞으로 이 예약은 실행되지 않습니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.title}</div>
            <div className="text-xs text-neutral-500 mt-1">
              {SCHED_LABEL[delTarget.schedule_type] ?? delTarget.schedule_type} · 다음 실행 {formatDateTime(delTarget.next_run_at)}
            </div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
