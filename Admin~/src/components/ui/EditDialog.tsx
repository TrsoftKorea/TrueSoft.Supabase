import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { useEscapeToClose } from './useEscapeToClose'

/**
 * 값을 넣고 저장하는 편집 창(추가·수정·보내기). 되돌리기 어려운 한 번의 확인은 ConfirmDialog.
 * 제목 줄 · 본문 · 아래 버튼 줄의 꼴을 여기 한 곳에서 정한다 — 화면마다 폭·여백·닫는 방법이 갈라지지 않게.
 * 어두운 바깥을 눌러도 닫지 않는다 — 입력하던 값을 실수로 잃는다. 닫기는 X · 취소 · Esc.
 * 본문이 길면 본문만 스크롤되고 제목 줄과 아래 버튼 줄은 남는다.
 */
type Size = 'md' | 'lg' | 'xl'
const SIZE_CLS: Record<Size, string> = { md: 'max-w-md', lg: 'max-w-lg', xl: 'max-w-2xl' }

export function EditDialog({
  title,
  description,
  actions,
  size = 'md',
  busy = false,
  onClose,
  footer,
  error,
  layer = 'base',
  flush = false,
  bodyClassName = '',
  children,
}: {
  title: ReactNode
  // 제목 아래 작은 글씨 한 줄(누구의 값인지 등).
  description?: ReactNode
  // 제목 줄 오른쪽, 닫기 앞에 놓는 버튼.
  actions?: ReactNode
  size?: Size
  busy?: boolean
  onClose: () => void
  // 아래 버튼 줄. 취소 · 저장 순서로 넣는다.
  footer?: ReactNode
  // 저장 실패 사유. 아래 버튼 줄 바로 위에 붙어 본문을 스크롤해도 보인다.
  error?: string | undefined
  // 다른 편집 창 위에 겹쳐 띄우는 창.
  layer?: 'base' | 'top'
  // 본문 여백 없음 — 표를 창 가장자리까지 붙일 때.
  flush?: boolean
  // 본문 칸의 높이를 고정해야 할 때 등.
  bodyClassName?: string
  children: ReactNode
}) {
  useEscapeToClose(true, busy, onClose)

  // 문서 맨 바깥(body)에 띄운다 — 화면 안에 그대로 두면 부모의 간격(space-y 의 margin-top)이 어두운 배경에도 붙어
  // 화면 위쪽 16px 이 덮이지 않았다(2026-10-28 사용자 지적).
  return createPortal(
    <div className={`fixed inset-0 ${layer === 'top' ? 'z-[60]' : 'z-50'} bg-black/40 flex items-center justify-center p-4`}>
      <div className={`bg-white rounded-lg shadow-xl w-full ${SIZE_CLS[size]} max-h-[90vh] flex flex-col`}>
        <div className="flex items-start justify-between gap-3 px-5 py-4 border-b border-neutral-200 shrink-0">
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-neutral-900 truncate">{title}</h3>
            {description && <p className="mt-0.5 text-xs text-neutral-500">{description}</p>}
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {actions}
            <button
              onClick={onClose}
              disabled={busy}
              title="닫기"
              aria-label="닫기"
              className="p-0.5 text-neutral-400 hover:text-neutral-700 disabled:opacity-40"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>
        {/* flex-auto(기준 = 제 높이): flex-1 은 기준이 0 이라 bodyClassName 의 고정 높이가 무시된다. 창보다 길면 줄고 스크롤된다. */}
        <div className={`flex-auto min-h-0 overflow-auto ${flush ? '' : 'p-5 space-y-4'} ${bodyClassName}`}>{children}</div>
        {error && (
          <div className="px-5 py-2 border-t border-red-200 bg-red-50 text-sm text-red-700 shrink-0">{error}</div>
        )}
        {footer && (
          <div className="flex justify-end gap-2 px-5 py-3 border-t border-neutral-200 bg-neutral-50 shrink-0">{footer}</div>
        )}
      </div>
    </div>,
    document.body,
  )
}
