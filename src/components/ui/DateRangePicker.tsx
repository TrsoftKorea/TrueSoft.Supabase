import { useState, useRef, useEffect } from 'react'
import { Calendar as CalendarIcon, ChevronLeft, ChevronRight } from 'lucide-react'

type Props = {
  start: string | null
  end: string | null
  onApply: (start: string | null, end: string | null) => void
}

const WEEKDAYS = ['일', '월', '화', '수', '목', '금', '토']

function pad(n: number) {
  return String(n).padStart(2, '0')
}
function ymdOf(d: Date) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
function parseYMD(s: string | null): Date | null {
  if (!s) return null
  const [y, m, d] = s.split('-').map(Number)
  if (!y || !m || !d) return null
  return new Date(y, m - 1, d)
}
function fmtSlash(s: string) {
  return s.replace(/-/g, '/')
}
function buildMonth(year: number, month: number) {
  const first = new Date(year, month, 1)
  const gridStart = new Date(year, month, 1 - first.getDay())
  const cells: { date: Date; inMonth: boolean }[] = []
  for (let i = 0; i < 42; i++) {
    const dt = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + i)
    cells.push({ date: dt, inMonth: dt.getMonth() === month })
  }
  return cells
}

function MonthGrid({
  year,
  month,
  draftStart,
  draftEnd,
  today,
  onPick,
}: {
  year: number
  month: number
  draftStart: string | null
  draftEnd: string | null
  today: string
  onPick: (ymd: string) => void
}) {
  const cells = buildMonth(year, month)
  return (
    <div className="w-[224px]">
      <div className="grid grid-cols-7 mb-1">
        {WEEKDAYS.map((w) => (
          <div key={w} className="h-7 flex items-center justify-center text-xs text-neutral-400">
            {w}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-y-0.5">
        {cells.map(({ date, inMonth }, i) => {
          const ymd = ymdOf(date)
          const isStart = draftStart && ymd === draftStart
          const isEnd = draftEnd && ymd === draftEnd
          const inRange = draftStart && draftEnd && ymd >= draftStart && ymd <= draftEnd
          const isEndpoint = isStart || isEnd
          const isToday = ymd === today
          return (
            <button
              key={i}
              type="button"
              onClick={() => onPick(ymd)}
              className={[
                'h-8 w-8 mx-auto flex items-center justify-center text-sm rounded-md transition-colors',
                !inMonth ? 'text-neutral-300' : 'text-neutral-800',
                isEndpoint
                  ? 'bg-[#1677ff] text-white font-medium'
                  : inRange
                    ? 'bg-[#e8f0fe]'
                    : 'hover:bg-neutral-100',
                !isEndpoint && isToday ? 'ring-1 ring-[#1677ff]/40' : '',
              ].join(' ')}
            >
              {date.getDate()}
            </button>
          )
        })}
      </div>
    </div>
  )
}

export function DateRangePicker({ start, end, onApply }: Props) {
  const [open, setOpen] = useState(false)
  const [draftStart, setDraftStart] = useState<string | null>(start)
  const [draftEnd, setDraftEnd] = useState<string | null>(end)
  const [view, setView] = useState<Date>(() => {
    const base = parseYMD(start) ?? new Date()
    return new Date(base.getFullYear(), base.getMonth(), 1)
  })
  const ref = useRef<HTMLDivElement>(null)
  const today = ymdOf(new Date())

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) close()
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [start, end])

  const openPicker = () => {
    setDraftStart(start)
    setDraftEnd(end)
    const base = parseYMD(start) ?? new Date()
    setView(new Date(base.getFullYear(), base.getMonth(), 1))
    setOpen(true)
  }

  const close = () => {
    setOpen(false)
    setDraftStart(start)
    setDraftEnd(end)
  }

  const pick = (ymd: string) => {
    if (!draftStart || (draftStart && draftEnd)) {
      setDraftStart(ymd)
      setDraftEnd(null)
    } else if (ymd < draftStart) {
      setDraftStart(ymd)
      setDraftEnd(null)
    } else {
      setDraftEnd(ymd)
    }
  }

  const apply = () => {
    if (!draftStart) {
      onApply(null, null)
    } else {
      onApply(draftStart, draftEnd ?? draftStart)
    }
    setOpen(false)
  }

  const clear = () => {
    setDraftStart(null)
    setDraftEnd(null)
  }

  const leftYear = view.getFullYear()
  const leftMonth = view.getMonth()
  const right = new Date(leftYear, leftMonth + 1, 1)
  const label = start && end ? `${fmtSlash(start)} - ${fmtSlash(end)}` : '검색기간'
  const footerText = draftStart
    ? `${fmtSlash(draftStart)} - ${fmtSlash(draftEnd ?? draftStart)}`
    : '기간을 선택하세요'

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => (open ? close() : openPicker())}
        className="h-9 px-3 rounded-md border border-neutral-300 text-sm bg-white hover:bg-neutral-50 inline-flex items-center gap-2 min-w-[200px] justify-between"
      >
        <span className={start && end ? 'text-neutral-800' : 'text-neutral-400'}>{label}</span>
        <CalendarIcon className="w-4 h-4 text-neutral-400" />
      </button>
      {open && (
        <div className="absolute left-0 mt-1 z-30 bg-white border border-neutral-200 rounded-lg shadow-xl p-4">
          <div className="flex gap-6">
            <div>
              <div className="flex items-center justify-between mb-2 px-1">
                <button
                  type="button"
                  onClick={() => setView(new Date(leftYear, leftMonth - 1, 1))}
                  className="p-1 rounded hover:bg-neutral-100 text-neutral-500"
                >
                  <ChevronLeft className="w-4 h-4" />
                </button>
                <span className="text-sm font-medium text-neutral-800">{leftMonth + 1}월 {leftYear}</span>
                <span className="w-6" />
              </div>
              <MonthGrid
                year={leftYear}
                month={leftMonth}
                draftStart={draftStart}
                draftEnd={draftEnd}
                today={today}
                onPick={pick}
              />
            </div>
            <div>
              <div className="flex items-center justify-between mb-2 px-1">
                <span className="w-6" />
                <span className="text-sm font-medium text-neutral-800">{right.getMonth() + 1}월 {right.getFullYear()}</span>
                <button
                  type="button"
                  onClick={() => setView(new Date(leftYear, leftMonth + 1, 1))}
                  className="p-1 rounded hover:bg-neutral-100 text-neutral-500"
                >
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
              <MonthGrid
                year={right.getFullYear()}
                month={right.getMonth()}
                draftStart={draftStart}
                draftEnd={draftEnd}
                today={today}
                onPick={pick}
              />
            </div>
          </div>
          <div className="flex items-center justify-between mt-3 pt-3 border-t border-neutral-100">
            <span className="text-xs text-neutral-500">{footerText}</span>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={clear}
                className="h-8 px-3 rounded-md text-xs text-neutral-500 hover:bg-neutral-100"
              >
                지우기
              </button>
              <button
                type="button"
                onClick={close}
                className="h-8 px-3 rounded-md border border-neutral-300 text-xs hover:bg-neutral-50"
              >
                취소
              </button>
              <button
                type="button"
                onClick={apply}
                className="h-8 px-3 rounded-md bg-[#1677ff] text-white text-xs hover:bg-[#1677ff]/90"
              >
                적용
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
