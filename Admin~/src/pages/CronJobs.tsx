import { Fragment, useCallback, useEffect, useState } from 'react'
import { RefreshCw, ChevronDown, ChevronRight } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { formatDateTime, formatCount } from '../components/ui/format'

type Job = {
  jobname: string
  schedule: string
  active: boolean
  last_start: string | null
  last_end: string | null
  last_status: string | null
  last_message: string | null
  last_success: string | null
  runs_24h: number
  failures_24h: number
  last_failure_message: string | null
}

// install.sql 이 등록하는 작업. 여기 없는 작업은 이름 그대로 보인다.
const JOB_LABELS: Record<string, string> = {
  'run-mail-schedules': '우편 예약·반복 발송',
  'deliver-open-mail-batches': '나중에 들어온 플레이어에게 우편 배달',
  'ts_leaderboard_rotate_due': '리더보드 회차 넘기기',
  'ts_chat_cleanup': '오래된 채팅 정리',
  'ts_match_lobby_cleanup': '끝난 매치 로비 정리',
  'ts_friend_request_cleanup': '오래 둔 친구 요청 취소',
  'cleanup-expired-mails': '만료된 우편 정리',
  'cleanup-user-data-logs': '7일 지난 데이터 로그 정리',
  'withdrawal-cleanup': '탈퇴 기한이 지난 계정 삭제',
  'withdrawal-overdue-check': '탈퇴 계정이 실제로 지워졌는지 확인',
  'cleanup-cron-run-details': '7일 지난 자동 작업 기록 정리',
  'purchase-refund-sync-google': '구글 환불 확인',
}

// pg_cron 일정은 UTC 다. 화면에는 한국 시간으로 보인다.
function scheduleInfo(s: string): { text: string; minutes: number | null } {
  const p = s.trim().split(/\s+/)
  if (p.length !== 5 || p[2] !== '*' || p[3] !== '*' || p[4] !== '*') return { text: s, minutes: null }
  const m = p[0] ?? ''
  const h = p[1] ?? ''
  if (m === '*' && h === '*') return { text: '매분', minutes: 1 }
  const every = /^\*\/(\d+)$/.exec(m)
  if (every && h === '*') return { text: `${every[1]}분마다`, minutes: Number(every[1]) }
  if (/^\d+$/.test(m) && h === '*') return { text: `매시 ${m}분`, minutes: 60 }
  if (/^\d+$/.test(m) && /^\d+$/.test(h)) {
    const kst = (Number(h) + 9) % 24
    return { text: `매일 ${String(kst).padStart(2, '0')}:${m.padStart(2, '0')}`, minutes: 1440 }
  }
  return { text: s, minutes: null }
}

type Health = { label: string; cls: string; bad: boolean }

function healthOf(j: Job, now: number): Health {
  if (!j.active) return { label: '꺼짐', cls: 'bg-neutral-200 text-neutral-600', bad: true }
  if (j.last_status === 'failed') return { label: '실패', cls: 'bg-red-100 text-red-700', bad: true }
  if (!j.last_start) return { label: '최근 기록 없음', cls: 'bg-amber-100 text-amber-700', bad: true }
  // 일정 간격의 두 배가 넘도록 안 돌았으면 작업이 멈춘 것이다. 실패 기록도 안 남아 가장 놓치기 쉽다.
  const { minutes } = scheduleInfo(j.schedule)
  if (minutes !== null && now - new Date(j.last_start).getTime() > (minutes * 2 + 2) * 60_000) {
    return { label: '멈춤', cls: 'bg-red-100 text-red-700', bad: true }
  }
  if (j.last_status === 'running' || j.last_status === 'starting') return { label: '도는 중', cls: 'bg-brand-soft text-[#1677ff]', bad: false }
  if (j.failures_24h > 0) return { label: '최근 실패 있음', cls: 'bg-amber-100 text-amber-700', bad: true }
  return { label: '정상', cls: 'bg-emerald-100 text-emerald-700', bad: false }
}

