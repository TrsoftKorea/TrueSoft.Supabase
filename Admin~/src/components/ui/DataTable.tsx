import { Fragment, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { TableStatusRow } from './TableStatusRow'
import { CardHeader, WhiteCard } from './Card'

/**
 * 칸 종류. 종류가 폭·정렬·줄바꿈을 정한다 — 화면마다 칸 폭을 손으로 박으면 내용이 길어질 때 두 줄로 깨지거나
 * (2026-11-10 이름 "이주/형"), 내용이 짧을 때 칸 사이가 휑하게 벌어진다.
 *  - name     이름·닉네임·이메일·상품 이름. 줄바꿈 없이 내용 길이만큼.
 *  - mono     계정 ID·주문번호처럼 글자 하나하나가 중요한 값. 고정폭 글꼴, 줄바꿈 없음. 이메일은 name(고정폭은 폭을 더 먹는다).
 *  - datetime 날짜·시각. 자릿수가 맞게 고정폭 숫자, 줄바꿈 없음.
 *  - number   개수·금액. 오른쪽 정렬, 고정폭 숫자(자릿수가 위아래로 맞는다), 줄바꿈 없음.
 *  - status   상태 글자(StatusText). 줄바꿈 없음.
 *  - text     제목·메모처럼 길이를 모르는 글. 남는 폭을 이 칸이 가져가고(최소 16rem), 넘치면 "…"로 줄이고 올리면 전체가 보인다.
 *  - actions  버튼. 오른쪽 끝, 줄바꿈 없음.
 * 표는 혼자 쓰지 말고 TableCard 에 담는다 — text 칸이 없는 표는 카드째 내용 폭으로 줄어야 오른쪽이 비지 않는다.
 */
export type ColumnKind = 'name' | 'mono' | 'datetime' | 'number' | 'status' | 'text' | 'actions'

export type Column<T> = {
  key: string
  header: ReactNode
  kind: ColumnKind
  cell: (row: T) => ReactNode
  /** text 칸에서 "…"로 잘렸을 때 마우스를 올리면 보일 전체 글. cell 이 글자 하나면 생략해도 그 글이 보인다. */
  full?: (row: T) => string
}

const HEAD: Record<ColumnKind, string> = {
  name: 'text-left',
  mono: 'text-left',
  datetime: 'text-left',
  number: 'text-right',
  status: 'text-left',
  // 최소 폭은 머리 칸에 둔다(안쪽 글 상자에 두면 무시된다) — 창이 좁으면 이 칸이 "…"만 남기 전에 표가 옆으로 밀린다.
  text: 'text-left w-full min-w-[16rem]',
  actions: 'text-right',
}

const CELL: Record<ColumnKind, string> = {
  name: 'whitespace-nowrap text-neutral-900',
  mono: 'whitespace-nowrap font-mono text-[13px] text-neutral-700',
  datetime: 'whitespace-nowrap tabular-nums text-neutral-600',
  number: 'whitespace-nowrap text-right tabular-nums text-neutral-900',
  status: 'whitespace-nowrap',
  // max-w-0 + w-full: 남는 폭을 다 가져가되 내용 길이로 표를 밀어내지 않는다 — 그래야 안쪽 truncate 가 먹는다.
  text: 'w-full max-w-0 text-neutral-900',
  actions: 'whitespace-nowrap text-right',
}

const isEmpty = (v: ReactNode) => v === null || v === undefined || v === '' || v === false

/** 빈 값 표시는 하나로 — 회색 "-". */
export const EMPTY_MARK = <span className="text-neutral-400">-</span>

export type DataTableProps<T> = {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  loading?: boolean
  failed?: boolean
  emptyText?: string
  /** 줄을 누르면 할 일(상세로 가기 등). expand 와 함께 주면 expand 가 이긴다. */
  onRowClick?: (row: T) => void
  /** 줄을 누르면 그 아래로 펼칠 내용. null 이면 그 줄은 펼칠 것이 없다(화살표도 안 보인다). 한 번에 한 줄만 펼친다. */
  expand?: (row: T) => ReactNode
}

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  loading = false,
  failed = false,
  emptyText,
  onRowClick,
  expand,
}: DataTableProps<T>) {
  const fill = columns.some((c) => c.kind === 'text')
  const [openKey, setOpenKey] = useState<string | null>(null)
  const tableRef = useRef<HTMLTableElement>(null)

  // 내용 폭 표는 한 번 넓어지면 줄지 않는다 — 쪽을 넘기거나 다시 불러올 때마다 이름 길이에 따라 카드 폭이
  // 흔들리지 않게(2026-11-10 실측: 이름 하나가 길어지자 942→982px). 화면을 다시 열면 처음부터 잰다.
  useLayoutEffect(() => {
    const t = tableRef.current
    if (fill || !t || typeof ResizeObserver === 'undefined') return
    let widest = 0
    const ro = new ResizeObserver(() => {
      const w = Math.ceil(t.getBoundingClientRect().width)
      if (w > widest) {
        widest = w
        t.style.minWidth = `max(100%, ${w}px)`
      }
    })
    ro.observe(t)
    return () => ro.disconnect()
  }, [fill])

  const span = columns.length + (expand ? 1 : 0)
  return (
    <div className="overflow-x-auto">
      <table ref={tableRef} className="min-w-full text-sm">
        <thead className="bg-neutral-50 text-xs text-neutral-500">
          <tr>
            {expand && <th className="w-8 pl-3 pr-0" aria-label="펼치기" />}
            {columns.map((c) => (
              <th key={c.key} className={`px-4 py-2.5 font-medium whitespace-nowrap ${HEAD[c.kind]}`}>{c.header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {loading || failed || rows.length === 0 ? (
            <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={span} {...(emptyText !== undefined ? { emptyText } : {})} />
          ) : rows.map((r) => {
            const key = rowKey(r)
            const detail = expand ? expand(r) : null
            const canOpen = !isEmpty(detail)
            const open = canOpen && openKey === key
            const click = canOpen ? () => setOpenKey(open ? null : key) : onRowClick && !expand ? () => onRowClick(r) : undefined
            return (
              <Fragment key={key}>
                <tr onClick={click} className={`border-t border-neutral-100 ${click ? 'cursor-pointer hover:bg-neutral-50' : ''}`}>
                  {expand && (
                    <td className="w-8 pl-3 pr-0 text-neutral-400">
                      {canOpen && (open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />)}
                    </td>
                  )}
                  {columns.map((c) => {
                    const v = c.cell(r)
                    return (
                      <td key={c.key} className={`px-4 py-2.5 ${CELL[c.kind]}`}>
                        {isEmpty(v) ? EMPTY_MARK : c.kind === 'text'
                          ? <div className="truncate" title={c.full ? c.full(r) : typeof v === 'string' ? v : undefined}>{v}</div>
                          : v}
                      </td>
                    )
                  })}
                </tr>
                {open && (
                  <tr className="bg-neutral-50">
                    <td colSpan={span} className="px-4 py-3 pl-11 text-sm">{detail}</td>
                  </tr>
                )}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/**
 * 표 하나를 담는 카드. 제목·버튼(위)과 쪽 넘김(footer, 아래)을 함께 둔다.
 * text 칸이 없는 표는 카드째 내용 폭으로 줄어든다 — 표만 줄이면 카드 오른쪽이 텅 빈다.
 * 같은 화면에 위아래로 쌓인 표 카드는 CardStack 에 넣어 폭을 맞춘다.
 */
export function TableCard<T>({
  title,
  description,
  actions,
  footer,
  ...table
}: DataTableProps<T> & { title?: ReactNode; description?: ReactNode; actions?: ReactNode; footer?: ReactNode }) {
  const fill = table.columns.some((c) => c.kind === 'text')
  return (
    <WhiteCard className={`overflow-hidden ${fill ? '' : 'w-fit max-w-full'}`}>
      {(title || actions) && (
        <div className="p-5 pb-3">
          <CardHeader title={title} description={description} actions={actions} />
        </div>
      )}
      <DataTable {...table} />
      {footer}
    </WhiteCard>
  )
}

/** 위아래로 쌓인 표 카드의 폭을 가장 넓은 것에 맞춘다. text 칸이 있는(전체 폭) 카드는 넣지 않는다. */
export function CardStack({ children }: { children: ReactNode }) {
  return <div className="flex w-fit max-w-full flex-col gap-4 [&>*]:w-full">{children}</div>
}
