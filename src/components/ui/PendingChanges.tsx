export type PendingItem = { id: number; label: string; detail?: string }

// 지금 보고 있는 화면과 관련된 미게시(대기) 변경만 한 줄씩 보여준다. 없으면 렌더링 안 함.
// 전체 건수 안내와 게시 동선은 전역 상단 배너가 담당하므로 여기서는 내역과 취소만 제공한다.
export function PendingChanges({
  items,
  onDiscard,
  busy,
}: {
  items: PendingItem[]
  onDiscard: (id: number) => void
  busy?: boolean
}) {
  if (items.length === 0) return null
  return (
    <div className="rounded-md border border-amber-200 bg-amber-50 divide-y divide-amber-100">
      {items.map((it) => (
        <div key={it.id} className="flex items-center gap-2 px-4 py-2 text-xs text-amber-800">
          <span className="font-medium">{it.label}</span>
          {it.detail && <span className="font-mono text-amber-700/80">{it.detail}</span>}
          <button
            onClick={() => onDiscard(it.id)}
            disabled={busy}
            title="이 변경을 대기열에서 없앱니다. 게시되지 않습니다."
            className="ml-auto shrink-0 rounded border border-amber-300 px-2 py-0.5 text-amber-700 hover:bg-amber-100 disabled:opacity-50"
          >
            변경 취소
          </button>
        </div>
      ))}
    </div>
  )
}
