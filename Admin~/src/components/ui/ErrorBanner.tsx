import { AlertCircle, X } from 'lucide-react'

/**
 * 비동기 작업 실패를 알리는 화면 안 배너.
 *
 * 브라우저 alert 대신 쓴다 — 제목 줄에 주소가 그대로 나오고 문구도 꾸밀 수 없다. 확인 창(ConfirmDialog)과
 * 달리 응답을 기다리지 않는 일회성 알림이라 모달이 아니라 인라인 배너로 둔다.
 */
export function ErrorBanner({
  message,
  onDismiss,
}: {
  message: string | null
  onDismiss: () => void
}) {
  if (!message) return null
  return (
    <div className="flex items-start gap-2 rounded-md border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
      <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
      <span className="flex-1">{message}</span>
      <button
        onClick={onDismiss}
        className="shrink-0 text-red-400 hover:text-red-600"
        aria-label="닫기"
      >
        <X className="w-4 h-4" />
      </button>
    </div>
  )
}
