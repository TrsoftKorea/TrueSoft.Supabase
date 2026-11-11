import { useCallback, useEffect, useState } from 'react'
import { Trash2, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { serverTargetLabel, useServerName } from '../lib/serverNames'
import { InfoList } from '../components/ui/InfoList'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableCard } from '../components/ui/DataTable'
import { Switch } from '../components/ui/Switch'
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
  created_by: string | null; server_code: string | null; open_for_new: boolean
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
      <PageHeader title="예약 목록" description="예약·반복 발송을 끄면 다시 켤 때까지 보내지 않습니다." actions={<RefreshButton onClick={() => void load()} loading={loading} />} />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <TableCard
        columns={[
          {
            // 켜고 끄는 버튼을 따로 두지 않고 상태 칸을 스위치로 — 버튼 칸이 줄어 1280px 화면에서도 표가 옆으로 안 넘친다.
            key: 'active', header: '사용', kind: 'status', cell: (r) => (
              <span className="inline-flex items-center gap-1.5">
                <Switch label={`${r.title} 사용`} value={r.is_active} disabled={togglingId !== null} onChange={() => void toggleActive(r)} />
                {togglingId === r.id && <Loader2 className="w-3 h-3 animate-spin text-neutral-400" />}
              </span>
            ),
          },
          {
            // 주 내용 한 줄 + 조건은 작은 글씨로 아래 — 한 줄에 이으면 칸이 쓸데없이 넓어진다.
            // 반복 규칙("매일 …")이 아래에 붙으면 반복 발송이고 없으면 한 번 보내는 예약이라 종류 칸을 따로 두지 않는다.
            key: 'next', header: '다음 발송', kind: 'datetime', cell: (r) => (
              <>
                {formatDateTime(r.next_run_at)}
                {r.schedule_type === 'repeat' && <div className="text-xs text-neutral-500">{repeatText(r)}</div>}
              </>
            ),
          },
          {
            key: 'target', header: '대상', kind: 'name', cell: (r) => (
              <>
                {r.server_code ? serverTargetLabel(serverName(r.server_code)) : MODE_LABEL[r.target_mode] ?? r.target_mode}
                {r.open_for_new && r.schedule_type !== 'repeat' && <div className="text-xs text-neutral-500">나중에 들어온 플레이어도</div>}
              </>
            ),
          },
          {
            // 아이템은 제목 아래 작은 글씨로 — 긴 글 칸 둘을 나란히 두면 넓은 화면에서도 표가 옆으로 넘친다.
            key: 'title', header: '제목', kind: 'text',
            full: (r) => (r.items?.length ? `${r.title} · ${itemsText(r.items, catalog)}` : r.title),
            cell: (r) => (
              <>
                <div className="truncate">{r.title}</div>
                {!!r.items?.length && <div className="truncate text-xs text-neutral-500">{itemsText(r.items, catalog)}</div>}
              </>
            ),
          },
          { key: 'by', header: '운영자', kind: 'name', cell: (r) => r.created_by },
          {
            key: 'actions', header: '', kind: 'actions', cell: (r) => (
              <Button size="sm" variant="dangerOutline" onClick={() => { setDelError(''); setDelTarget(r) }}>
                <Trash2 className="w-3 h-3" />삭제
              </Button>
            ),
          },
        ]}
        rows={rows}
        rowKey={(r) => r.id}
        loading={loading}
        failed={failed}
        emptyText="예약이 없습니다."
      />

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
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-2">
            <div className="text-neutral-800">{delTarget.title}</div>
            <InfoList items={[
              { label: '발송 방식', value: delTarget.schedule_type === 'repeat' ? `반복 · ${repeatText(delTarget)}` : SCHED_LABEL[delTarget.schedule_type] ?? delTarget.schedule_type },
              { label: '다음 실행', value: formatDateTime(delTarget.next_run_at) },
            ]} />
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
