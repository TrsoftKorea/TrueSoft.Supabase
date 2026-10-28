import type { ButtonHTMLAttributes } from 'react'

/**
 * 공용 버튼. 테이블 좁은 칸에서 버튼 글자가 두 줄로 깨지는 버그(하루에 세 번 반복)가 여기 한 곳에서 막히면,
 * 페이지마다 whitespace-nowrap 을 깜빡할 일이 구조적으로 없어진다. 역할별 모양(입력·바꾸기·지우기)과
 * 버튼을 담은 칸의 규칙은 .claude/rules/console.md 「버튼」.
 */

// danger 는 평소엔 outline 과 같고 마우스를 올려야 빨갛다. dangerOutline 은 늘 빨개서 옆 버튼과 한눈에 갈린다.
type Variant = 'primary' | 'outline' | 'danger' | 'dangerOutline'
type Size = 'md' | 'compact' | 'sm'

const VARIANT_CLS: Record<Variant, string> = {
  primary: 'border border-transparent bg-[#1677ff] text-white hover:bg-[#1677ff]/90 disabled:opacity-50',
  outline: 'border border-neutral-300 text-neutral-600 bg-white hover:bg-neutral-50 disabled:opacity-50',
  danger: 'border border-neutral-300 text-neutral-600 bg-white hover:bg-red-50 hover:text-red-600 disabled:opacity-40',
  dangerOutline: 'border border-red-200 text-red-600 bg-white hover:bg-red-50 disabled:opacity-40',
}

const SIZE_CLS: Record<Size, string> = {
  md: 'h-9 px-4 gap-1.5 text-sm rounded-md',
  compact: 'h-9 px-3 gap-1.5 text-sm rounded-md',
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
