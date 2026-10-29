import { useState, type ReactNode } from 'react'

export type Axis = { top: number; ticks: number[] }

/**
 * 0 부터 max 를 덮는 깔끔한 눈금(0·50·100…). 그래프는 max 가 아니라 top 을 꼭대기로 그린다 —
 * 그래야 막대·선 높이와 눈금 줄이 맞는다. 세는 값(명·건·개)이라 눈금 간격은 정수만 쓴다.
 */
export function niceAxis(max: number, count = 4): Axis {
  const raw = Math.max(1, max) / count
  const pow = 10 ** Math.floor(Math.log10(raw))
  const step = Math.max(1, [1, 2, 5, 10].map((m) => m * pow).find((s) => s >= raw) ?? 10 * pow)
  const top = Math.max(step, Math.ceil(Math.max(1, max) / step) * step)
  const ticks: number[] = []
  for (let v = 0; v <= top; v += step) ticks.push(v)
  return { top, ticks }
}

/** 눈금 글자 — 만 단위부터 줄인다(12,000 → 1.2만, 150,000,000 → 1.5억). */
export function tickLabel(v: number): string {
  if (v >= 100_000_000) return `${(v / 100_000_000).toLocaleString('ko-KR', { maximumFractionDigits: 1 })}억`
  if (v >= 10_000) return `${(v / 10_000).toLocaleString('ko-KR', { maximumFractionDigits: 1 })}만`
  return v.toLocaleString('ko-KR')
}

/**
 * 그래프 왼쪽 값 눈금 + 옅은 가로줄. 그래프(svg)는 children 으로 넣고 h-full 로 채운다.
 * 눈금 글자는 그래프가 가로로 늘어나도 찌그러지지 않게 svg 밖(HTML)에 둔다. footer 는 그래프 바로 아래(가로축 글자) 자리.
 * tip 은 커서를 따라다니는 값 상자의 내용 — 무엇을 보일지는 그래프가(커서 아래 막대·지점), 어디에 띄울지는 여기가 정한다.
 */
export function ValueAxisFrame({ axis, heightCls, children, footer, tip }: {
  axis: Axis
  heightCls: string
  children: ReactNode
  footer?: ReactNode
  tip?: ReactNode
}) {
  const [cursor, setCursor] = useState<{ x: number; y: number; w: number; h: number } | null>(null)
  const labels = axis.ticks.map(tickLabel)
  const widest = labels.reduce((a, b) => (b.length > a.length ? b : a), '')
  const at = (v: number) => `${(v / axis.top) * 100}%`
  const onMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const r = e.currentTarget.getBoundingClientRect()
    setCursor({ x: e.clientX - r.left, y: e.clientY - r.top, w: r.width, h: r.height })
  }
  // 커서 오른쪽 아래에 띄우고, 오른쪽 절반·아래쪽 절반에서는 반대편으로 넘겨 그래프 밖으로 잘리지 않게 한다.
  const tipStyle = cursor && {
    left: cursor.x > cursor.w / 2 ? cursor.x - 12 : cursor.x + 12,
    top: cursor.y > cursor.h / 2 ? cursor.y - 12 : cursor.y + 12,
    transform: `translate(${cursor.x > cursor.w / 2 ? '-100%' : '0'}, ${cursor.y > cursor.h / 2 ? '-100%' : '0'})`,
  }
  return (
    // items-start: 눈금 칸이 아래 가로축 글자 줄까지 늘어나면 눈금 높이가 그래프와 어긋난다.
    // mt-2: 맨 위 눈금 글자는 꼭대기 선에 걸쳐 반쯤 위로 나온다 — 위 글자와 붙어 보이지 않게 띄운다.
    <div className="flex items-start gap-2 mt-2">
      <div className="relative shrink-0 text-[11px] text-neutral-400 tabular-nums text-right">
        {/* 가장 긴 글자를 안 보이게 깔아 칸 폭을 잡는다 — 글자들은 아래에서 높이에 맞춰 띄운다. */}
        <div className={`invisible ${heightCls}`} aria-hidden>{widest}</div>
        {axis.ticks.map((v, i) => (
          <div key={v} className="absolute right-0 translate-y-1/2 leading-none whitespace-nowrap" style={{ bottom: at(v) }}>{labels[i]}</div>
        ))}
      </div>
      <div className="flex-1 min-w-0">
        <div className={`relative ${heightCls}`} onMouseMove={onMove} onMouseLeave={() => setCursor(null)}>
          {axis.ticks.slice(1).map((v) => (
            <div key={v} className="absolute inset-x-0 border-t border-neutral-100 pointer-events-none" style={{ bottom: at(v) }} />
          ))}
          <div className="relative h-full">{children}</div>
          {tip && tipStyle && (
            <div className="absolute z-10 pointer-events-none whitespace-nowrap rounded-md border border-neutral-200 bg-white px-2 py-1 text-xs text-neutral-800 shadow-sm"
              style={tipStyle}>
              {tip}
            </div>
          )}
        </div>
        {footer}
      </div>
    </div>
  )
}
