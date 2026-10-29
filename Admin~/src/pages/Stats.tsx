import { useCallback, useEffect, useState } from 'react'
import { RefreshCw, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { Tabs } from '../components/ui/Tabs'
import { formatKRW } from '../components/ui/format'
import { timeZoneName, useGameTimeZone } from '../lib/gameTime'
import { PLATFORMS, platformLabel } from '../lib/platforms'
import { Segmented } from '../components/ui/Segmented'
import { niceAxis, ValueAxisFrame } from '../components/ui/ValueAxis'

type Day = { day: string; total: number; new: number; returning: number; comeback: number }
type Cohort = { day: string; size: number; kept: (number | null)[] }
type ActivityStats = {
  today: string
  daily: Day[]
  offsets: number[]
  retention: Cohort[]
  first_day: string | null
  signup_platforms?: string[]
}

const RANGES = [
  { days: 30, label: '30일' },
  { days: 90, label: '90일' },
  { days: 365, label: '1년' },
] as const

/**
 * 가입 플랫폼 거르기 버튼. '' = 전체. 서버가 알려 준, 이 기간에 기록이 있는 플랫폼만 보인다 — 지금 고른 값은 기간을 바꿔
 * 기록이 없어져도 남긴다(고른 버튼이 사라지지 않게). 순서는 우편 발송과 같은 PLATFORMS 순, 모르는 값은 그 뒤.
 */
function signupPlatformOptions(present: string[] | undefined, selected: string) {
  const values = new Set(present ?? PLATFORMS.map((p) => p.v as string))
  if (selected) values.add(selected)
  const known = PLATFORMS.map((p) => p.v as string).filter((v) => values.has(v))
  const rest = [...values].filter((v) => !known.includes(v)).sort((a, b) => (a === 'unknown' ? 1 : b === 'unknown' ? -1 : a.localeCompare(b)))
  return [
    { v: '', label: '전체' },
    ...[...known, ...rest].map((v) => ({ v, label: v === 'unknown' ? '알 수 없음' : platformLabel(v) })),
  ]
}

type Tab = 'users' | 'comeback' | 'retention' | 'revenue' | 'chat'
// 접속 기록으로 세는 탭 — 가입 플랫폼으로 거를 수 있다.
const ACTIVITY_TABS: Tab[] = ['users', 'comeback', 'retention']
const TABS: [Tab, string][] = [
  ['users', '일일 사용자'],
  ['comeback', '복귀 유저'],
  ['retention', '리텐션'],
  ['revenue', '국가별 매출'],
  ['chat', '일일 채팅'],
]

type ChatDay = { day: string; total: number; by_kind: Record<string, number> }
type ChatStats = { daily: ChatDay[]; first_day: string | null }
// 채널 종류 — 서버의 chat_channels.kind 값. 앞에 둔 것이 막대 아래에 쌓인다.
const CHAT_KINDS: { key: string; label: string; color: string }[] = [
  { key: 'global', label: '전체 채널', color: '#1677ff' },
  { key: 'server', label: '서버 채널', color: '#60a5fa' },
  { key: 'group', label: '로비 대화', color: '#f59e0b' },
  { key: 'direct', label: '1:1', color: '#94a3b8' },
]

function ChatTab({ target, days, reloadKey, onUnauthenticated, onError }: {
  target: ProjectTarget; days: number; reloadKey: number
  onUnauthenticated: () => void; onError: (m: string) => void
}) {
  const [data, setData] = useState<ChatStats | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let alive = true
    setLoading(true)
    callAdmin<ChatStats>(target, 'chat.stats', { days })
      .then((r) => { if (alive) setData(r) })
      .catch((e: unknown) => {
        if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
        onError(e instanceof Error ? e.message : '채팅 통계를 불러오지 못했습니다.')
      })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [target, days, reloadKey, onUnauthenticated, onError])

  if (loading && !data) {
    return <WhiteCard className="p-10 text-center"><Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" /></WhiteCard>
  }
  if (!data) return null
  // 막대는 종류별로 쌓는다 — 날짜 행에 종류별 숫자를 펼쳐 둔다.
  const rows: DayRow[] = data.daily.map((d) => ({ day: d.day, ...Object.fromEntries(CHAT_KINDS.map((k) => [k.key, d.by_kind[k.key] ?? 0])) }))
  const today = data.daily[data.daily.length - 1]
  const sum = data.daily.reduce((s, d) => s + d.total, 0)

  return (
    <>
      {data.first_day && data.daily[0] && data.first_day > data.daily[0].day && (
        <div className="text-xs text-neutral-500">기록은 {data.first_day}부터 있습니다. 그 전 날짜는 0으로 보입니다.</div>
      )}
      <WhiteCard>
        <div className="border-b border-neutral-100 px-5 py-3 flex flex-wrap items-center justify-between gap-2">
          <span className="text-sm font-medium text-neutral-800">일일 채팅 개수</span>
          <span className="text-xs text-neutral-500">
            {today && <>오늘 {n(today.total)}개 · </>}기간 합계 {n(sum)}개 · 운영자 공지 제외
          </span>
        </div>
        <div className="p-5">
          <DayBars days={rows} series={CHAT_KINDS} unit="개" />
        </div>
      </WhiteCard>
    </>
  )
}

