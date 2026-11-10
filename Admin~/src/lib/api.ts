import { getProject, PROJECTS, type ProjectTarget } from './projectTarget'
import { getHubToken, getToken, storeHubSession, storeTokens } from './googleAuth'

/** 토큰이 없거나 만료돼 로그인 화면으로 돌려보내야 하는 경우. */
export class NotAuthenticatedError extends Error {
  constructor() {
    super('로그인이 필요합니다.')
    this.name = 'NotAuthenticatedError'
  }
}

/** 서버가 응답은 했지만 실패를 돌려준 경우. 연결 자체가 안 된 경우는 일반 Error 다. */
export class AdminRequestError extends Error {
  constructor(message: string, readonly status: number) {
    super(message)
    this.name = 'AdminRequestError'
  }
}

/**
 * 어드민 백엔드에 POST 하나를 보낸다. token 이 있으면 Authorization 헤더를 실어 보내고
 * 401 을 NotAuthenticatedError 로 바꾼다 — callAdmin/callAdminPublic 이 공유하는 실제 요청 로직은
 * 이 함수 하나뿐이라, 헤더·에러 처리 방식이 바뀌어도 고칠 곳이 한 곳이다.
 */
async function postAdmin<T>(
  target: ProjectTarget,
  action: string,
  params: Record<string, unknown>,
  token: string | null,
): Promise<T> {
  // 가짜 데이터 모드는 서버에 아무것도 보내지 않는다. 일반 빌드에서는 이 분기와 가짜 데이터 파일이 통째로 빠진다.
  if (__MOCK__) {
    const { mockAdmin } = await import('../mock/mockAdmin')
    return mockAdmin<T>(target, action, params)
  }

  const project = getProject(target)
  if (!project.url) throw new Error(`프로젝트(${project.key}) 주소가 비어 있습니다. .env 를 확인하세요.`)

  let res: Response
  try {
    res = await fetch(`${project.url}/functions/v1/admin-api`, {
      method: 'POST',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ action, params }),
    })
  } catch {
    // 브라우저가 주는 원문("Failed to fetch")은 운영자가 읽을 수 없다.
    throw new Error('서버에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.')
  }

  const body = (await res.json().catch(() => null)) as
    | { ok?: boolean; data?: T; error?: string }
    | null

  if (token && res.status === 401) throw new NotAuthenticatedError()
  if (!res.ok || !body?.ok) {
    throw new AdminRequestError(body?.error ?? `요청이 실패했습니다 (HTTP ${res.status}).`, res.status)
  }
  return body.data as T
}

/**
 * 어드민 백엔드 호출.
 *
 * 브라우저는 secret 키를 갖지 않는다. 구글 ID 토큰만 보내고,
 * Edge Function 이 그 토큰을 검증한 뒤에야 secret 키로 DB 에 접근한다.
 * 이 파일에는 권한이 없다 — 권한 판단은 전부 서버에 있다.
 */
export async function callAdmin<T>(
  target: ProjectTarget,
  action: string,
  params: Record<string, unknown> = {},
): Promise<T> {
  const token = getToken(target)
  if (!token) throw new NotAuthenticatedError()
  return postAdmin<T>(target, action, params, token)
}

/**
 * 게임별 로그인 결과. 실패한 게임은 사유를 들고 있다.
 * status 는 서버가 준 응답 코드 — 403 일 때만 "권한 없음"이고, 연결 실패면 비어 있다.
 */
export type SignInOutcome = { target: ProjectTarget; ok: boolean; error?: string; status?: number }

/**
 * 모든 게임 서버에 같은 로그인을 한꺼번에 보내고, 통과한 게임의 표만 저장한다.
 * 한 게임도 통과하지 못하면 첫 실패 사유로 던진다 — 로그인 화면이 그 문구를 그대로 보여 준다.
 */
type HubLogin = { token: string; isMaster: boolean; mustChange?: boolean }

/** 마스터가 초기화한 임시 비밀번호로 들어왔다 — 새 비밀번호를 정하기 전에는 들어갈 수 없다. token 은 바꾸기에만 쓰는 표다. */
export class PasswordChangeRequiredError extends Error {
  constructor(readonly pendingToken: string) {
    super('새 비밀번호를 정해야 합니다.')
    this.name = 'PasswordChangeRequiredError'
  }
}

export async function signInAllProjects(
  login: { idToken: string } | { email: string; password: string },
): Promise<SignInOutcome[]> {
  if ('idToken' in login) {
    // 구글 로그인은 트루베이스 초대 표도 같이 받는다. 초대되지 않았거나 저장소가 응답하지 않아도 게임 프로젝트 로그인은 그대로 된다.
    const hub = postHub<HubLogin>('login', { idToken: login.idToken }, null).catch(() => null)
    return signInProjects('auth.googleLogin', { idToken: login.idToken }, hub, false)
  }
  // 비밀번호는 트루베이스 저장소 한 곳에 있다 — 저장소가 준 표를 프로젝트마다 그 프로젝트의 로그인으로 바꾼다.
  const session = await postHub<HubLogin>('login', { email: login.email, password: login.password }, null)
  if (session.mustChange) throw new PasswordChangeRequiredError(session.token)
  return signInProjects('auth.hubLogin', { hubToken: session.token }, Promise.resolve(session), true)
}

/**
 * 비밀번호로 들어온 사람이 자기 비밀번호를 바꾼다. 바꾸면 그 전 저장소 표와 그 표로 받은 프로젝트 로그인이 끊기므로
 * 새 표로 모든 프로젝트에 다시 로그인해 둔다. pendingToken 은 임시 비밀번호로 받은 표(첫 로그인)다 — 없으면 지금 로그인의 표.
 */
