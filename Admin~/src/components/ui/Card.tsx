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

/** 카드 맨 위 제목과 한 줄 설명. 카드는 전부 이것을 쓴다 — 화면마다 굵기·설명 유무가 달라지지 않게. 오른쪽 버튼은 actions. */
export function CardHeader({ title, description, actions }: { title: ReactNode; description: ReactNode; actions?: ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <div className="min-w-0">
        <h2 className="text-sm font-medium text-neutral-900">{title}</h2>
        <p className="mt-1 text-xs text-neutral-500">{description}</p>
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
