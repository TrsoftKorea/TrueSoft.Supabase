import { useCallback, useEffect, useState } from 'react'
import { RefreshCw, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'

type Day = { day: string; total: number; new: number; returning: number; comeback: number }
type Cohort = { day: string; size: number; kept: (number | null)[] }
type ActivityStats = {
  today: string
  daily: Day[]
  offsets: number[]
  retention: Cohort[]
  first_day: string | null
}

const RANGES = [
  { days: 30, label: '30일' },
  { days: 90, label: '90일' },
  { days: 365, label: '1년' },
] as const

type Tab = 'users' | 'comeback' | 'retention'
// 매출·채팅 통계가 생기면 여기에 탭을 더한다.
const TABS: [Tab, string][] = [
  ['users', '일일 사용자'],
  ['comeback', '복귀 유저'],
  ['retention', '리텐션'],
]

const NEW_COLOR = '#1677ff'
const RETURNING_COLOR = '#94a3b8'
const COMEBACK_COLOR = '#f59e0b'

const n = (v: number) => v.toLocaleString('ko-KR')
const md = (iso: string) => `${Number(iso.slice(5, 7))}/${Number(iso.slice(8, 10))}`

const W = 600
const H = 150

/** 날짜별 막대. 계열을 아래부터 쌓는다(신규 + 재방문 = 전체). */
function DayBars({ days, series }: { days: Day[]; series: { key: keyof Day; color: string; label: string }[] }) {
  const [hover, setHover] = useState<number | null>(null)
  const max = Math.max(1, ...days.map((d) => series.reduce((s, x) => s + (d[x.key] as number), 0)))
  const slot = W / Math.max(1, days.length)
  const bar = Math.max(1, slot * 0.7)
  const hd = hover !== null ? days[hover] : undefined

  return (
    <div>
      <div className="flex flex-wrap items-baseline justify-between gap-2 text-xs text-neutral-500 min-h-5">
        <span className="flex gap-3">
          {series.map((s) => (
            <span key={s.label} className="inline-flex items-center gap-1 whitespace-nowrap">
              <span className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: s.color }} />{s.label}
            </span>
          ))}
        </span>
        {hd ? (
          <span className="text-neutral-800 whitespace-nowrap">
            {md(hd.day)} · {series.map((s) => `${s.label} ${n(hd[s.key] as number)}`).join(' · ')}
          </span>
        ) : <span>최대 {n(max)}명</span>}
      </div>
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full h-36" onMouseLeave={() => setHover(null)}>
        <line x1={0} y1={H - 0.5} x2={W} y2={H - 0.5} className="stroke-neutral-200" strokeWidth={1} vectorEffect="non-scaling-stroke" />
        {days.map((d, i) => {
          let y = H
          const x = i * slot + (slot - bar) / 2
          return (
            <g key={d.day} onMouseEnter={() => setHover(i)}>
              <rect x={i * slot} y={0} width={slot} height={H} fill="transparent" />
              {series.map((s) => {
                const h = ((d[s.key] as number) / max) * (H - 6)
                y -= h
                return <rect key={s.label} x={x} y={y} width={bar} height={h} fill={s.color} opacity={hover === null || hover === i ? 1 : 0.55} />
              })}
            </g>
          )
        })}
      </svg>
      <div className="flex justify-between text-[11px] text-neutral-400 mt-1">
        <span>{days[0] ? md(days[0].day) : ''}</span>
        <span>{days.length ? md(days[days.length - 1]!.day) : ''}</span>
      </div>
    </div>
  )
}

/** 비율이 높을수록 진한 파랑. 아직 오지 않은 날은 빈칸. */
function RetentionCell({ kept, size }: { kept: number | null; size: number }) {
  if (kept === null) return <td className="px-2 py-1.5 text-center text-neutral-300">·</td>
  const pct = size > 0 ? (kept / size) * 100 : 0
  const alpha = Math.min(0.85, pct / 100 + 0.05)
  return (
    <td className="px-2 py-1.5 text-center tabular-nums whitespace-nowrap" title={`${n(kept)} / ${n(size)}명`}
      style={{ background: `rgba(22, 119, 255, ${alpha})`, color: pct >= 50 ? '#fff' : undefined }}>
      {pct.toFixed(0)}%
    </td>
  )
}

