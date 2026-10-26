// user_profiles.platform 에 실제로 들어오는 값. 우편 발송을 이 값으로 좁힌다.
export const PLATFORMS = [
  { v: 'android', label: 'Android' },
  { v: 'ios', label: 'iOS' },
  { v: 'windows', label: 'Windows' },
] as const

export function platformLabel(v: string | null | undefined): string {
  if (!v) return ''
  return PLATFORMS.find((p) => p.v === v)?.label ?? v
}