export default function CronJobs({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [jobs, setJobs] = useState<Job[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [error, setError] = useState('')
  const [loadedAt, setLoadedAt] = useState<number>(Date.now())
  const [open, setOpen] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    setError('')
    try {
      setJobs(await callAdmin<Job[]>(target, 'jobs.list'))
      setLoadedAt(Date.now())
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '자동 작업 상태를 불러오지 못했습니다.')
      setJobs([])
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  const rows = jobs.map((j) => ({ j, h: healthOf(j, loadedAt), s: scheduleInfo(j.schedule) }))
  const badCount = rows.filter((r) => r.h.bad).length

  return (
    <div className="space-y-4">
      <PageHeader title="자동 작업" description="서버가 정해진 시각마다 혼자 하는 일이 제때 돌고 있는지 봅니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="flex flex-wrap items-center gap-3">
        {!loading && !failed && (
          badCount > 0
            ? <span className="rounded-md bg-red-50 border border-red-200 px-3 py-1.5 text-sm text-red-700">확인이 필요한 작업 {formatCount(badCount)}개</span>
            : <span className="rounded-md bg-emerald-50 border border-emerald-200 px-3 py-1.5 text-sm text-emerald-700">모든 작업이 정상입니다</span>
        )}
        <span className="text-xs text-neutral-500">조회 {formatDateTime(new Date(loadedAt).toISOString())}</span>
        <Button variant="outline" size="compact" className="ml-auto" onClick={() => { void load() }} disabled={loading}>
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />새로고침
        </Button>
      </div>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="w-8" />
                <th className="text-left px-4 py-2.5 font-medium">작업</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">일정</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">마지막 실행</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">마지막 성공</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">24시간 실패</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={7} emptyText="등록된 자동 작업이 없습니다." />
              ) : (
                rows.map(({ j, h, s }) => {
                  const message = j.last_status === 'failed' ? j.last_message : j.last_failure_message
                  const expandable = !!message
                  const isOpen = open === j.jobname
                  return (
                    <Fragment key={j.jobname}>
                      <tr
                        className={`border-t border-neutral-100 ${expandable ? 'cursor-pointer hover:bg-neutral-50' : ''}`}
                        onClick={() => expandable && setOpen(isOpen ? null : j.jobname)}
                      >
                        <td className="pl-3 text-neutral-400">
                          {expandable && (isOpen ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />)}
                        </td>
                        <td className="px-4 py-3">
                          <div className="font-medium text-neutral-800">{JOB_LABELS[j.jobname] ?? j.jobname}</div>
                          {JOB_LABELS[j.jobname] && <div className="text-xs text-neutral-400 font-mono">{j.jobname}</div>}
                        </td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{s.text}</td>
                        <td className="px-4 py-3">
                          <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs whitespace-nowrap ${h.cls}`}>{h.label}</span>
                        </td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{j.last_start ? formatDateTime(j.last_start) : '-'}</td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{j.last_success ? formatDateTime(j.last_success) : '-'}</td>
                        <td className="px-4 py-3 whitespace-nowrap">
                          <span className={j.failures_24h > 0 ? 'text-red-600 font-medium' : 'text-neutral-500'}>
                            {formatCount(j.failures_24h)} / {formatCount(j.runs_24h)}회
                          </span>
                        </td>
                      </tr>
                      {isOpen && message && (
                        <tr className="border-t border-neutral-100 bg-neutral-50">
                          <td />
                          <td colSpan={6} className="px-4 py-3">
                            <div className="text-xs text-neutral-500 mb-1">{j.last_status === 'failed' ? '마지막 실행의 오류' : '최근 24시간 안의 마지막 오류'}</div>
                            <pre className="text-xs text-red-700 whitespace-pre-wrap break-all font-mono">{message}</pre>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>
    </div>
  )
}
