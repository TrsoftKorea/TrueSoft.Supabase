// user_profiles.platform·signup_platform 에 실제로 들어오는 값. 우편 발송과 통계 › 접속의 가입 플랫폼 거르기가 쓴다.
export const PLATFORMS = [
  { v: 'android', label: 'Android' },
  { v: 'ios', label: 'iOS' },
  { v: 'windows', label: 'Windows' },
] as const

export function platformLabel(v: string | null | undefined): string {
  if (!v) return ''
  return PLATFORMS.find((p) => p.v === v)?.label ?? v
}
