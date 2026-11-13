/** 붙어 있는 작은 버튼 묶음 — 카드 안에서 하나를 고를 때(위쪽 큰 탭과 겹쳐 보이지 않게). */
export function Segmented<T extends string>({ value, onChange, options }: { value: T; onChange: (v: T) => void; options: { v: T; label: string }[] }) {
  return (
    // 좁은 화면에서는 버튼 여백을 줄이고, 그래도 넘치면 묶음 안에서 옆으로 밀린다(화면 전체가 밀리지 않게).
    <div className="inline-flex max-w-full overflow-x-auto rounded-lg bg-neutral-100 p-0.5">
      {options.map((o) => (
        <button key={o.v} type="button" onClick={() => onChange(o.v)}
          className={['shrink-0 px-3 sm:px-4 py-1.5 rounded-md text-sm transition-colors whitespace-nowrap', value === o.v ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'].join(' ')}>
          {o.label}
        </button>
      ))}
    </div>
  )
}
