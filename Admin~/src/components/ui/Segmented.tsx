/** 붙어 있는 작은 버튼 묶음 — 카드 안에서 하나를 고를 때(위쪽 큰 탭과 겹쳐 보이지 않게). */
export function Segmented<T extends string>({ value, onChange, options }: { value: T; onChange: (v: T) => void; options: { v: T; label: string }[] }) {
  return (
    <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
      {options.map((o) => (
        <button key={o.v} type="button" onClick={() => onChange(o.v)}
          className={['px-4 py-1.5 rounded-md text-sm transition-colors whitespace-nowrap', value === o.v ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'].join(' ')}>
          {o.label}
        </button>
      ))}
    </div>
  )
}
