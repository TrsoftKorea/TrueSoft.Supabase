// 프로젝트(게임) ↔ 백엔드 주소 매핑의 단일 소스.
// 새 프로젝트 추가 시 여기만 수정: union 에 1개 + PROJECTS 에 1줄 + .env 에 1줄.
//
// publishable 키는 필요 없다. 운영자 인증은 구글이 하고, 백엔드 호출은 Edge Function 주소로
// 직접 나간다 — 브라우저가 Supabase 키를 가질 이유가 없다.
export type ProjectTarget = 'defencer' | 'devilslayer'

export type ProjectConfig = {
  key: ProjectTarget
  label: string
  url: string
}

const env = import.meta.env as Record<string, string | undefined>

export const PROJECTS: ProjectConfig[] = [
  { key: 'defencer', label: '마왕은 힘들어', url: env['VITE_DEFENCER_URL'] ?? '' },
  { key: 'devilslayer', label: '데빌 슬레이어', url: env['VITE_DEVILSLAYER_URL'] ?? '' },
]

const STORAGE_KEY = 'tb_project_target'

export function getTarget(): ProjectTarget {
  const v = window.localStorage.getItem(STORAGE_KEY)
  return v === 'devilslayer' ? 'devilslayer' : 'defencer'
}

export function setTarget(t: ProjectTarget): void {
  window.localStorage.setItem(STORAGE_KEY, t)
}

export function getProject(t: ProjectTarget): ProjectConfig {
  const found = PROJECTS.find((p) => p.key === t)
  if (!found) throw new Error(`알 수 없는 프로젝트: ${t}`)
  return found
}
