import { useSyncExternalStore } from 'react'

// 화면 모드는 보는 사람의 취향이라 게임별이 아니라 브라우저마다 기억한다.
// index.html 의 첫 스크립트도 같은 키를 읽어, 그리기 전에 html.dark 를 붙인다(밝은 화면이 번쩍이지 않게).
const STORAGE_KEY = 'tb_theme'

export type ThemeMode = 'system' | 'light' | 'dark'

function read(): ThemeMode {
  try {
    const v = window.localStorage.getItem(STORAGE_KEY)
    return v === 'light' || v === 'dark' ? v : 'system'
  } catch {
    return 'system'
  }
}

let mode: ThemeMode = read()
const listeners = new Set<() => void>()
const media = window.matchMedia('(prefers-color-scheme: dark)')

let dark = false

function apply() {
  dark = mode === 'dark' || (mode === 'system' && media.matches)
  document.documentElement.classList.toggle('dark', dark)
}

// 시스템 설정을 따르는 중이면 OS 가 바뀔 때도 다시 그리게 알린다(게임 색처럼 화면 모드로 값을 고르는 곳이 있다).
media.addEventListener('change', () => {
  if (mode !== 'system') return
  apply()
  listeners.forEach((l) => l())
})
apply()

/** 지금 어두운 화면인지. */
export const isDarkMode = () => dark

/** 화면 모드가 바뀌면 다시 그리게 한다. */
export function subscribeTheme(l: () => void): () => void {
  listeners.add(l)
  return () => listeners.delete(l)
}

export function setThemeMode(next: ThemeMode) {
  mode = next
  try {
    if (next === 'system') window.localStorage.removeItem(STORAGE_KEY)
    else window.localStorage.setItem(STORAGE_KEY, next)
  } catch {
    // 저장이 막힌 브라우저면 이번 실행 동안만 적용한다.
  }
  apply()
  listeners.forEach((l) => l())
}

export function useThemeMode(): ThemeMode {
  return useSyncExternalStore(
    (l) => {
      listeners.add(l)
      return () => listeners.delete(l)
    },
    () => mode,
  )
}