type CountryRow = { country: string; revenue_krw: number; purchases: number; payers: number }
type RevenueStats = { days: number; total_krw: number; purchases: number; payers: number; unknown_amount?: number; countries: CountryRow[] }

const regionNames = (() => {
  try { return new Intl.DisplayNames(['ko'], { type: 'region' }) } catch { return null }
})()
const countryName = (code: string) =>
  code === 'unknown' ? '알 수 없음' : regionNames?.of(code) ?? code

function RevenueTab({ target, days, reloadKey, onUnauthenticated, onError }: {
  target: ProjectTarget; days: number; reloadKey: number
  onUnauthenticated: () => void; onError: (m: string) => void
}) {
  const [data, setData] = useState<RevenueStats | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let alive = true
    setLoading(true)
    callAdmin<RevenueStats>(target, 'revenue.stats', { days })
      .then((r) => { if (alive) setData(r) })
      .catch((e: unknown) => {
        if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
        onError(e instanceof Error ? e.message : '매출을 불러오지 못했습니다.')
      })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [target, days, reloadKey, onUnauthenticated, onError])

  if (loading && !data) {
    return <WhiteCard className="p-10 text-center"><Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" /></WhiteCard>
  }
  if (!data) return null
  const max = Math.max(1, ...data.countries.map((c) => c.revenue_krw))

  return (
    <WhiteCard>
      <div className="border-b border-neutral-100 px-5 py-3 flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-medium text-neutral-800">국가별 매출</span>
        <span className="text-xs text-neutral-500">
          합계 {formatKRW(data.total_krw)} · {n(data.purchases)}건 · 결제자 {n(data.payers)}명 · 가입 국가 기준, 환불 제외
        </span>
      </div>
      {!!data.unknown_amount && (
        <p className="border-b border-neutral-100 px-5 py-2 text-xs text-amber-700">
          금액을 모르는 결제 {n(data.unknown_amount)}건은 합계에 들어가지 않았습니다. 채우는 상태는 구매 내역 화면 위쪽에 보입니다.
        </p>
      )}
      {data.countries.length === 0 ? (
        <div className="p-8 text-center text-sm text-neutral-400">이 기간에 결제가 없습니다.</div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-xs text-neutral-500 border-b border-neutral-100">
                <th className="px-5 py-2 text-left font-medium">국가</th>
                <th className="px-3 py-2 text-left font-medium w-2/5">매출</th>
                <th className="px-3 py-2 text-right font-medium whitespace-nowrap">금액</th>
                <th className="px-3 py-2 text-right font-medium whitespace-nowrap">비율</th>
                <th className="px-3 py-2 text-right font-medium whitespace-nowrap">건수</th>
                <th className="px-5 py-2 text-right font-medium whitespace-nowrap">결제자</th>
              </tr>
            </thead>
            <tbody>
              {data.countries.map((c) => (
                <tr key={c.country} className="border-b border-neutral-100 last:border-0">
                  <td className="px-5 py-2 whitespace-nowrap text-neutral-800">{countryName(c.country)}</td>
                  <td className="px-3 py-2">
                    <div className="h-2.5 rounded-full bg-[#1677ff]" style={{ width: `${Math.max(1, (c.revenue_krw / max) * 100)}%` }} />
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums whitespace-nowrap font-medium">{formatKRW(c.revenue_krw)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-neutral-500">
                    {data.total_krw > 0 ? `${((c.revenue_krw / data.total_krw) * 100).toFixed(1)}%` : '-'}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">{n(c.purchases)}</td>
                  <td className="px-5 py-2 text-right tabular-nums">{n(c.payers)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </WhiteCard>
  )
}

const NEW_COLOR = '#1677ff'
const RETURNING_COLOR = '#94a3b8'
const COMEBACK_COLOR = '#f59e0b'

const n = (v: number) => v.toLocaleString('ko-KR')
const md = (iso: string) => `${Number(iso.slice(5, 7))}/${Number(iso.slice(8, 10))}`

const W = 600
const H = 150

type DayRow = { day: string } & Record<string, number | string>

/** 날짜별 막대. 계열을 아래부터 쌓는다(신규 + 재방문 = 전체). */
function DayBars<R extends DayRow>({ days, series, unit = '명' }: { days: R[]; series: { key: keyof R & string; color: string; label: string }[]; unit?: string }) {
  const [hover, setHover] = useState<number | null>(null)
  const max = Math.max(1, ...days.map((d) => series.reduce((s, x) => s + (d[x.key] as number), 0)))
  const axis = niceAxis(max)
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
        ) : <span>최대 {n(max)}{unit}</span>}
      </div>
      <ValueAxisFrame axis={axis} heightCls="h-36" footer={
        <div className="flex justify-between text-[11px] text-neutral-400 mt-1">
          <span>{days[0] ? md(days[0].day) : ''}</span>
          <span>{days.length ? md(days[days.length - 1]!.day) : ''}</span>
        </div>
      }>
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full h-full block" onMouseLeave={() => setHover(null)}>
          <line x1={0} y1={H - 0.5} x2={W} y2={H - 0.5} className="stroke-neutral-200" strokeWidth={1} vectorEffect="non-scaling-stroke" />
          {days.map((d, i) => {
            let y = H
            const x = i * slot + (slot - bar) / 2
            return (
              <g key={d.day} onMouseEnter={() => setHover(i)}>
                <rect x={i * slot} y={0} width={slot} height={H} fill="transparent" />
                {series.map((s) => {
                  const h = ((d[s.key] as number) / axis.top) * H
                  y -= h
                  return <rect key={s.label} x={x} y={y} width={bar} height={h} fill={s.color} opacity={hover === null || hover === i ? 1 : 0.55} />
                })}
              </g>
            )
          })}
        </svg>
      </ValueAxisFrame>
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
  const [signupPlatform, setSignupPlatform] = useState('')
  const [reloadKey, setReloadKey] = useState(0)
  const [data, setData] = useState<ActivityStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await callAdmin<ActivityStats>(target, 'activity.stats', { days, signupPlatform }))
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '통계를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, days, signupPlatform, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  const today = data?.daily[data.daily.length - 1]
  const recent = data ? [...data.daily].reverse() : []
  const tz = useGameTimeZone(target)

  return (
    <div className="space-y-4">
      <PageHeader
        title="통계"
        description={`게임 운영 통계를 봅니다.${tz ? ` 날짜는 ${timeZoneName(tz)} 기준입니다.` : ''}`}
        actions={
          <Button variant="outline" size="compact" onClick={() => { void load(); setReloadKey((k) => k + 1) }} disabled={loading}>
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            새로고침
          </Button>
        }
      />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <Tabs value={tab} onChange={setTab} tabs={TABS} />

      {/* 거르기는 이름표 + 같은 모양으로 나란히 둔다 — 모양이 다르면 어느 쪽이 기간인지 헷갈린다(2026-10-29 사용자 지적). */}
      {/* 이름표는 버튼 묶음 위에 둔다(우편 발송의 입력 이름과 같은 꼴) — 옆에 두면 선택 안 된 버튼처럼 보인다. */}
      <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
        <div>
          <div className="text-xs font-medium text-neutral-500 mb-1">기간</div>
          <Segmented value={String(days)} onChange={(v) => setDays(Number(v))}
            options={RANGES.map((r) => ({ v: String(r.days), label: r.label }))} />
        </div>
        {ACTIVITY_TABS.includes(tab) && (
          <div>
            <div className="text-xs font-medium text-neutral-500 mb-1">가입 플랫폼</div>
            <Segmented value={signupPlatform} onChange={setSignupPlatform} options={signupPlatformOptions(data?.signup_platforms, signupPlatform)} />
          </div>
        )}
      </div>

      {loading && !data ? (
        <WhiteCard className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </WhiteCard>
      ) : data && (
        <>
          {/* 접속 기록은 통계를 만든 날부터 쌓였다. 매출은 결제 기록이 원래 있어 이 안내가 맞지 않는다. */}
          {tab !== 'revenue' && tab !== 'chat' && data.first_day && data.first_day > data.daily[0]!.day && (
            <div className="text-xs text-neutral-500">기록은 {data.first_day}부터 있습니다. 그 전 날짜는 0으로 보입니다.</div>
          )}

          {tab === 'revenue' && (
            <RevenueTab target={target} days={days} reloadKey={reloadKey} onUnauthenticated={onUnauthenticated} onError={setError} />
          )}

          {tab === 'chat' && (
            <ChatTab target={target} days={days} reloadKey={reloadKey} onUnauthenticated={onUnauthenticated} onError={setError} />
          )}

          {tab === 'users' && (<>
          <WhiteCard>
            <div className="border-b border-neutral-100 px-5 py-3 flex items-center justify-between">
              <span className="text-sm font-medium text-neutral-800">일일 사용자</span>
              {today && <span className="text-xs text-neutral-500">오늘 {n(today.total)}명 · 신규 {n(today.new)} · 재방문 {n(today.returning)}</span>}
            </div>
            <div className="p-5">
              {/* 앞에 둔 계열이 막대 아래에 쌓인다 — 재방문을 바닥에, 신규를 위에. */}
              <DayBars days={data.daily} series={[
                { key: 'returning', color: RETURNING_COLOR, label: '재방문' },
                { key: 'new', color: NEW_COLOR, label: '신규' },
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
