import { useCallback, useEffect, useMemo, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { platformLabel } from '../../lib/platforms'
import { WhiteCard } from '../../components/ui/Card'

type Breakdown = Record<string, number>
type Point = { at: string; total: number }
type Ccu = {
  now: { total: number; by_server: Breakdown; by_platform: Breakdown }
  bucket_minutes: number
  series: Point[]
  peak: Point | null
  first_at: string | null
}

const RANGES = [
  { hours: 24, label: '24시간' },
  { hours: 24 * 7, label: '7일' },
  { hours: 24 * 30, label: '30일' },
  { hours: 24 * 365, label: '1년' },
] as const

const n = (v: number) => v.toLocaleString('ko-KR')

function formatAt(iso: string, withDate: boolean): string {
  const d = new Date(iso)
  const hm = d.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false })
  return withDate ? `${d.getMonth() + 1}/${d.getDate()} ${hm}` : hm
}

function sortedEntries(b: Breakdown): [string, number][] {
  return Object.entries(b).sort((x, y) => y[1] - x[1])
}

const W = 600
const H = 160

function Chart({ series, hours }: { series: Point[]; hours: number }) {
  const [hover, setHover] = useState<number | null>(null)
  const withDate = hours > 24

  const { path, area, pts, max } = useMemo(() => {
    const t0 = Date.now() - hours * 3600_000
    const t1 = Date.now()
    const max = Math.max(1, ...series.map((p) => p.total))
    const pts = series.map((p) => ({
      x: ((new Date(p.at).getTime() - t0) / (t1 - t0)) * W,
      y: H - (p.total / max) * (H - 8),
      p,
    }))
    const path = pts.map((q, i) => `${i === 0 ? 'M' : 'L'}${q.x.toFixed(1)},${q.y.toFixed(1)}`).join(' ')
    const first = pts[0]
    const last = pts[pts.length - 1]
    const area = first && last ? `${path} L${last.x.toFixed(1)},${H} L${first.x.toFixed(1)},${H} Z` : ''
    return { path, area, pts, max }
  }, [series, hours])

  const onMove = (e: React.MouseEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const x = ((e.clientX - rect.left) / rect.width) * W
    let best: number | null = null
    pts.forEach((q, i) => {
      const cur = best === null ? undefined : pts[best]
      if (!cur || Math.abs(q.x - x) < Math.abs(cur.x - x)) best = i
    })
    setHover(best)
  }

  const hq = hover !== null ? pts[hover] : undefined
  const hp = hq?.p

  return (
    <div>
      <div className="flex items-baseline justify-between text-xs text-neutral-500 h-5">
        <span>최대 {n(max)}명</span>
        {hp && <span className="text-neutral-800">{formatAt(hp.at, withDate)} · <b>{n(hp.total)}명</b></span>}
      </div>
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full h-40 cursor-crosshair"
        onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
        <line x1={0} y1={H - 0.5} x2={W} y2={H - 0.5} className="stroke-neutral-200" strokeWidth={1} vectorEffect="non-scaling-stroke" />
        {area && <path d={area} fill="#1677ff" fillOpacity={0.12} />}
        {path && <path d={path} fill="none" stroke="#1677ff" strokeWidth={1.5} vectorEffect="non-scaling-stroke" />}
        {hq && (
          <line x1={hq.x} y1={0} x2={hq.x} y2={H} className="stroke-neutral-400" strokeWidth={1}
            strokeDasharray="3 3" vectorEffect="non-scaling-stroke" />
        )}
      </svg>
      <div className="flex justify-between text-[11px] text-neutral-400 mt-1">
        <span>{formatAt(new Date(Date.now() - hours * 3600_000).toISOString(), withDate)}</span>
        <span>지금</span>
      </div>
    </div>
  )
}

function BreakdownList({ title, entries, label }: { title: string; entries: [string, number][]; label: (k: string) => string }) {
  return (
    <div>
      <div className="text-xs text-neutral-500 mb-1.5">{title}</div>
      {entries.length === 0 ? (
        <div className="text-sm text-neutral-400">-</div>
      ) : (
        <ul className="space-y-1">
          {entries.map(([k, v]) => (
            <li key={k} className="flex justify-between gap-4 text-sm">
              <span className="text-neutral-700 whitespace-nowrap">{label(k)}</span>
              <span className="font-medium text-neutral-900 tabular-nums">{n(v)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export default function CcuSection({
  target,
  reloadKey,
  onUnauthenticated,
  onError,
}: {
  target: ProjectTarget
  reloadKey: number
  onUnauthenticated: () => void
  onError: (message: string) => void
}) {
  const [hours, setHours] = useState<number>(24)
  const [data, setData] = useState<Ccu | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setData(await callAdmin<Ccu>(target, 'ccu.stats', { hours }))
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      onError(e instanceof Error ? e.message : '동시접속자를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, hours, onUnauthenticated, onError])

  useEffect(() => { void load() }, [load, reloadKey])

  const unknownLabel = (k: string) => (k === 'unknown' ? '알 수 없음' : k)

  return (
    <WhiteCard>
      <div className="flex items-center justify-between border-b border-neutral-100 px-5 py-3">
        <span className="text-sm font-medium text-neutral-800">동시접속자</span>
        <div className="flex gap-1">
          {RANGES.map((r) => (
            <button key={r.hours} type="button" onClick={() => setHours(r.hours)}
              className={`whitespace-nowrap rounded px-2 py-0.5 text-xs ${hours === r.hours ? 'bg-brand-soft text-[#1677ff] font-medium' : 'text-neutral-500 hover:text-neutral-800'}`}>
              {r.label}
            </button>
          ))}
        </div>
      </div>

      {loading && !data ? (
        <div className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </div>
      ) : data && (
        <div className="grid gap-5 p-5 lg:grid-cols-[1fr_220px]">
          <div className="min-w-0">
            {data.series.length === 0 ? (
              <div className="h-40 flex items-center justify-center text-sm text-neutral-400">
                아직 기록이 없습니다. 5분마다 쌓입니다.
              </div>
            ) : (
              <Chart series={data.series} hours={hours} />
            )}
            {data.first_at && new Date(data.first_at).getTime() > Date.now() - hours * 3600_000 && (
              <div className="mt-2 text-xs text-neutral-400">기록은 {formatAt(data.first_at, true)}부터 있습니다.</div>
            )}
          </div>

          <div className="space-y-4">
            <div>
              <div className="text-xs text-neutral-500">지금 접속 중</div>
              <div className="mt-1 text-2xl font-semibold text-neutral-900">{n(data.now.total)}명</div>
            </div>
            {data.peak && (
              <div>
                <div className="text-xs text-neutral-500">기간 최고</div>
                <div className="mt-1 text-sm text-neutral-900">
                  <b>{n(data.peak.total)}명</b>
                  <span className="text-neutral-500"> · {formatAt(data.peak.at, true)}</span>
                </div>
              </div>
            )}
            <BreakdownList title="서버별" entries={sortedEntries(data.now.by_server)} label={unknownLabel} />
            <BreakdownList title="플랫폼별" entries={sortedEntries(data.now.by_platform)}
              label={(k) => (k === 'unknown' ? '알 수 없음' : platformLabel(k))} />
          </div>
        </div>
      )}
    </WhiteCard>
  )
}
