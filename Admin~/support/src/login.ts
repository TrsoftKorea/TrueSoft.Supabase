// 게임 밖 창의 구글·애플 로그인. 두 회사가 주는 웹 로그인 스크립트로 ID 토큰을 받아 support-api web.login 에 넘긴다.
// nonce — 토큰을 가로채 다른 곳에 다시 쓰지 못하게 한 번만 쓰는 값. 두 회사에는 해시를 주고 서버(인증 서버)에는 원래 값을
// 넘긴다 — 인증 서버가 원래 값을 해시해 토큰 속 값과 맞춰 본다.

const GOOGLE_SCRIPT = 'https://accounts.google.com/gsi/client'
// 애플 로그인 창의 글자는 애플이 정한다. 스크립트 주소의 언어는 애플 단추 그림에만 쓰이는데 단추는 이 페이지가 그린다.
const APPLE_SCRIPT = 'https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/en_US/appleid.auth.js'

const loading = new Map<string, Promise<void>>()

function loadScript(src: string): Promise<void> {
  let p = loading.get(src)
  if (!p) {
    p = new Promise<void>((resolve, reject) => {
      const s = document.createElement('script')
      s.src = src
      s.async = true
      s.onload = () => resolve()
      s.onerror = () => { loading.delete(src); reject(new Error(`script: ${src}`)) }
      document.head.appendChild(s)
    })
    loading.set(src, p)
  }
  return p
}

export async function makeNonce(): Promise<{ raw: string; hashed: string }> {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  const raw = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(raw)))
  return { raw, hashed: Array.from(digest, (b) => b.toString(16).padStart(2, '0')).join('') }
}

type GoogleId = {
  accounts: {
    id: {
      initialize(o: { client_id: string; nonce: string; callback: (r: { credential?: string }) => void; ux_mode?: string }): void
      renderButton(el: HTMLElement, o: Record<string, unknown>): void
    }
  }
}

/** 구글 로그인 단추를 el 에 그린다. 누르고 고르면 onToken 이 ID 토큰과 원래 nonce 로 불린다. */
export async function renderGoogleButton(
  el: HTMLElement, clientId: string, lang: string, onToken: (idToken: string, rawNonce: string) => void,
): Promise<void> {
  await loadScript(GOOGLE_SCRIPT)
  const google = (window as unknown as { google?: GoogleId }).google
  if (!google) throw new Error('google script')
  const nonce = await makeNonce()
  google.accounts.id.initialize({
    client_id: clientId,
    nonce: nonce.hashed,
    ux_mode: 'popup',
    callback: (r) => { if (r.credential) onToken(r.credential, nonce.raw) },
  })
  el.replaceChildren()
  google.accounts.id.renderButton(el, {
    type: 'standard', theme: 'outline', size: 'large', shape: 'rectangular', text: 'signin_with',
    width: Math.min(320, el.clientWidth || 320), locale: lang.replace('-', '_'),
  })
}

type AppleId = {
  auth: {
    init(o: { clientId: string; redirectURI: string; usePopup: boolean; nonce: string; state: string }): void
    signIn(): Promise<{ authorization?: { id_token?: string } }>
  }
}

export type AppleLogin = { signIn(): Promise<{ idToken: string; rawNonce: string } | null> }

/**
 * 애플 로그인을 미리 준비한다(스크립트·nonce). 아이폰 Safari 는 누른 직후가 아닌 팝업을 막으므로, 단추를 누르면
 * 기다림 없이 바로 signIn 을 부를 수 있게 화면이 열릴 때 해 둔다. signIn 한 번마다 nonce 를 새로 준비한다.
 */
export async function prepareApple(servicesId: string): Promise<AppleLogin> {
  await loadScript(APPLE_SCRIPT)
  const apple = (window as unknown as { AppleID?: AppleId }).AppleID
  if (!apple) throw new Error('apple script')
  let nonce = await makeNonce()
  const init = (n: { raw: string; hashed: string }) => {
    const state = Array.from(crypto.getRandomValues(new Uint8Array(8)), (b) => b.toString(16).padStart(2, '0')).join('')
    // 돌아올 주소는 Services ID 에 등록한 반환 URL 과 같아야 한다(콘솔 "서비스 연결" 안내: https://truebase-support.pages.dev/).
    apple.auth.init({ clientId: servicesId, redirectURI: `${location.origin}/`, usePopup: true, nonce: n.hashed, state })
  }
  init(nonce)
  return {
    async signIn() {
      const used = nonce
      try {
        const r = await apple.auth.signIn()
        const idToken = r.authorization?.id_token
        return idToken ? { idToken, rawNonce: used.raw } : null
      } catch (e: unknown) {
        const code = (e as { error?: string } | null)?.error
        if (code === 'popup_closed_by_user' || code === 'user_cancelled_authorize') return null
        throw e
      } finally {
        nonce = await makeNonce()
        init(nonce)
      }
    },
  }
}
