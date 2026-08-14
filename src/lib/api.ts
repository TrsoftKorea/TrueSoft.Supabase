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

  const project = getProject(target)
  if (!project.url) throw new Error(`${project.label} 주소가 비어 있습니다. .env 를 확인하세요.`)

  const res = await fetch(`${project.url}/functions/v1/admin-api`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ action, params }),
  })

  const body = (await res.json().catch(() => null)) as
    | { ok?: boolean; data?: T; error?: string }
    | null

  if (res.status === 401) throw new NotAuthenticatedError()
  if (!res.ok || !body?.ok) {
    throw new Error(body?.error ?? `요청이 실패했습니다 (HTTP ${res.status}).`)
  }
  return body.data as T
}
