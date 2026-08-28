import type { ButtonHTMLAttributes } from 'react'

/**
 * 공용 버튼. 오늘만 세 번 반복된 버그(테이블 좁은 칸에서 버튼 글자가 두 줄로
 * 깨짐)가 여기 한 곳에서만 막히면, 페이지마다 whitespace-nowrap을 깜빡할 일이
 * 구조적으로 없어진다 — truebase-button-text-wrap-check 메모리 참고.
 *
 * 지금은 Operators.tsx만 이 컴포넌트를 쓴다. 나머지 페이지는 점진적으로 옮긴다.
 */

type Variant = 'primary' | 'outline' | 'danger'
type Size = 'md' | 'sm'

const VARIANT_CLS: Record<Variant, string> = {
  primary: 'border border-transparent bg-[#1677ff] text-white hover:bg-[#1677ff]/90 disabled:opacity-50',
  outline: 'border border-neutral-300 text-neutral-600 bg-white hover:bg-neutral-50 disabled:opacity-50',
  danger: 'border border-neutral-300 text-neutral-600 bg-white hover:bg-red-50 hover:text-red-600 disabled:opacity-40',
}

const SIZE_CLS: Record<Size, string> = {
  md: 'h-9 px-4 gap-1.5 text-sm rounded-md',
  sm: 'px-2 py-1 gap-1 text-xs rounded',
}

export function Button({
  variant = 'outline',
  size = 'md',
  className = '',
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: Size }) {
  return (
    <button
      className={`inline-flex items-center justify-center whitespace-nowrap ${SIZE_CLS[size]} ${VARIANT_CLS[variant]} ${className}`}
      {...rest}
    />
  )
}
