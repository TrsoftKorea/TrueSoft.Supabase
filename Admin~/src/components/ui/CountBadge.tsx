/**
 * 메뉴 옆 둥근 숫자 표시. 18px 원 안에 "99+"까지 들어가야 해 글자는 네 단계보다 작은 11px 로 둔다 —
 * 글자 크기 검사(check-ui U21)가 이 파일만 예외로 본다.
 */
export function CountBadge({ count, tone = 'alert', className = '' }: { count: number; tone?: 'alert' | 'warn'; className?: string }) {
  return (
    <span className={`inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full text-[11px] font-semibold leading-none ${
      tone === 'alert' ? 'bg-red-500 text-white' : 'bg-amber-400 text-[#161a2b]'
    } ${className}`}>
      {count > 99 ? '99+' : count}
    </span>
  )
}
