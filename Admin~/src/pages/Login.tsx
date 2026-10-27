import { useEffect, useRef, useState } from 'react'
import { renderGoogleButton, storeTokens } from '../lib/googleAuth'
import { signInAllProjects, type SignInOutcome } from '../lib/api'
import { PROJECTS } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import PasswordLoginForm from '../components/PasswordLoginForm'

/** 가짜 데이터 모드에서만 — 모든 게임에 가짜 로그인 표를 넣고 들어간다. 일반 빌드에서는 빈 함수가 된다. */
async function enterMock(onSignedIn: (outcomes: SignInOutcome[]) => void) {
  if (!__MOCK__) return
  const { mockToken } = await import('../mock/mockAdmin')
  storeTokens(Object.fromEntries(PROJECTS.map((p) => [p.key, mockToken()])))
  onSignedIn(PROJECTS.map((p) => ({ target: p.key, ok: true })))
}

/**
 * 운영자 로그인.
 *
 * 게임의 Supabase Auth 를 쓰지 않는다 — 운영자 계정이 플레이어 풀에 섞이면
 * 게임의 탈퇴·차단 흐름이 운영자 접근을 건드릴 수 있다.
 * 구글이 신원만 증명하고, 운영자인지는 게임마다 백엔드의 허용목록이 판단한다.
 * 어느 게임에 들어갈지는 로그인 뒤 게임 목록에서 고른다.
 */
export default function Login({ onSignedIn }: { onSignedIn: (outcomes: SignInOutcome[]) => void }) {
  const buttonRef = useRef<HTMLDivElement>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    const el = buttonRef.current
    if (!el) return
    let cancelled = false
    const onCredential = async (idToken: string) => {
      setError('')
      try {
        onSignedIn(await signInAllProjects('auth.googleLogin', { idToken }))
      } catch (e: unknown) {
        setError(e instanceof Error ? e.message : '로그인하지 못했습니다.')
      }
    }
    renderGoogleButton(el, (idToken) => void onCredential(idToken), () => cancelled).catch((e: unknown) => {
      if (!cancelled) setError(e instanceof Error ? e.message : '구글 로그인을 준비하지 못했습니다.')
    })
    return () => { cancelled = true }
  }, [onSignedIn])

  return (
    <div className="min-h-screen flex items-center justify-center p-6 bg-[#161a2b]">
      <WhiteCard className="w-full max-w-sm p-6">
        <h1 className="text-xl font-semibold text-neutral-900">트루베이스 운영 콘솔</h1>
        <p className="mt-1 text-sm text-neutral-600">로그인하면 들어갈 수 있는 게임 목록이 나옵니다.</p>

        <div className="mt-6">
          <PasswordLoginForm onSignedIn={onSignedIn} />
        </div>

        <div className="my-5 flex items-center gap-3">
          <div className="h-px flex-1 bg-neutral-200" />
          <span className="text-xs text-neutral-500">또는</span>
          <div className="h-px flex-1 bg-neutral-200" />
        </div>

        <div ref={buttonRef} className="flex justify-center" />
        {error && <div className="mt-3 text-sm text-red-600">{error}</div>}

        <p className="mt-4 text-xs text-neutral-500">허용된 운영자 계정만 들어올 수 있습니다.</p>

        {__MOCK__ && (
          <button
            type="button"
            onClick={() => void enterMock(onSignedIn)}
            className="mt-4 w-full h-9 rounded-md border border-amber-400 bg-amber-50 text-sm font-medium text-amber-800 hover:bg-amber-100"
          >
            가짜 데이터로 들어가기
          </button>
        )}
      </WhiteCard>
    </div>
  )
}
