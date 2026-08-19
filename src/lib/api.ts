import { getProject, type ProjectTarget } from './projectTarget'
import { getToken } from './googleAuth'

/** 토큰이 없거나 만료돼 로그인 화면으로 돌려보내야 하는 경우. */
export class NotAuthenticatedError extends Error {
  constructor() {
    super('로그인이 필요합니다.')
    this.name = 'NotAuthenticatedError'
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
  const project = getProject(target)
  if (!project.url) throw new Error(`${project.label} 주소가 비어 있습니다. .env 를 확인하세요.`)

  const res = await fetch(`${project.url}/functions/v1/admin-api`, {
    method: 'POST',
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ action, params }),
  })

  const body = (await res.json().catch(() => null)) as
    | { ok?: boolean; data?: T; error?: string }
    | null

  if (token && res.status === 401) throw new NotAuthenticatedError()
  if (!res.ok || !body?.ok) {
    throw new Error(body?.error ?? `요청이 실패했습니다 (HTTP ${res.status}).`)
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
  const token = getToken()
  if (!token) throw new NotAuthenticatedError()
  return postAdmin<T>(target, action, params, token)
}

/**
 * 로그인 전에도 부르는 호출(비밀번호 로그인·재설정 코드 요청/확인).
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
