// 프로젝트 ↔ 백엔드 주소 매핑의 단일 소스.
// 새 프로젝트(대기 프로젝트 포함)는 VITE_MORE_PROJECT_URLS(쉼표로 나눈 주소)에 더한다 — .env·배포 작업(admin-deploy.yml).
// 그 프로젝트의 키는 주소의 프로젝트 ref 다. 처음 두 게임은 이 방식 전부터 있어 키 이름을 그대로 둔다
// (브라우저에 남은 로그인 표·고른 게임이 그 키로 저장돼 있다).
// 대기 중인 프로젝트는 목록에 있어도 화면에 안 보인다 — 각 프로젝트 DB 가 정하고(projectLook 의 active),
// 마스터가 "새 프로젝트 만들기"에서 꺼낸다.
//
// publishable 키는 필요 없다. 운영자 인증은 구글이 하고, 백엔드 호출은 Edge Function 주소로
// 직접 나간다 — 브라우저가 Supabase 키를 가질 이유가 없다.
export type ProjectTarget = string

export type ProjectConfig = {
  key: ProjectTarget
  url: string
  /**
   * 화면 상단 띠·확인 창에 쓰는 게임 기본색. 운영 콘솔 "설정 › 프로젝트 정보"에서 바꾸면 그 값이 이긴다(lib/projectLook).
   * 이름은 기본값을 두지 않는다 — 운영자가 프로젝트 정보에서 정한 이름만 쓴다.
   * 흰 글자가 얹히므로 흰색과 4.5:1 이상 대비가 나야 한다.
   */
  color: string
}

const env = import.meta.env as Record<string, string | undefined>

// 추가 프로젝트의 기본색. 색은 콘솔 "설정 › 프로젝트 정보"에서 정한다.
const MORE_PROJECT_COLOR = '#0f766e'

function moreProjects(list: string): ProjectConfig[] {
  const out: ProjectConfig[] = []
  for (const raw of list.split(',')) {
    const url = raw.trim().replace(/\/+$/, '')
    if (!url) continue
    try {
      const key = new URL(url).hostname.split('.')[0]
      if (key && !out.some((p) => p.key === key)) out.push({ key, url, color: MORE_PROJECT_COLOR })
    } catch {
      // 주소 꼴이 아니면 뺀다 — 목록 하나가 틀렸다고 콘솔 전체가 안 뜨면 안 된다.
    }
  }
  return out
}

export const PROJECTS: ProjectConfig[] = [
  { key: 'defencer', url: env['VITE_DEFENCER_URL'] ?? '', color: '#7c3aed' },
  { key: 'devilslayer', url: env['VITE_DEVILSLAYER_URL'] ?? '', color: '#c2410c' },
  ...moreProjects(env['VITE_MORE_PROJECT_URLS'] ?? ''),
  // 가짜 데이터 화면에서 "새 프로젝트 만들기"를 볼 수 있게 대기 프로젝트를 하나 둔다. 일반 빌드에서는 빠진다.
  ...(__MOCK__ ? [{ key: 'standby', url: '', color: '#0f766e' }] : []),
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

// 고객센터 웹 페이지(Admin~/support). SDK 의 SupabaseSettings.customerCenterUrl 기본값과 같다.
export const SUPPORT_PAGE_ORIGIN = 'https://truebase-support.pages.dev'

/** 게임 밖에서 여는 고객센터 주소. 백엔드 주소를 모르면 null. */
export function supportPageUrl(t: ProjectTarget): string | null {
  try {
    const ref = new URL(getProject(t).url).hostname.split('.')[0]
    return ref ? `${SUPPORT_PAGE_ORIGIN}/?p=${ref}` : null
  } catch {
    return null
  }
}
