import { useSyncExternalStore } from 'react'
import { callAdminPublic } from './api'
import { getProject, PROJECTS, type ProjectTarget } from './projectTarget'

// 게임마다 운영 콘솔에서 쓰는 색. 각 게임 DB(ts_admin_console_settings)에 저장되고, 정하지 않았으면
// projectTarget 의 기본색을 쓴다. 다음에 열 때 기본색이 잠깐 비치지 않도록 마지막 값을 브라우저에 기억해 둔다.
const STORAGE_KEY = 'tb_project_colors'

type Colors = Partial<Record<ProjectTarget, string>>

function load(): Colors {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Colors) : {}
  } catch {
    return {}
  }
}

let colors: Colors = load()
const listeners = new Set<() => void>()

function update(next: Colors) {
  colors = next
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(colors))
  } catch {
    // 저장이 막힌 브라우저면 이번 실행 동안만 기억한다.
  }
  listeners.forEach((l) => l())
}

/** 이 게임의 콘솔 색. 아직 못 받았거나 정하지 않았으면 기본색. */
export function projectColor(target: ProjectTarget): string {
  return colors[target] ?? getProject(target).color
}

/** 색 설정을 저장한 직후 화면에 바로 반영한다. null 이면 기본색으로. */
export function setProjectColor(target: ProjectTarget, color: string | null) {
  const next = { ...colors }
  if (color) next[target] = color
  else delete next[target]
  update(next)
}

/** 모든 게임의 색을 서버에서 다시 받는다. 한 게임이 응답하지 않아도 나머지는 반영한다. */
export async function refreshProjectColors(): Promise<void> {
  const results = await Promise.all(
    PROJECTS.map(async (p) => {
      try {
        const res = await callAdminPublic<{ color: string | null }>(p.key, 'auth.consoleColor')
        return [p.key, res.color] as const
      } catch {
        return [p.key, undefined] as const
      }
    }),
  )
  const next = { ...colors }
  for (const [key, color] of results) {
    if (color === undefined) continue // 못 받은 게임은 기억해 둔 값을 그대로 둔다
    if (color) next[key] = color
    else delete next[key]
  }
  update(next)
}

/** 색이 바뀌면 다시 그리게 한다. 돌려준 함수로 게임별 색을 읽는다. */
export function useProjectColor(): (target: ProjectTarget) => string {
  useSyncExternalStore(
    (l) => {
      listeners.add(l)
      return () => listeners.delete(l)
    },
    () => colors,
  )
  return projectColor
}
