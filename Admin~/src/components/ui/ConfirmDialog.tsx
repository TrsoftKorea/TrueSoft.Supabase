import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Loader2, X } from 'lucide-react'
import { WhiteCard } from './Card'
import { TargetGameStrip } from './TargetGameStrip'
import { useEscapeToClose } from './useEscapeToClose'

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
  showTarget = true,
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
  // 게임과 무관한 동작(콘솔 전체 게시 등)이면 "대상 게임" 띠를 감춘다 — 한 게임에만 걸리는 일로 오해하지 않게.
  showTarget?: boolean
}) {
  // 진행 중에는 Esc 로 닫지 않는다. 요청은 이미 나갔는데 창만 사라지면 결과를 볼 수 없다.
  useEscapeToClose(open, busy, onCancel)

  if (!open) return null

  // 대상 게임은 이 탭이 열고 있는 게임이다 — 화면마다 넘겨받지 않고 띠가 직접 읽는다.
  // 바깥(어두운 배경)을 눌러도 닫지 않는다 — 편집 창과 같은 규칙. 닫기는 X · 취소 · Esc.
  // 배경 색·위치도 편집 창과 같게, 문서 맨 바깥(body)에 띄운다 — 부모의 간격이 배경에 붙지 않게.
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4 overflow-auto">
      <div className="w-full max-w-md">
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

          {showTarget && <TargetGameStrip />}

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
    </div>,
    document.body,
  )
}
