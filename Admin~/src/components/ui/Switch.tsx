/** 켜기/끄기 스위치. 이름이 옆에 보이지 않으면 label 로 읽는 이름을 준다. */
export function Switch({
  value,
  onChange,
  disabled,
  label,
}: {
  value: boolean
  onChange: (v: boolean) => void
  disabled?: boolean
  label?: string
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={value}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!value)}
      className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors disabled:opacity-50 ${
        value ? 'bg-[#1677ff]' : 'bg-neutral-300'
      }`}
    >
      {/* 손잡이는 어두운 화면에서도 흰색이다 — bg-white 는 어두운 화면에서 카드 바탕색으로 바뀌어 꺼진 회색 바탕과 안 갈린다. */}
      <span
        className={`inline-block h-4 w-4 transform rounded-full bg-[#ffffff] shadow-sm transition-transform ${
          value ? 'translate-x-4' : 'translate-x-0.5'
        }`}
      />
    </button>
  )
}
