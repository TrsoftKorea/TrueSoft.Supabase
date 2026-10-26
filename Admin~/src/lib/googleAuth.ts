// 운영자 인증. 게임 Supabase Auth 를 쓰지 않고 구글 ID 토큰을 그대로 백엔드에 보낸다.
// 운영자는 게임의 auth.users 에 존재하지 않는다.

import type { ProjectTarget } from './projectTarget'

type CredentialResponse = { credential?: string }

type GoogleIdApi = {
  initialize(config: {
    client_id: string
    callback: (res: CredentialResponse) => void
    auto_select?: boolean
    cancel_on_tap_outside?: boolean
  }): void
  renderButton(parent: HTMLElement, options: Record<string, unknown>): void
  disableAutoSelect(): void
}

declare global {
  interface Window {
    google?: { accounts: { id: GoogleIdApi } }
  }
}

const STORAGE_KEY = 'tb_admin_tokens'
const GSI_SRC = 'https://accounts.google.com/gsi/client'

const env = import.meta.env as Record<string, string | undefined>
export const GOOGLE_CLIENT_ID = env['VITE_GOOGLE_CLIENT_ID'] ?? ''

/** ID 토큰의 만료 시각(ms). 형식이 이상하면 0 — 즉시 만료로 취급한다. */
function expiryOf(token: string): number {
  const part = token.split('.')[1]
  if (!part) return 0
  try {
    const padded = part.replace(/-/g, '+').replace(/_/g, '/')
    const payload = JSON.parse(atob(padded)) as { exp?: unknown }
    return typeof payload.exp === 'number' ? payload.exp * 1000 : 0
  } catch {
    return 0
  }
}

// 로그인 표(세션 토큰)는 게임마다 따로다 — 게임 서버마다 서명 비밀값이 달라 한쪽 표를 다른 쪽이 알아보지 못한다.
// 그래서 로그인할 때 모든 게임에서 한꺼번에 받아 게임별로 담아 둔다(lib/api 의 signInAllProjects).
type Tokens = Partial<Record<ProjectTarget, string>>

function readTokens(): Tokens {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Tokens) : {}
  } catch {
    // 예전 형식(토큰 문자열 하나)이나 깨진 값이면 다시 로그인하게 한다.
    return {}
  }
}

/** 만료된 표를 걷어 내고 남은 것만 돌려준다(만료 30초 전부터 만료로 본다). */
function liveTokens(): Tokens {
  const all = readTokens()
  const live: Tokens = {}
  for (const [k, t] of Object.entries(all) as [ProjectTarget, string][]) {
    if (t && expiryOf(t) - 30_000 > Date.now()) live[k] = t
  }
  if (Object.keys(live).length !== Object.keys(all).length) storeTokens(live)
  return live
}

/** 이 게임의 로그인 표. 없거나 만료됐으면 null. */
export function getToken(target: ProjectTarget): string | null {
  return liveTokens()[target] ?? null
}

/** 로그인 표가 살아 있는 게임들 — 게임 목록에서 "들어가기"가 켜지는 게임이다. */
export function signedInTargets(): ProjectTarget[] {
  return Object.keys(liveTokens()) as ProjectTarget[]
}

/** 아무 게임이든 살아 있는 표에서 이메일을 읽는다. 없으면 빈 문자열. */
export function signedInEmail(): string {
  const any = Object.values(liveTokens())[0]
  return any ? emailOf(any) : ''
}

export function clearToken(): void {
  window.sessionStorage.removeItem(STORAGE_KEY)
  window.google?.accounts.id.disableAutoSelect()
}

/** 백엔드가 발급한 로그인 세션 토큰들을 게임별로 저장한다(구글·비밀번호 로그인 공용). */
export function storeTokens(tokens: Tokens): void {
  window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tokens))
}

/** 토큰의 이메일. 화면 표시용이며 권한 판단에는 쓰지 않는다(그건 백엔드가 한다). */
export function emailOf(token: string): string {
  const part = token.split('.')[1]
  if (!part) return ''
  try {
    const padded = part.replace(/-/g, '+').replace(/_/g, '/')
    const payload = JSON.parse(atob(padded)) as { email?: unknown }
    return typeof payload.email === 'string' ? payload.email : ''
  } catch {
    return ''
  }
}

let scriptPromise: Promise<void> | null = null

function loadGsi(): Promise<void> {
  if (scriptPromise) return scriptPromise
  scriptPromise = new Promise((resolve, reject) => {
    if (window.google?.accounts.id) return resolve()
    const el = document.createElement('script')
    el.src = GSI_SRC
    el.async = true
    el.onload = () => resolve()
    el.onerror = () => reject(new Error('구글 로그인 스크립트를 불러오지 못했습니다.'))
    document.head.appendChild(el)
  })
  // 실패는 캐시하지 않는다 — 캐시해 두면 네트워크가 살아난 뒤에도 재시도가 같은 reject 를 재생한다.
  scriptPromise.catch(() => { scriptPromise = null })
  return scriptPromise
}

/**
 * 구글 로그인 버튼을 그린다. 로그인에 성공하면 구글 ID 토큰으로 onCredential 을 부른다.
 * 저장하지 않는다 — 구글 토큰은 1시간짜리라, 호출 쪽이 백엔드에서 세션 토큰으로 바꿔 저장한다.
 * 반환값은 정리 함수가 아니라, 스크립트 로딩 실패를 알리기 위한 Promise 다.
 *
 * isCancelled 는 React StrictMode 의 effect 이중 실행을 막는 용도다 — 그게 없으면
 * google.accounts.id.initialize() 가 두 번 불려 버튼이 클릭에 반응하지 않는 상태로
 * 깨진다(개발 모드 콘솔에 "initialize() is called multiple times" 경고로 나타난다).
 */
export async function renderGoogleButton(
  parent: HTMLElement,
  onCredential: (idToken: string) => void,
  isCancelled: () => boolean = () => false,
): Promise<void> {
  if (!GOOGLE_CLIENT_ID) throw new Error('VITE_GOOGLE_CLIENT_ID 가 비어 있습니다.')
  await loadGsi()
  if (isCancelled()) return

  const api = window.google?.accounts.id
  if (!api) throw new Error('구글 로그인을 초기화하지 못했습니다.')

  api.initialize({
    client_id: GOOGLE_CLIENT_ID,
    callback: (res) => {
      if (res.credential) onCredential(res.credential)
    },
    auto_select: false,
    cancel_on_tap_outside: true,
  })
  parent.replaceChildren()
  api.renderButton(parent, { theme: 'outline', size: 'large', width: 320, locale: 'ko' })
}
