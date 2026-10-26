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
  /**
   * 화면 상단 띠·확인 창에 쓰는 게임 기본색. 운영 콘솔 "설정 › 콘솔 표시"에서 바꾸면 그 값이 이긴다(lib/projectLook). 이름(label)도 같다.
   * 게임끼리 확실히 달라야 하고, 흰 글자가 얹히므로 흰색과 4.5:1 이상 대비가 나야 한다.
   */
  color: string
}

const env = import.meta.env as Record<string, string | undefined>

export const PROJECTS: ProjectConfig[] = [
  { key: 'defencer', label: '마왕은 힘들어', url: env['VITE_DEFENCER_URL'] ?? '', color: '#7c3aed' },
  { key: 'devilslayer', label: '데빌 슬레이어', url: env['VITE_DEVILSLAYER_URL'] ?? '', color: '#c2410c' },
]

const STORAGE_KEY = 'tb_project_target'

const isTarget = (v: string | null): v is ProjectTarget => PROJECTS.some((p) => p.key === v)

// 탭마다 다른 게임을 열 수 있으므로 지금 게임은 탭 저장소(sessionStorage)가 정한다.
// 브라우저 공용 저장소는 "새 탭을 열 때 처음 고를 게임"으로만 쓴다 — 공용 값을 읽으면
// 다른 탭에서 고른 게임이 이 탭의 확인 창·새로고침에 섞여 든다.
export function getTarget(): ProjectTarget {
  try {
    const tab = window.sessionStorage.getItem(STORAGE_KEY)
    if (isTarget(tab)) return tab
    const last = window.localStorage.getItem(STORAGE_KEY)
    if (isTarget(last)) return last
  } catch {
    // 저장소가 막힌 브라우저는 첫 게임으로.
  }
  return PROJECTS[0]!.key
}

export function setTarget(t: ProjectTarget): void {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, t)
    window.localStorage.setItem(STORAGE_KEY, t)
  } catch {
    // 저장이 막혀도 이번 화면에서는 App 상태가 게임을 들고 있다.
  }
}

export function getProject(t: ProjectTarget): ProjectConfig {
  const found = PROJECTS.find((p) => p.key === t)
  if (!found) throw new Error(`알 수 없는 프로젝트: ${t}`)
  return found
}
