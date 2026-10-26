import { useEffect, type ReactNode } from 'react'
import { Loader2, X } from 'lucide-react'
import { WhiteCard } from './Card'
import { getProject, getTarget } from '../../lib/projectTarget'
import { useProjectLook } from '../../lib/projectLook'

/**
 * 되돌리기 어려운 동작 앞에 띄우는 확인 창.
 *
 * 브라우저 window.confirm 은 문구를 꾸밀 수 없어 쓰지 않습니다.
 * 무엇을 지우는지 children 으로 함께 보여주면 엉뚱한 행을 지우는 사고를 막을 수 있습니다.
 */
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = '확인',
  danger = false,
  busy = false,
  confirmDisabled = false,
  error,
  onConfirm,
  onCancel,
  children,
}: {
  open: boolean
  title: string
  description?: ReactNode
  confirmLabel?: string
  danger?: boolean
  busy?: boolean
  confirmDisabled?: boolean
  // 확인 뒤 실패한 사유. 페이지 위 배너는 이 창의 어두운 배경에 가려 보이지 않으므로 창 안에 띄운다.
  error?: string | undefined
  onConfirm: () => void
  onCancel: () => void
  children?: ReactNode
}) {
  // 진행 중에는 Esc 로 닫지 않는다. 요청은 이미 나갔는데 창만 사라지면 결과를 볼 수 없다.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, busy, onCancel])

  const look = useProjectLook()

  if (!open) return null

  // 확인 창은 어느 화면에서 열리든 지금 선택된 게임이 대상이다 — 화면마다 넘겨받지 않고 여기서 읽는다.
  const project = getProject(getTarget())

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/30 p-6 overflow-auto"
      onClick={() => {
        if (!busy) onCancel()
      }}
    >
      <div className="w-full max-w-md" onClick={(e) => e.stopPropagation()}>
        <WhiteCard className="p-5 space-y-4">
          <div className="flex items-center justify-between">
            <h3 className="text-base font-semibold text-neutral-900">{title}</h3>
            <button
              onClick={onCancel}
              disabled={busy}
              className="text-neutral-400 hover:text-neutral-700 disabled:opacity-40"
              aria-label="닫기"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          <div
            className="rounded-md px-3 py-2 text-sm font-medium text-white"
            style={{ backgroundColor: look.color(project.key) }}
          >
            대상 게임: {look.name(project.key)}
          </div>

          {children}

          {description && <div className="text-sm text-neutral-600">{description}</div>}

          {error && (
            <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>
          )}

          <div className="flex items-center gap-2 pt-1">
            <button
              onClick={onCancel}
              disabled={busy}
              className="h-9 px-4 rounded-md border border-neutral-300 text-sm text-neutral-700 disabled:opacity-40"
            >
              취소
            </button>
            <button
              onClick={onConfirm}
              disabled={busy || confirmDisabled}
              className={[
                'ml-auto inline-flex items-center gap-1.5 h-9 px-5 rounded-md text-white text-sm font-medium disabled:opacity-50',
                danger ? 'bg-red-600 hover:bg-red-700' : 'bg-[#1677ff] hover:bg-[#1677ff]/90',
              ].join(' ')}
            >
              {busy && <Loader2 className="w-4 h-4 animate-spin" />}
              {confirmLabel}
            </button>
          </div>
        </WhiteCard>
      </div>
    </div>
  )
}
