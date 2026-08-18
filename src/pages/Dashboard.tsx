import { useEffect, useState, useCallback } from 'react'
import { RefreshCw, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatKRW } from '../components/ui/format'

type Stats = {
  total_players: number
  new_today: number
  new_7d: number
  active_today: number
  banned_now: number
  purchases_today: number
  revenue_today_krw: number
  revenue_7d_krw: number
  revenue_total_krw: number
}

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="p-5">
      <div className="text-xs text-neutral-500">{label}</div>
      <div className="mt-1.5 text-2xl font-semibold text-neutral-900">{value}</div>
    </div>
  )
}

function StatSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <WhiteCard>
      <div className="border-b border-neutral-100 px-5 py-3">
        <span className="text-sm font-medium text-neutral-800">{title}</span>
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-3 divide-x divide-y sm:divide-y-0 divide-neutral-100">
        {children}
      </div>
    </WhiteCard>
  )
}

const n = (v: number) => v.toLocaleString('ko-KR')

export default function Dashboard({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [stats, setStats] = useState<Stats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setStats(await callAdmin<Stats>(target, 'dashboard.stats'))
    } catch (e: unknown) {
      report(e, '지표를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  return (
    <div className="space-y-4">
      <PageHeader title="대시보드" description="플레이어와 매출 현황을 한눈에 봅니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="flex justify-end">
        <button
          onClick={() => void load()}
          className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          새로고침
        </button>
      </div>

      {loading && !stats ? (
        <WhiteCard className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </WhiteCard>
      ) : stats && (
        <div className="space-y-4">
          <StatSection title="플레이어">
            <StatTile label="총 가입자" value={n(stats.total_players)} />
            <StatTile label="오늘 신규 가입" value={n(stats.new_today)} />
            <StatTile label="최근 7일 신규 가입" value={n(stats.new_7d)} />
            <StatTile label="오늘 활동" value={n(stats.active_today)} />
            <StatTile label="현재 차단 중" value={n(stats.banned_now)} />
          </StatSection>

          <StatSection title="매출">
            <StatTile label="오늘 결제 건수" value={n(stats.purchases_today)} />
            <StatTile label="오늘 결제 금액" value={formatKRW(stats.revenue_today_krw)} />
            <StatTile label="최근 7일 결제 금액" value={formatKRW(stats.revenue_7d_krw)} />
            <StatTile label="누적 결제 금액" value={formatKRW(stats.revenue_total_krw)} />
          </StatSection>
        </div>
      )}
    </div>
  )
}