function DailyTable({ days }: { days: Day[] }) {
  return (
    <WhiteCard>
      <div className="border-b border-neutral-100 px-5 py-3">
        <span className="text-sm font-medium text-neutral-800">날짜별 숫자</span>
      </div>
      <div className="overflow-x-auto max-h-96">
        <table className="w-full text-sm">
          <thead className="sticky top-0 bg-surface">
            <tr className="text-xs text-neutral-500 border-b border-neutral-100">
              <th className="px-5 py-2 text-left font-medium">날짜</th>
              <th className="px-3 py-2 text-right font-medium">전체</th>
              <th className="px-3 py-2 text-right font-medium">신규</th>
              <th className="px-3 py-2 text-right font-medium">재방문</th>
              <th className="px-5 py-2 text-right font-medium">복귀</th>
            </tr>
          </thead>
          <tbody>
            {days.map((d) => (
              <tr key={d.day} className="border-b border-neutral-100 last:border-0">
                <td className="px-5 py-1.5 whitespace-nowrap text-neutral-700">{d.day}</td>
                <td className="px-3 py-1.5 text-right tabular-nums font-medium">{n(d.total)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{n(d.new)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{n(d.returning)}</td>
                <td className="px-5 py-1.5 text-right tabular-nums">{n(d.comeback)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </WhiteCard>
  )
}

export default function Stats({ target, onUnauthenticated }: { target: ProjectTarget; onUnauthenticated: () => void }) {
  const [tab, setTab] = useState<Tab>('users')
  const [days, setDays] = useState<number>(30)
  const [data, setData] = useState<ActivityStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await callAdmin<ActivityStats>(target, 'activity.stats', { days }))
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '통계를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, days, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  const today = data?.daily[data.daily.length - 1]
  const recent = data ? [...data.daily].reverse() : []

  return (
    <div className="space-y-4">
      <PageHeader title="통계" description="날짜별 접속자, 복귀 유저, 가입일별 리텐션을 봅니다. 날짜는 한국 시간 기준입니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="border-b border-neutral-200 flex gap-1 overflow-x-auto">
        {TABS.map(([key, label]) => (
          <button
            key={key}
            type="button"
            onClick={() => setTab(key)}
            className={[
              'h-10 px-4 text-sm border-b-2 -mb-px transition-colors whitespace-nowrap',
              tab === key
                ? 'border-[#1677ff] text-[#1677ff] font-medium'
                : 'border-transparent text-neutral-600 hover:text-neutral-900',
            ].join(' ')}
          >
            {label}
          </button>
        ))}
      </div>

      <div className="flex items-center justify-between gap-2">
        <div className="flex gap-1">
          {RANGES.map((r) => (
            <button key={r.days} type="button" onClick={() => setDays(r.days)}
              className={`whitespace-nowrap rounded px-2.5 py-1 text-xs ${days === r.days ? 'bg-brand-soft text-[#1677ff] font-medium' : 'text-neutral-500 hover:text-neutral-800'}`}>
              {r.label}
            </button>
          ))}
        </div>
        <Button variant="outline" size="compact" onClick={() => void load()} disabled={loading}>
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          새로고침
        </Button>
      </div>

      {loading && !data ? (
        <WhiteCard className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </WhiteCard>
      ) : data && (
        <>
          {data.first_day && data.first_day > data.daily[0]!.day && (
            <div className="text-xs text-neutral-500">기록은 {data.first_day}부터 있습니다. 그 전 날짜는 0으로 보입니다.</div>
          )}

          {tab === 'users' && (<>
          <WhiteCard>
            <div className="border-b border-neutral-100 px-5 py-3 flex items-center justify-between">
              <span className="text-sm font-medium text-neutral-800">일일 사용자</span>
              {today && <span className="text-xs text-neutral-500">오늘 {n(today.total)}명 · 신규 {n(today.new)} · 재방문 {n(today.returning)}</span>}
            </div>
            <div className="p-5">
              <DayBars days={data.daily} series={[
                { key: 'new', color: NEW_COLOR, label: '신규' },
                { key: 'returning', color: RETURNING_COLOR, label: '재방문' },
              ]} />
            </div>
          </WhiteCard>
          <DailyTable days={recent} />
          </>)}

          {tab === 'comeback' && (
          <WhiteCard>
            <div className="border-b border-neutral-100 px-5 py-3 flex items-center justify-between">
              <span className="text-sm font-medium text-neutral-800">복귀 유저</span>
              <span className="text-xs text-neutral-500">직전 14일 동안 접속하지 않았다가 들어온 사람</span>
            </div>
            <div className="p-5">
              <DayBars days={data.daily} series={[{ key: 'comeback', color: COMEBACK_COLOR, label: '복귀' }]} />
            </div>
          </WhiteCard>
          )}

          {tab === 'retention' && (
          <WhiteCard>
            <div className="border-b border-neutral-100 px-5 py-3 flex items-center justify-between">
              <span className="text-sm font-medium text-neutral-800">리텐션</span>
              <span className="text-xs text-neutral-500">가입한 날로부터 N일째에 다시 접속한 비율 · 칸에 마우스를 올리면 인원</span>
            </div>
            {data.retention.length === 0 ? (
              <div className="p-8 text-center text-sm text-neutral-400">이 기간에 가입한 플레이어가 없습니다.</div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-neutral-500 border-b border-neutral-100">
                      <th className="px-3 py-2 text-left font-medium whitespace-nowrap">가입일</th>
                      <th className="px-2 py-2 text-right font-medium whitespace-nowrap">가입</th>
                      {data.offsets.map((o) => <th key={o} className="px-2 py-2 font-medium whitespace-nowrap">D{o}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {data.retention.map((c) => (
                      <tr key={c.day} className="border-b border-neutral-100 last:border-0">
                        <td className="px-3 py-1.5 whitespace-nowrap text-neutral-700">{c.day}</td>
                        <td className="px-2 py-1.5 text-right tabular-nums">{n(c.size)}</td>
                        {c.kept.map((k, i) => <RetentionCell key={i} kept={k} size={c.size} />)}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </WhiteCard>
          )}
        </>
      )}
    </div>
  )
}
