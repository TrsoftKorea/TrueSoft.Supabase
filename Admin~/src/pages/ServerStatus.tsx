import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ChevronDown, ChevronUp, Loader2, RefreshCw } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { Segmented } from '../components/ui/Segmented'
import { Tabs } from '../components/ui/Tabs'
import { InfoList } from '../components/ui/InfoList'
import { niceAxis, ValueAxisFrame } from '../components/ui/ValueAxis'
import { formatDateTime } from '../components/ui/format'

type Kind = 'request' | 'function'
type Feature = {
  kind: Kind; feature: string; calls: number; bytes: number; unsized: number
  series: { at: string; calls: number; bytes: number; unsized: number }[]
}
type Usage = {
  bucket_minutes: number
  features: Feature[]
  gaps: { at: string; reasons: string[] }[]
  calls_first_at: string | null
  mau: { current: string; months: { month: string; users: number }[]; this_month: { day: string; users: number }[] }
  sizes: { series: { day: string; db: number; storage: number }[]; db: number; storage: number }
  sync: { last_run_at: string | null; finished_at: string | null; ok: boolean | null; ok_at: string | null; message: string | null } | null
}

const RANGES = [
  { hours: 24, label: '24시간' },
  { hours: 24 * 7, label: '7일' },
  { hours: 24 * 30, label: '30일' },
  { hours: 24 * 365, label: '1년' },
] as const

type Tab = 'egress' | 'functions' | 'mau' | 'sizes'
const TABS = [['egress', '데이터 전송량'], ['functions', '서버 함수 호출'], ['mau', '월간 활성 사용자'], ['sizes', '저장 용량']] as const

/**
 * 그래프 한 줄 — 기능 하나의 기간 합과 묶음마다의 값. 전송량은 바이트, 서버 함수 호출은 건수.
 * unsized 는 전송량에서만 — 크기를 몰라 바이트에 못 더한 건수다(0B 가 "안 나갔다"로 읽히지 않게 따로 보인다).
 */
type Row = { feature: string; total: number; unsized: number; series: { at: string; v: number; unsized: number }[] }

/** 서버 함수만 고르거나(kind), 요청·서버 함수를 기능 이름으로 합친다(null). */
function rowsOf(features: Feature[], kind: Kind | null, metric: 'calls' | 'bytes'): Row[] {
  const withUnsized = metric === 'bytes'
  const byName = new Map<string, { total: number; unsized: number; at: Map<string, { v: number; unsized: number }> }>()
  for (const f of features) {
    if (kind && f.kind !== kind) continue
    const r = byName.get(f.feature) ?? { total: 0, unsized: 0, at: new Map<string, { v: number; unsized: number }>() }
    r.total += f[metric]
    if (withUnsized) r.unsized += f.unsized
    for (const p of f.series) {
      const cur = r.at.get(p.at) ?? { v: 0, unsized: 0 }
      r.at.set(p.at, { v: cur.v + p[metric], unsized: cur.unsized + (withUnsized ? p.unsized : 0) })
    }
    byName.set(f.feature, r)
  }
  return [...byName.entries()].map(([feature, r]) => ({
    feature, total: r.total, unsized: r.unsized,
    series: [...r.at.entries()].map(([at, p]) => ({ at, v: p.v, unsized: p.unsized })),
  }))
}

type Metric = { unitOf: (maxPerHour: number) => { div: number; label: string }; text: (v: number) => string; head: string }

const HOUR = 3600_000
const W = 600
const H = 180
// 막대에 따로 색을 주는 기능 수. 나머지는 "그 밖" 한 색으로 묶는다 — 색이 너무 많으면 어느 것이 어느 기능인지 못 가린다.
const TOP_FEATURES = 6
const FEATURE_COLORS = ['#1677ff', '#f59e0b', '#10b981', '#8b5cf6', '#ef4444', '#06b6d4']
const REST_COLOR = '#a3a3a3'
const REST = '그 밖'

const n = (v: number) => Math.round(v).toLocaleString('ko-KR')

