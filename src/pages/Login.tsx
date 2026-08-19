import { useEffect, useRef, useState } from 'react'
import { getProject, type ProjectTarget } from '../lib/projectTarget'
import { renderGoogleButton } from '../lib/googleAuth'
import { WhiteCard } from '../components/ui/Card'
import PasswordLoginForm from '../components/PasswordLoginForm'

/**
 * 운영자 로그인.
 *
 * 게임의 Supabase Auth 를 쓰지 않는다 — 운영자 계정이 플레이어 풀에 섞이면
 * 게임의 탈퇴·차단 흐름이 운영자 접근을 건드릴 수 있다.
 * 구글이 신원만 증명하고, 운영자인지는 백엔드의 허용목록이 판단한다.
 */
export default function Login({
  target,
  onToken,
}: {
  target: ProjectTarget
  onToken: (token: string) => void
}) {
  const buttonRef = useRef<HTMLDivElement>(null)
  const [error, setError] = useState('')

  // 초대·재설정 메일의 링크(?email=&code=)로 들어온 경우 — 비밀번호 폼을 코드 확인
  // 단계부터 바로 연다. 링크는 한 번만 유효하므로 읽자마자 주소창에서 지운다.
  const [invite] = useState(() => {
    const params = new URLSearchParams(window.location.search)
    const inviteEmail = params.get('email')
    const inviteCode = params.get('code')
    return inviteEmail && inviteCode ? { email: inviteEmail, code: inviteCode } : null
  })
  const [usePassword, setUsePassword] = useState(!!invite)

  useEffect(() => {
    if (!invite) return
    const url = new URL(window.location.href)
    url.searchParams.delete('email')
    url.searchParams.delete('code')
    window.history.replaceState({}, '', url.toString())
  }, [invite])

  const project = getProject(target)

  useEffect(() => {
    const el = buttonRef.current
    if (!el) return
    let cancelled = false
    renderGoogleButton(el, onToken, () => cancelled).catch((e: unknown) => {
      if (!cancelled) setError(e instanceof Error ? e.message : '구글 로그인을 준비하지 못했습니다.')
    })
    return () => { cancelled = true }
  }, [onToken])

  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <WhiteCard className="w-full max-w-sm p-6">
        <h1 className="text-xl font-semibold text-neutral-900">TrueBase 운영 도구</h1>
        <p className="text-sm text-neutral-500 mt-1">{project.label}</p>

        <div ref={buttonRef} className={`mt-6 flex justify-center ${usePassword ? 'hidden' : ''}`} />
        {usePassword && (
          <div className="mt-6">
            <PasswordLoginForm
              target={target}
              onToken={onToken}
              initialMode={invite ? 'confirm' : 'login'}
              initialEmail={invite?.email ?? ''}
              initialCode={invite?.code ?? ''}
            />
          </div>
        )}

        {error && <div className="mt-3 text-sm text-red-600">{error}</div>}

        <button
          type="button"
          onClick={() => setUsePassword((v) => !v)}
          className="mt-4 text-xs text-[#1677ff] hover:underline"
        >
          {usePassword ? '구글 계정으로 로그인' : '이메일과 비밀번호로 로그인'}
        </button>

        <p className="mt-3 text-xs text-neutral-400">허용된 운영자 계정만 들어올 수 있습니다.</p>
      </WhiteCard>
    </div>
  )
}
