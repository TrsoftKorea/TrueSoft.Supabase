import { Monitor, Moon, Sun } from 'lucide-react'
import { setThemeMode, useThemeMode, type ThemeMode } from '../lib/theme'

const NEXT: Record<ThemeMode, ThemeMode> = { system: 'light', light: 'dark', dark: 'system' }
const LABEL: Record<ThemeMode, string> = { system: '기기 설정 따름', light: '밝은 화면', dark: '어두운 화면' }
const ICON = { system: Monitor, light: Sun, dark: Moon }

/** 누를 때마다 기기 설정 → 밝은 화면 → 어두운 화면 순으로 바뀐다. 이 브라우저에만 적용된다. */
export function ThemeToggle({ className = '', compact = false }: { className?: string; compact?: boolean }) {
  const mode = useThemeMode()
  const Icon = ICON[mode]
  return (
    <button
      type="button"
      onClick={() => setThemeMode(NEXT[mode])}
      title={`화면: ${LABEL[mode]} (누르면 ${LABEL[NEXT[mode]]})`}
      aria-label={`화면 모드: ${LABEL[mode]}`}
      className={`inline-flex items-center gap-1.5 h-8 px-2.5 rounded-md border text-sm whitespace-nowrap ${className}`}
    >
      <Icon className="w-3.5 h-3.5" />
      {!compact && LABEL[mode]}
    </button>
  )
}
