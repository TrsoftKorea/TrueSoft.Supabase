import type { ReactNode } from 'react'

/**
 * 모든 페이지 상단의 제목·부제목을 동일한 크기·간격으로 렌더링합니다.
 * 오른쪽에 버튼이 필요한 페이지는 actions 슬롯을 사용하세요.
 */
export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string
  description?: string
  actions?: ReactNode
}) {
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0">
        <h2 className="text-2xl font-semibold text-neutral-900">{title}</h2>
        {description && <p className="text-sm text-neutral-500 mt-1">{description}</p>}
      </div>
      {actions && <div className="shrink-0">{actions}</div>}
    </div>
  )
}