function bytesText(b: number): string {
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let v = b
  let i = 0
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toLocaleString('ko-KR', { maximumFractionDigits: i === 0 ? 0 : 1 })}${units[i]}`
}

/** 눈금이 정수라 값이 10 이상 되는 단위를 고른다(작은 게임에서 0·1 두 칸만 남지 않게). */
function bytesUnit(max: number): { div: number; label: string } {
  if (max < 10 * 1024 ** 2) return { div: 1024, label: 'KB' }
  if (max < 10 * 1024 ** 3) return { div: 1024 ** 2, label: 'MB' }
  return { div: 1024 ** 3, label: 'GB' }
}

const BYTES: Metric = { unitOf: bytesUnit, text: bytesText, head: '전송량' }
const CALLS: Metric = { unitOf: () => ({ div: 1, label: '건' }), text: (v) => `${n(v)}건`, head: '건수' }

function formatAt(iso: string, withDate: boolean): string {
  const d = new Date(iso)
  const hm = d.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false })
  return withDate ? `${d.getMonth() + 1}/${d.getDate()} ${hm}` : hm
}

/** 묶음 시작 시각 글자 — 하루 묶음은 자정이라 시각을 빼고 날짜만. */
function bucketAt(iso: string, minutes: number, withDate: boolean): string {
  if (minutes < 1440) return formatAt(iso, withDate)
  const d = new Date(iso)
  return `${d.getMonth() + 1}/${d.getDate()}`
}

function bucketLabel(minutes: number): string {
  if (minutes >= 1440) return '하루'
  return `${minutes / 60}시간`
}

/**
 * 묶음 하나가 실제로 덮은 시간 — 기간 앞뒤에서 잘린 묶음(지금 막 시작한 묶음·기록을 켠 날)도 시간당 값으로 그리려고 쓴다.
 * 끝은 서버가 준 마지막 시간의 끝 — 서버는 끝난 시간만 주고, 매시 10분 전에는 직전 시간도 뺀다(그때 요청 수가 들어온다).
 */
function coveredHours(at: string, minutes: number, windowStart: number, firstAt: string | null): number {
  const s = Math.max(new Date(at).getTime(), windowStart, firstAt ? new Date(firstAt).getTime() : 0)
  const e = Math.min(new Date(at).getTime() + minutes * 60_000, Math.floor((Date.now() - 10 * 60_000) / HOUR) * HOUR)
  return Math.max(1, (e - s) / HOUR)
}

const xOf = (iso: string, t0: number, t1: number) => ((new Date(iso).getTime() - t0) / (t1 - t0)) * W

type Gaps = Usage['gaps']

/**
 * 그 묶음의 요청 수가 온전하지 않을 수 있는 사유 — 서버가 준 시각과 문자열 꼴이 달라 시각 값으로 맞춘다.
 * 요청 수(전송량 탭)에만 해당한다. 서버 함수 호출은 함수가 직접 센다.
 */
function gapOf(gaps: Gaps, at: string): string[] | null {
  const t = new Date(at).getTime()
  return gaps.find((g) => new Date(g.at).getTime() === t)?.reasons ?? null
}

function TimeFooter({ hours, withDate }: { hours: number; withDate: boolean }) {
  return (
    <div className="flex justify-between text-[11px] text-neutral-400 mt-1">
      <span>{formatAt(new Date(Date.now() - hours * HOUR).toISOString(), withDate)}</span>
      <span>지금</span>
    </div>
  )
}

type Slice = { name: string; color: string; total: number }

/** 많이 쓴 기능부터 TOP_FEATURES 개는 제 색, 나머지는 "그 밖"으로. 표와 막대가 같은 묶음·색을 쓴다. */
function slicesOf(rows: Row[]): { slices: Slice[]; groupOf: (f: string) => string } {
  const sorted = [...rows].sort((a, b) => b.total - a.total)
  const top = sorted.slice(0, TOP_FEATURES)
  const rest = sorted.slice(TOP_FEATURES)
  const slices: Slice[] = top.map((f, i) => ({ name: f.feature, color: FEATURE_COLORS[i] ?? REST_COLOR, total: f.total }))
  if (rest.length > 0) slices.push({ name: REST, color: REST_COLOR, total: rest.reduce((a, f) => a + f.total, 0) })
  const topNames = new Set(top.map((f) => f.feature))
  return { slices, groupOf: (f) => (topNames.has(f) ? f : REST) }
}

/** 기능별로 쌓은 시간당 막대. 막대를 누르면 그 묶음을 고르고, 같은 막대를 다시 누르면 푼다. */
function StackedChart({ rows, metric, data, hours, gaps, selectedAt, onSelect }: {
  rows: Row[]; metric: Metric; data: Usage; hours: number
  gaps: Gaps; selectedAt: string | null; onSelect: (at: string | null) => void
}) {
  const [hover, setHover] = useState<number | null>(null)
  const withDate = hours > 24
  const { slices, groupOf } = slicesOf(rows)

  const { bars, axis, unit, barW } = useMemo(() => {
    const t0 = Date.now() - hours * HOUR
    const t1 = Date.now()
    // 묶음마다 기능별 합 → 시간당 값으로 나눠 쌓는다.
    const byAt = new Map<string, Map<string, number>>()
    for (const r of rows) {
      for (const p of r.series) {
        const m = byAt.get(p.at) ?? new Map<string, number>()
        const g = groupOf(r.feature)
        m.set(g, (m.get(g) ?? 0) + p.v)
        byAt.set(p.at, m)
      }
    }
    const raw = [...byAt.entries()].sort((a, b) => a[0].localeCompare(b[0])).map(([at, m]) => {
      const hrs = coveredHours(at, data.bucket_minutes, t0, data.calls_first_at)
      const parts = slices.map((s) => ({ ...s, perHour: (m.get(s.name) ?? 0) / hrs, sum: m.get(s.name) ?? 0 }))
      return { at, x: xOf(at, t0, t1), parts, total: parts.reduce((a, p) => a + p.perHour, 0), gap: gapOf(gaps, at) }
    })
    const unit = metric.unitOf(Math.max(1, ...raw.map((b) => b.total)))
    const axis = niceAxis(Math.max(1, ...raw.map((b) => b.total / unit.div)))
    const barW = Math.max(1, ((data.bucket_minutes * 60_000) / (t1 - t0)) * W * 0.8)
    return { bars: raw, axis, unit, barW }
  }, [rows, slices, groupOf, metric, hours, gaps, data.bucket_minutes, data.calls_first_at])

  const nearest = (e: React.MouseEvent<SVGSVGElement>): number | null => {
    const rect = e.currentTarget.getBoundingClientRect()
    const x = ((e.clientX - rect.left) / rect.width) * W
    let best: number | null = null
    bars.forEach((b, i) => {
      const cur = best === null ? undefined : bars[best]
      if (!cur || Math.abs(b.x + barW / 2 - x) < Math.abs(cur.x + barW / 2 - x)) best = i
    })
    return best
  }

  const onClick = (e: React.MouseEvent<SVGSVGElement>) => {
    const i = nearest(e)
    const b = i === null ? undefined : bars[i]
    if (b) onSelect(b.at === selectedAt ? null : b.at)
  }

  const hb = hover !== null ? bars[hover] : undefined
  const tip = hb ? (
    <div className="space-y-0.5">
      <div className="text-neutral-500">{bucketAt(hb.at, data.bucket_minutes, withDate)} · {bucketLabel(data.bucket_minutes)} · 시간당</div>
      {hb.parts.filter((p) => p.sum > 0).map((p) => (
        <div key={p.name} className="flex items-center gap-1.5">
          <span className="inline-block w-2.5 h-2.5 rounded-sm shrink-0" style={{ backgroundColor: p.color }} />
          <span>{p.name}</span>
          <span className="ml-auto pl-3 tabular-nums">{metric.text(p.perHour)}</span>
        </div>
      ))}
      {hb.gap && <div className="text-neutral-500">일부 빠졌을 수 있음 · {hb.gap.join(', ')}</div>}
    </div>
  ) : null

  return (
    <div>
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-500">
        {slices.map((s) => (
          <span key={s.name} className="inline-flex items-center gap-1.5">
            <span className="inline-block w-2.5 h-2.5 rounded-sm" style={{ backgroundColor: s.color }} />
            {s.name}
          </span>
        ))}
        {bars.some((b) => b.gap) && (
          <span className="inline-flex items-center gap-1.5">
            <span className="inline-block w-3 border-t-2 border-dotted border-neutral-500" />
            일부 빠졌을 수 있는 시간
          </span>
        )}
        <span className="ml-auto">시간당 {unit.label}</span>
      </div>
      <ValueAxisFrame axis={axis} heightCls="h-44" tip={tip} footer={<TimeFooter hours={hours} withDate={withDate} />}>
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full h-full block cursor-pointer"
          onMouseMove={(e) => setHover(nearest(e))} onMouseLeave={() => setHover(null)} onClick={onClick}>
          <line x1={0} y1={H - 0.5} x2={W} y2={H - 0.5} className="stroke-neutral-200" strokeWidth={1} vectorEffect="non-scaling-stroke" />
          {bars.map((b, i) => {
            let y = H
            // 고른 막대가 있으면 그 막대만 진하게, 없으면 마우스를 올린 막대만.
            const strong = selectedAt !== null ? b.at === selectedAt : hover === null || hover === i
            return (
              <g key={b.at} opacity={strong ? 1 : selectedAt !== null ? 0.3 : 0.55}>
                {b.parts.map((p) => {
                  const h = (p.perHour / unit.div / axis.top) * H
                  y -= h
                  return h > 0 ? <rect key={p.name} x={b.x} y={y} width={barW} height={h} fill={p.color} /> : null
                })}
                {b.gap && (
                  <line x1={b.x} y1={Math.max(y - 2, 1)} x2={b.x + barW} y2={Math.max(y - 2, 1)}className="stroke-neutral-500" strokeWidth={2}
                    strokeDasharray="2 2" vectorEffect="non-scaling-stroke" />
                )}
              </g>
            )
          })}
        </svg>
      </ValueAxisFrame>
    </div>
  )
}

/** 기능별 값·비율. 색은 막대 그래프와 같게 기능 이름으로 정한다 — 고른 막대에서는 순서가 바뀌어도 색이 따라간다. */
function ShareTable({ rows, metric, colorOf }: {
  rows: { feature: string; v: number; unsized: number }[]; metric: Metric; colorOf: (feature: string) => string
}) {
  const total = rows.reduce((a, f) => a + f.v, 0)
  const sorted = [...rows].sort((a, b) => b.v - a.v)
  return (
    <div className="overflow-auto rounded-md border border-neutral-200">
      <table className="w-full text-sm">
        <thead className="bg-neutral-50 text-neutral-500 text-xs">
          <tr>
            <th className="text-left px-4 py-2 font-medium">기능</th>
            <th className="text-right px-4 py-2 font-medium w-32">{metric.head}</th>
            <th className="text-left px-4 py-2 font-medium w-56">비율</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((f) => {
            const share = total > 0 ? (f.v / total) * 100 : 0
            return (
              <tr key={f.feature} className="border-t border-neutral-100">
                <td className="px-4 py-2 text-neutral-800">{f.feature}</td>
                <td className="px-4 py-2 text-right tabular-nums whitespace-nowrap">
                  {f.v === 0 && f.unsized > 0 ? <span className="text-neutral-400">알 수 없음</span> : metric.text(f.v)}
                  {f.unsized > 0 && <div className="text-[11px] text-neutral-400">크기 모름 {n(f.unsized)}건</div>}
                </td>
                <td className="px-4 py-2">
                  <div className="flex items-center gap-2">
                    <div className="flex-1 h-1.5 rounded-full bg-neutral-100 overflow-hidden">
                      <div className="h-full rounded-full" style={{ width: `${share}%`, backgroundColor: colorOf(f.feature) }} />
                    </div>
                    <span className="w-12 text-right tabular-nums text-xs text-neutral-600">
                      {share.toLocaleString('ko-KR', { maximumFractionDigits: 1 })}%
                    </span>
                  </div>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/** 기능별 카드 본문 — 막대·요약·표. 전송량 탭과 서버 함수 호출 탭이 같은 꼴을 쓴다. */
function FeatureBreakdown({ rows, metric, data, hours, gaps = [], selectedAt, onSelect }: {
  rows: Row[]; metric: Metric; data: Usage; hours: number
  gaps?: Gaps; selectedAt: string | null; onSelect: (at: string | null) => void
}) {
  if (rows.length === 0) {
    return <div className="h-44 flex items-center justify-center text-sm text-neutral-400">아직 기록이 없습니다.</div>
  }
  const { slices, groupOf } = slicesOf(rows)
  const colorOf = (feature: string) => slices.find((s) => s.name === groupOf(feature))?.color ?? REST_COLOR
  // 막대를 골랐으면 그 묶음의 값만, 아니면 기간 전체.
  const tableRows = selectedAt
    ? rows.map((r) => {
        const p = r.series.find((s) => s.at === selectedAt)
        return { feature: r.feature, v: p?.v ?? 0, unsized: p?.unsized ?? 0 }
      }).filter((r) => r.v > 0 || r.unsized > 0)
    : rows.map((r) => ({ feature: r.feature, v: r.total, unsized: r.unsized }))
  const total = tableRows.reduce((a, r) => a + r.v, 0)
  const top = [...tableRows].sort((a, b) => b.v - a.v)[0]
  const selectedGap = selectedAt ? gapOf(gaps, selectedAt) : null
  // 점선은 막대가 있는 묶음에만 그려진다 — 막대 없는 묶음의 사유(수집이 멈춘 시간)는 따로 센다.
  const withData = new Set(rows.flatMap((r) => r.series.filter((p) => p.v > 0 || p.unsized > 0).map((p) => new Date(p.at).getTime())))
  const gapBars = gaps.filter((g) => withData.has(new Date(g.at).getTime())).length
  const emptyGaps = gaps.length - gapBars
  return (
    <>
      <StackedChart rows={rows} metric={metric} data={data} hours={hours} gaps={gaps} selectedAt={selectedAt} onSelect={onSelect} />
      <div className="flex flex-wrap items-end justify-between gap-3">
        <InfoList className="flex-1" items={[
          { label: '보는 범위', value: selectedAt
            ? `${bucketAt(selectedAt, data.bucket_minutes, true)} · ${bucketLabel(data.bucket_minutes)}`
            : `기간 전체 · ${RANGES.find((r) => r.hours === hours)?.label ?? ''}` },
          { label: '합계', value: metric.text(total) },
          { label: '가장 많은 기능', value: top ? `${top.feature} · ${metric.text(top.v)}` : null },
        ]} />
        {selectedAt && (
          <Button variant="outline" size="compact" onClick={() => onSelect(null)}>기간 전체 보기</Button>
        )}
      </div>
      {selectedGap
        ? <div className="text-xs text-neutral-500">이 시간은 일부 빠졌을 수 있습니다 · {selectedGap.join(', ')}</div>
        : !selectedAt && (gapBars > 0 || emptyGaps > 0) && (
          <div className="text-xs text-neutral-500">
            {[gapBars > 0 && `일부 빠졌을 수 있는 막대 ${n(gapBars)}개`, emptyGaps > 0 && `기록 없이 비어 있는 자리 ${n(emptyGaps)}곳`]
              .filter(Boolean).join(' · ')}
          </div>
        )}
      <ShareTable rows={tableRows} metric={metric} colorOf={colorOf} />
      {data.calls_first_at && new Date(data.calls_first_at).getTime() > Date.now() - hours * HOUR && (
        <div className="text-xs text-neutral-400">기록은 {formatAt(data.calls_first_at, true)}부터 있습니다.</div>
      )}
    </>
  )
}

const MAU_COLOR = '#8b5cf6'
const DB_COLOR = '#1677ff'
const STORAGE_COLOR = '#10b981'

/** 'YYYY-MM-DD' 날짜를 그 날짜 그대로 다룬다(시간대에 따라 하루 밀리지 않게). */
const ymd = (s: string) => s.slice(0, 10).split('-').map(Number) as [number, number, number]
const monthLabel = (s: string) => `${ymd(s)[1]}월`
const dayLabel = (s: string) => `${ymd(s)[1]}/${ymd(s)[2]}`

/** 이번 달(게임 시간대)로 끝나는 최근 12달 — 기록이 없는 달은 0명으로 채워 막대 자리를 맞춘다. */
function last12Months(current: string, months: { month: string; users: number }[]): { month: string; users: number }[] {
  const byKey = new Map(months.map((m) => [m.month.slice(0, 7), m.users]))
  const last = ymd(current)
  const out: { month: string; users: number }[] = []
  for (let i = 11; i >= 0; i--) {
    const key = new Date(Date.UTC(last[0], last[1] - 1 - i, 1)).toISOString().slice(0, 7)
    out.push({ month: `${key}-01`, users: byKey.get(key) ?? 0 })
  }
  return out
}

function MauChart({ months }: { months: { month: string; users: number }[] }) {
  const [hover, setHover] = useState<number | null>(null)
  const axis = niceAxis(Math.max(1, ...months.map((m) => m.users)))
  const slot = W / months.length
  const hm = hover !== null ? months[hover] : undefined
  const tip = hm ? (
    <div className="flex items-center gap-1.5">
      <span className="inline-block w-2.5 h-2.5 rounded-sm shrink-0" style={{ backgroundColor: MAU_COLOR }} />
      <span>{ymd(hm.month)[0]}년 {monthLabel(hm.month)}</span>
      <span className="pl-3 tabular-nums font-medium">{n(hm.users)}명</span>
    </div>
  ) : null
  return (
    <ValueAxisFrame axis={axis} heightCls="h-44" tip={tip}
      footer={
        <div className="flex text-[11px] text-neutral-400 mt-1">
          {months.map((m) => <span key={m.month} className="flex-1 text-center">{monthLabel(m.month)}</span>)}
        </div>
      }>
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full h-full block cursor-crosshair"
        onMouseMove={(e) => {
          const rect = e.currentTarget.getBoundingClientRect()
          setHover(Math.min(months.length - 1, Math.max(0, Math.floor(((e.clientX - rect.left) / rect.width) * months.length))))
        }}
        onMouseLeave={() => setHover(null)}>
        <line x1={0} y1={H - 0.5} x2={W} y2={H - 0.5} className="stroke-neutral-200" strokeWidth={1} vectorEffect="non-scaling-stroke" />
        {months.map((m, i) => {
          const h = (m.users / axis.top) * H
          // 이번 달은 아직 차는 중이라 옅게.
          return h > 0 ? (
            <rect key={m.month} x={i * slot + slot * 0.15} y={H - h} width={slot * 0.7} height={h} fill={MAU_COLOR}
              opacity={hover === null || hover === i ? (i === months.length - 1 ? 0.6 : 1) : 0.45} />
          ) : null
        })}
      </svg>
    </ValueAxisFrame>
  )
}

function SizeChart({ series }: { series: { day: string; db: number; storage: number }[] }) {
  const [hover, setHover] = useState<number | null>(null)
  const unit = bytesUnit(Math.max(1, ...series.map((p) => Math.max(p.db, p.storage))))
  const axis = niceAxis(Math.max(1, ...series.map((p) => Math.max(p.db, p.storage) / unit.div)))
  const xAt = (i: number) => (series.length === 1 ? W / 2 : (i / (series.length - 1)) * W)
  const yAt = (v: number) => H - (v / unit.div / axis.top) * H
  const line = (pick: (p: { db: number; storage: number }) => number) =>
    series.map((p, i) => `${i === 0 ? 'M' : 'L'}${xAt(i).toFixed(1)},${yAt(pick(p)).toFixed(1)}`).join(' ')
  const hp = hover !== null ? series[hover] : undefined
  const tip = hp ? (
    <div className="space-y-0.5">
      <div className="text-neutral-500">{ymd(hp.day)[0]}년 {dayLabel(hp.day)}</div>
      <div className="flex items-center gap-1.5">
        <span className="inline-block w-2.5 h-2.5 rounded-sm shrink-0" style={{ backgroundColor: DB_COLOR }} />
        <span>DB</span><span className="ml-auto pl-3 tabular-nums">{bytesText(hp.db)}</span>
      </div>
      <div className="flex items-center gap-1.5">
        <span className="inline-block w-2.5 h-2.5 rounded-sm shrink-0" style={{ backgroundColor: STORAGE_COLOR }} />
        <span>파일</span><span className="ml-auto pl-3 tabular-nums">{bytesText(hp.storage)}</span>
      </div>
    </div>
  ) : null
  const first = series[0]
  const last = series[series.length - 1]
  return (
    <div>
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-500">
        <span className="inline-flex items-center gap-1.5">
          <span className="inline-block w-2.5 h-2.5 rounded-sm" style={{ backgroundColor: DB_COLOR }} />
          DB · {unit.label}
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className="inline-block w-2.5 h-2.5 rounded-sm" style={{ backgroundColor: STORAGE_COLOR }} />
          파일 저장 공간 · {unit.label}
        </span>
      </div>
      <ValueAxisFrame axis={axis} heightCls="h-44" tip={tip}
        footer={first && last && (
          <div className="flex justify-between text-[11px] text-neutral-400 mt-1">
            <span>{dayLabel(first.day)}</span><span>{dayLabel(last.day)}</span>
          </div>
        )}>
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full h-full block cursor-crosshair"
          onMouseMove={(e) => {
            const rect = e.currentTarget.getBoundingClientRect()
            const r = (e.clientX - rect.left) / rect.width
            setHover(Math.min(series.length - 1, Math.max(0, Math.round(r * (series.length - 1)))))
          }}
          onMouseLeave={() => setHover(null)}>
          <line x1={0} y1={H - 0.5} x2={W} y2={H - 0.5} className="stroke-neutral-200" strokeWidth={1} vectorEffect="non-scaling-stroke" />
          <path d={line((p) => p.db)} fill="none" stroke={DB_COLOR} strokeWidth={1.5} vectorEffect="non-scaling-stroke" />
          <path d={line((p) => p.storage)} fill="none" stroke={STORAGE_COLOR} strokeWidth={1.5} vectorEffect="non-scaling-stroke" />
          {hover !== null && (
            <line x1={xAt(hover)} y1={0} x2={xAt(hover)} y2={H} className="stroke-neutral-400" strokeWidth={1}
              strokeDasharray="3 3" vectorEffect="non-scaling-stroke" />
          )}
        </svg>
      </ValueAxisFrame>
    </div>
  )
}

/** 수집 상태 한 줄 — 정상이면 한마디, 문제면 무엇이 문제인지만. 세부는 "자세히"로 접는다. */
function SyncLine({ data }: { data: Usage }) {
  const [open, setOpen] = useState(false)
  const s = data.sync
  // 매시 5분에 돈다 — 두 시간 넘게 안 깨어났으면 멈춘 것이다(수집이 실패를 삼켜 자동 작업 기록에는 성공으로 남는다).
  const stale = !s?.last_run_at || Date.now() - new Date(s.last_run_at).getTime() > 2 * HOUR + 10 * 60_000
  const problem = !s?.last_run_at ? '아직 한 번도 모으지 않았습니다'
    : stale ? '두 시간 넘게 모으지 못했습니다'
    // 요청 수는 셌는데(그 시각이 성공 시각으로 남는다) 저장 용량만 못 남긴 경우를 가른다.
    : s.ok === false && s.ok_at && s.ok_at === s.finished_at ? '저장 용량 기록에 실패했습니다'
    : s.ok === false ? '마지막 수집에 실패했습니다'
    : null
  return (
    <div className="text-xs">
      <div className="flex flex-wrap items-center gap-2">
        {problem
          ? <span className="rounded bg-red-50 px-2 py-0.5 text-red-700">{problem}</span>
          : <span className="rounded bg-emerald-50 px-2 py-0.5 text-emerald-700">한 시간마다 모으는 중</span>}
        {s?.last_run_at && (
          <button type="button" onClick={() => setOpen((v) => !v)} className="inline-flex items-center gap-0.5 text-neutral-500 hover:text-neutral-700">
            자세히 {open ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
          </button>
        )}
      </div>
      {open && s?.last_run_at && (
        <div className="mt-1.5 text-neutral-500 space-y-0.5">
          <div>마지막 실행 {formatDateTime(s.last_run_at)}</div>
          {s.message && <div className="break-all">{s.message}</div>}
        </div>
      )}
    </div>
  )
}

export default function ServerStatus({ target, onUnauthenticated }: { target: ProjectTarget; onUnauthenticated: () => void }) {
  const [hours, setHours] = useState<number>(24)
  // 그래프는 받아 온 기간과 함께 둔다 — 기간을 바꾼 직전 데이터를 새 기간 잣대로 그리지 않게.
  const [data, setData] = useState<{ hours: number; usage: Usage } | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selectedAt, setSelectedAt] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('egress')
  // 전송량은 어림값이라 기능끼리 견줄 때는 DB 가 직접 센 요청 수가 더 믿을 만하다 — 같은 그래프를 건수로도 본다.
  const [egressBy, setEgressBy] = useState<'bytes' | 'calls'>('bytes')
  const reqId = useRef(0)

  const load = useCallback(async () => {
    // 기간을 빨리 바꾸면 응답이 순서를 바꿔 올 수 있다 — 마지막 요청의 응답만 쓴다.
    const id = ++reqId.current
    setLoading(true)
    setError('')
    try {
      const usage = await callAdmin<Usage>(target, 'usage.stats', { hours })
      if (id === reqId.current) setData({ hours, usage })
    } catch (e: unknown) {
      if (id !== reqId.current) return
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '서버 상태를 불러오지 못했습니다.')
    } finally {
      if (id === reqId.current) setLoading(false)
    }
  }, [target, hours, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  // 기간·탭을 바꾸거나 다시 불러오면 고른 막대를 푼다 — 같은 시각 막대가 없을 수 있다.
  useEffect(() => { setSelectedAt(null) }, [data, tab])

  const usage = data?.usage
  const egressRows = useMemo(() => (usage ? rowsOf(usage.features, null, egressBy) : []), [usage, egressBy])
  const functionRows = useMemo(() => (usage ? rowsOf(usage.features, 'function', 'calls') : []), [usage])

  const months12 = usage ? last12Months(usage.mau.current, usage.mau.months) : []
  const thisMonth = months12[months12.length - 1]
  const lastMonth = months12[months12.length - 2]
  const peakMonth = months12.reduce<{ month: string; users: number } | undefined>((a, m) => (!a || m.users > a.users ? m : a), undefined)
  const busiestDay = usage?.mau.this_month.reduce<{ day: string; users: number } | undefined>((a, d) => (!a || d.users > a.users ? d : a), undefined)
  // 30일 전 무렵 기록 — 기록이 30일 넘게 쌓였을 때만 보인다.
  const monthAgoDay = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10)
  const sizeSeries = usage?.sizes.series ?? []
  const sizeMonthAgo = sizeSeries[0] && sizeSeries[0].day.slice(0, 10) <= monthAgoDay
    ? sizeSeries.find((p) => p.day.slice(0, 10) >= monthAgoDay)
    : undefined
  const timeTab = tab === 'egress' || tab === 'functions'

  return (
    <div className="space-y-4">
      <PageHeader
        title="서버 상태"
        description="Supabase 사용량을 항목별로 봅니다."
        actions={
          <Button variant="outline" size="compact" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            새로고침
          </Button>
        }
      />
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <Tabs value={tab} onChange={setTab} tabs={TABS} />

      <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
        {timeTab ? (
          <div>
            <div className="text-xs font-medium text-neutral-500 mb-1">기간</div>
            <Segmented value={String(hours)} onChange={(v) => setHours(Number(v))}
              options={RANGES.map((r) => ({ v: String(r.hours), label: r.label }))} />
          </div>
        ) : <div />}
        {usage && <SyncLine data={usage} />}
      </div>

      {loading && !usage ? (
        <WhiteCard className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </WhiteCard>
      ) : data && usage && (
        <>
          {tab === 'egress' && <WhiteCard className="p-5 space-y-5">
            <CardHeader title="데이터 전송량"
              description={egressBy === 'bytes'
                ? '플레이어에게 나간 데이터 양을 기능별로 어림해 봅니다. 막대를 누르면 그때의 값만 봅니다.'
                : '플레이어 요청 수를 기능별로 봅니다. 막대를 누르면 그때의 값만 봅니다.'} />
            <div>
              <div className="text-xs font-medium text-neutral-500 mb-1">기준</div>
              <Segmented value={egressBy} onChange={setEgressBy}
                options={[{ v: 'bytes', label: '전송량' }, { v: 'calls', label: '요청 수' }]} />
            </div>
            <FeatureBreakdown rows={egressRows} metric={egressBy === 'bytes' ? BYTES : CALLS} data={usage} hours={data.hours}
              gaps={usage.gaps} selectedAt={selectedAt} onSelect={setSelectedAt} />
          </WhiteCard>}

          {tab === 'functions' && <WhiteCard className="p-5 space-y-5">
            <CardHeader title="서버 함수 호출" description="서버 함수가 불린 횟수를 기능별로 봅니다. 막대를 누르면 그때의 값만 봅니다." />
            <FeatureBreakdown rows={functionRows} metric={CALLS} data={usage} hours={data.hours}
              selectedAt={selectedAt} onSelect={setSelectedAt} />
          </WhiteCard>}

          {tab === 'mau' && <WhiteCard className="p-5 space-y-5">
            <CardHeader title="월간 활성 사용자" description="최근 12달 동안 달마다 한 번이라도 접속한 계정 수를 봅니다." />
            <InfoList items={[
              { label: '이번 달', value: `${n(thisMonth?.users ?? 0)}명` },
              { label: '지난달', value: `${n(lastMonth?.users ?? 0)}명` },
              { label: '가장 많았던 달', value: peakMonth && peakMonth.users > 0
                ? `${ymd(peakMonth.month)[0]}년 ${monthLabel(peakMonth.month)} · ${n(peakMonth.users)}명` : null },
              { label: '가장 많이 늘어난 날', value: busiestDay ? `${dayLabel(busiestDay.day)} · ${n(busiestDay.users)}명` : null },
            ]} />
            <MauChart months={months12} />
          </WhiteCard>}

          {tab === 'sizes' && <WhiteCard className="p-5 space-y-5">
            <CardHeader title="저장 용량" description="DB와 파일 저장 공간이 차지하는 크기를 날마다 봅니다." />
            <InfoList items={[
              { label: 'DB', value: bytesText(usage.sizes.db) },
              { label: '파일 저장 공간', value: bytesText(usage.sizes.storage) },
              ...(sizeMonthAgo ? [{ label: '30일 사이 늘어난 DB', value: bytesText(Math.max(0, usage.sizes.db - sizeMonthAgo.db)) }] : []),
            ]} />
            {usage.sizes.series.length < 2 ? (
              <div className="h-24 flex items-center justify-center text-sm text-neutral-400">
                {usage.sizes.series.length === 0 ? '아직 기록이 없습니다.' : '아직 하루치 기록만 있습니다.'}
              </div>
            ) : (
              <SizeChart series={usage.sizes.series} />
            )}
          </WhiteCard>}
        </>
      )}
    </div>
  )
}