export async function changeOwnPassword(
  currentPassword: string,
  newPassword: string,
  pendingToken?: string,
): Promise<SignInOutcome[]> {
  const token = pendingToken ?? getHubToken()
  if (!token) throw new NotAuthenticatedError()
  const session = await postHub<HubLogin>('passwords.change', { currentPassword, newPassword }, token)
  return signInProjects('auth.hubLogin', { hubToken: session.token }, Promise.resolve(session), true)
}

async function signInProjects(
  action: string,
  params: Record<string, unknown>,
  hub: Promise<HubLogin | null>,
  viaPassword: boolean,
): Promise<SignInOutcome[]> {
  const results = await Promise.all(
    PROJECTS.filter((p) => p.url).map(async (p) => {
      try {
        const { token } = await postAdmin<{ token: string }>(p.key, action, params, null)
        return { target: p.key, ok: true, token }
      } catch (e: unknown) {
        return {
          target: p.key,
          ok: false,
          error: e instanceof Error ? e.message : '로그인하지 못했습니다.',
          status: e instanceof AdminRequestError ? e.status : undefined,
        }
      }
    }),
  )
  const tokens: Partial<Record<ProjectTarget, string>> = {}
  for (const r of results) if (r.ok && r.token) tokens[r.target] = r.token
  const hubSession = await hub
  // 초대만 받고 아직 운영자인 프로젝트가 없는 사람도 들어와야 "새 프로젝트 만들기"를 쓴다.
  if (Object.keys(tokens).length === 0 && !hubSession) {
    throw new Error(results.find((r) => r.error)?.error ?? '로그인하지 못했습니다.')
  }
  storeTokens(tokens)
  storeHubSession(hubSession ? { token: hubSession.token, isMaster: hubSession.isMaster, viaPassword } : null)
  return results.map(({ target, ok, error, status }) =>
    ok ? { target, ok } : { target, ok, error: error ?? '로그인하지 못했습니다.', ...(status !== undefined ? { status } : {}) },
  )
}

// 트루베이스 초대 저장소. 콘솔과 같은 주소의 /hub 다(Cloudflare Pages 의 서버 기능, hub/functions/hub.ts).
const env = import.meta.env as Record<string, string | undefined>
const HUB_URL = env['VITE_HUB_URL'] || '/hub'

async function postHub<T>(action: string, params: Record<string, unknown>, token: string | null): Promise<T> {
  if (__MOCK__) {
    const { mockAdmin } = await import('../mock/mockAdmin')
    return mockAdmin<T>('hub', `hub.${action}`, params)
  }
  let res: Response
  try {
    res = await fetch(HUB_URL, {
      method: 'POST',
      headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), 'Content-Type': 'application/json' },
      body: JSON.stringify({ action, params }),
    })
  } catch {
    throw new Error('서버에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.')
  }
  const body = (await res.json().catch(() => null)) as { ok?: boolean; data?: T; error?: string } | null
  if (token && res.status === 401) throw new NotAuthenticatedError()
  if (!res.ok || !body?.ok) throw new AdminRequestError(body?.error ?? `요청이 실패했습니다 (HTTP ${res.status}).`, res.status)
  return body.data as T
}

/** 초대 저장소 호출(마스터 화면). 초대 표가 없으면 다시 로그인해야 한다. */
export async function callHub<T>(action: string, params: Record<string, unknown> = {}): Promise<T> {
  const token = getHubToken()
  if (!token) throw new NotAuthenticatedError()
  return postHub<T>(action, params, token)
}

/**
 * 권한이 없어 목록에서 숨길 프로젝트인지 — 로그인 때 서버가 403 을 줬고 지금도 표가 없을 때만.
 * 연결 실패·만료처럼 다시 해 보면 될 수 있는 것은 숨기지 않고 사유를 보인다. 그 뒤 새로 만든 프로젝트는 표가 있어 보인다.
 */
export function isForbiddenProject(outcomes: SignInOutcome[], target: ProjectTarget): boolean {
  return getToken(target) === null && outcomes.find((o) => o.target === target)?.status === 403
}

/**
 * 들어갈 수 없는 게임 옆에 붙일 사유. short 는 사이드바처럼 좁은 자리용이다.
 * "권한 없음"은 서버가 403 을 준 게임에만 쓴다 — 연결 실패·서버 오류까지 권한 문제로 보이면
 * 운영자가 엉뚱하게 권한을 요청하게 된다.
 */
export function blockedReason(outcome: SignInOutcome | undefined): { short: string; full: string } {
  if (outcome?.status === 403) return { short: '권한 없음', full: '권한 없음' }
  if (!outcome || outcome.ok) return { short: '다시 로그인', full: '로그인이 만료됐습니다. 다시 로그인해 주세요.' }
  return { short: '들어갈 수 없음', full: outcome.error ?? '로그인하지 못했습니다.' }
}

/**
 * 로그인 전에도 부르는 호출(비밀번호 로그인).
 * 아직 토큰이 없는 상태이므로 Authorization 헤더를 안 보낸다 — admin-api 도 `auth.*` action은
 * 이 헤더 없이도 처리하도록 따로 분기돼 있다.
 */
export async function callAdminPublic<T>(
  target: ProjectTarget,
  action: string,
  params: Record<string, unknown> = {},
): Promise<T> {
  return postAdmin<T>(target, action, params, null)
}
