import type { ReactNode } from 'react'

export function WhiteCard({
  children,
  className = '',
}: {
  children: ReactNode
  className?: string
}) {
  return (
    <div
      className={`bg-white rounded-lg border border-neutral-200 shadow-[0_1px_2px_rgba(0,0,0,0.04)] ${className}`}
    >
      {children}
    </div>
  )
}

/**
 * 카드 맨 위 제목과 설명. 카드는 전부 이것을 쓴다 — 화면마다 굵기가 달라지지 않게. 오른쪽 버튼은 actions.
 * 설명은 제목만으로 뜻이 분명하지 않을 때만 넣는다. 좁은 화면에서는 버튼이 제목 아래로 내려간다.
 */
export function CardHeader({ title, description, actions }: { title?: ReactNode; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between sm:gap-3">
      <div className="min-w-0">
        {/* 제목 16px 굵게·설명 14px — 본문(14px)과 제목이 한눈에 갈리게(2026-11-10 디자인 견본). */}
        {title && <h2 className="text-base font-semibold text-neutral-900">{title}</h2>}
        {description && <p className="mt-1 text-sm text-neutral-500">{description}</p>}
      </div>
      {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
    </div>
  )
}

/**
 * 묶음 목록의 상자 하나 — 테두리 + 옅은 바탕 제목 띠(설정 › 서비스 연결 꼴). 상자끼리 칸 폭 맞추기는 안에 든 표가 한다.
 * 제목 띠 오른쪽 조작은 actions. 내용이 없으면(접힌 상자) 제목 띠만 남는다.
 */
export function GroupBox({ title, actions, children }: { title: ReactNode; actions?: ReactNode; children?: ReactNode }) {
  return (
    <section className="rounded-lg border border-neutral-200 overflow-hidden">
      <div className="flex items-center justify-between gap-3 px-4 py-2 bg-neutral-50 border-b border-neutral-200 last:border-b-0">
        <h3 className="text-xs font-semibold text-neutral-700">{title}</h3>
        {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
      </div>
      {children}
    </section>
  )
}

/**
 * 묶음 상자 안의 줄 목록. 머리 줄이 없고 칸 폭이 고정이라, 여러 상자에 나눠 담아도 상태·버튼이 위아래로 줄을 선다.
 * 칸: 이름 · 값 · 상태(status 일 때만) · 버튼. 줄(<tr>)은 화면이 짠다 — 상자마다 들여 쓴 소제목 줄이 들어가기도 한다.
 */
export function GroupTable({ status = false, children }: { status?: boolean; children: ReactNode }) {
  return (
    <table className="w-full text-sm table-fixed">
      <colgroup>
        <col className="w-[34%]" />
        <col />
        {status && <col className="w-[76px]" />}
        <col className="w-[164px]" />
      </colgroup>
      <tbody>{children}</tbody>
    </table>
  )
}
