import { useEffect, useState, useCallback } from 'react'
import { Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatKRW } from '../components/ui/format'
import { RefreshButton } from '../components/ui/FilterBar'
import CcuSection from './dashboard/CcuSection'

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

function StatSection({ title, description, children }: { title: string; description: string; children: React.ReactNode }) {
  return (
    <WhiteCard>
      <div className="border-b border-neutral-100 p-5 pb-3">
        <CardHeader title={title} description={description} />
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
  const [reloadKey, setReloadKey] = useState(0)

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
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
      <PageHeader
        title="대시보드"
        description="프로젝트 현황을 한눈에 봅니다."
        actions={<RefreshButton onClick={() => { void load(); setReloadKey((k) => k + 1) }} loading={loading} />}
      />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <CcuSection target={target} reloadKey={reloadKey} onUnauthenticated={onUnauthenticated} onError={setError} />

      {loading && !stats ? (
        <WhiteCard className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </WhiteCard>
      ) : stats && (
        <div className="space-y-4">
          <StatSection title="플레이어" description="가입하고 활동하는 플레이어 수를 봅니다.">
            <StatTile label="총 가입자" value={n(stats.total_players)} />
            <StatTile label="오늘 신규 가입" value={n(stats.new_today)} />
            <StatTile label="최근 7일 신규 가입" value={n(stats.new_7d)} />
            <StatTile label="오늘 활동" value={n(stats.active_today)} />
            <StatTile label="현재 차단 중" value={n(stats.banned_now)} />
          </StatSection>

          <StatSection title="매출" description="결제 건수와 금액을 봅니다. 환불·테스트·무료 결제는 뺍니다.">
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
