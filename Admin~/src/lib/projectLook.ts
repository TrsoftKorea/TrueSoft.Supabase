import { useSyncExternalStore } from 'react'
import { callAdminPublic } from './api'
import { getProject, PROJECTS, type ProjectTarget } from './projectTarget'

// 게임마다 운영 콘솔에서 쓰는 색·이름·아이콘. 각 게임 DB(ts_admin_console_settings)에 저장되고, 정하지 않았으면
// projectTarget 의 기본값을 쓴다. 다음에 열 때 기본값이 잠깐 비치지 않도록 마지막 값을 브라우저에 기억해 둔다.
const STORAGE_KEY = 'tb_project_look'

export type ProjectLook = { color: string | null; name: string | null; icon: string | null }
type Looks = Partial<Record<ProjectTarget, ProjectLook>>

function load(): Looks {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Looks) : {}
  } catch {
    return {}
  }
}

let looks: Looks = load()
const listeners = new Set<() => void>()

function update(next: Looks) {
  looks = next
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(looks))
  } catch {
    // 저장이 막힌 브라우저면 이번 실행 동안만 기억한다.
  }
  listeners.forEach((l) => l())
}

/** 이 게임의 콘솔 색. 아직 못 받았거나 정하지 않았으면 기본색. */
export function projectColor(target: ProjectTarget): string {
  return looks[target]?.color ?? getProject(target).color
}

/** 콘솔에 보이는 게임 이름. 정하지 않았으면 기본 이름. */
export function projectName(target: ProjectTarget): string {
  return looks[target]?.name ?? getProject(target).label
}

/** 게임 아이콘 그림(data URL). 없으면 null — 화면은 이름 첫 글자로 대신한다. */
export function projectIcon(target: ProjectTarget): string | null {
  return looks[target]?.icon ?? null
}

/** 설정을 저장한 직후 화면에 바로 반영한다. */
export function setProjectLook(target: ProjectTarget, look: ProjectLook) {
  update({ ...looks, [target]: look })
}

type ServerLook = { color: string | null; display_name?: string | null; icon?: string | null }

export const toProjectLook = (s: ServerLook): ProjectLook => ({
  color: s.color ?? null,
  name: s.display_name ?? null,
  icon: s.icon ?? null,
})

/** 모든 게임의 표시 설정을 서버에서 다시 받는다. 한 게임이 응답하지 않아도 나머지는 반영한다. */
export async function refreshProjectLooks(): Promise<void> {
  const results = await Promise.all(
    PROJECTS.map(async (p) => {
      try {
        return [p.key, toProjectLook(await callAdminPublic<ServerLook>(p.key, 'auth.consoleLook'))] as const
      } catch {
        return [p.key, undefined] as const
      }
    }),
  )
  const next = { ...looks }
  for (const [key, look] of results) {
    if (look) next[key] = look // 못 받은 게임은 기억해 둔 값을 그대로 둔다
  }
  update(next)
}

/** 표시 설정이 바뀌면 다시 그리게 한다. */
export function useProjectLook(): { color: typeof projectColor; name: typeof projectName; icon: typeof projectIcon } {
  useSyncExternalStore(
    (l) => {
      listeners.add(l)
      return () => listeners.delete(l)
    },
    () => looks,
  )
  return { color: projectColor, name: projectName, icon: projectIcon }
}

/** 색만 쓰는 곳을 위한 줄임. */
export function useProjectColor(): (target: ProjectTarget) => string {
  return useProjectLook().color
}
