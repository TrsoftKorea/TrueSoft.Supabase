import { useEffect, useRef, useState } from 'react'
import { PROJECTS, setTarget, type ProjectTarget } from '../lib/projectTarget'
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
  onTargetChange,
  onToken,
}: {
  target: ProjectTarget
  onTargetChange: (t: ProjectTarget) => void
  onToken: (token: string) => void
}) {
  const buttonRef = useRef<HTMLDivElement>(null)
  const [error, setError] = useState('')

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

        <select
          value={target}
          onChange={(e) => {
            const t = e.target.value as ProjectTarget
            setTarget(t)
            onTargetChange(t)
          }}
          className="mt-2 w-full h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white"
        >
          {PROJECTS.map((p) => (
            <option key={p.key} value={p.key}>
              {p.label}
            </option>
          ))}
        </select>

        <div className="mt-6">
          <PasswordLoginForm target={target} onToken={onToken} />
        </div>

        <div className="my-5 flex items-center gap-3">
          <div className="h-px flex-1 bg-neutral-200" />
          <span className="text-xs text-neutral-400">또는</span>
          <div className="h-px flex-1 bg-neutral-200" />
        </div>

        <div ref={buttonRef} className="flex justify-center" />
        {error && <div className="mt-3 text-sm text-red-600">{error}</div>}

        <p className="mt-4 text-xs text-neutral-400">허용된 운영자 계정만 들어올 수 있습니다.</p>
      </WhiteCard>
    </div>
  )
}
