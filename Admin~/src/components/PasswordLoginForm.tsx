import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { signInAllProjects, type SignInOutcome } from '../lib/api'
import { Button } from './ui/Button'
import { inputCls } from './ui/Input'

/**
 * 이메일 + 비밀번호 로그인. 구글 계정이 없는 운영자용 대안 로그인 경로다.
 *
 * 비밀번호는 마스터가 "트루베이스 설정" 화면에서 직접 정해서 알려준다 — 여기서는
 * 그렇게 받은 비밀번호로 로그인만 한다.
 */

export default function PasswordLoginForm({ onSignedIn }: { onSignedIn: (outcomes: SignInOutcome[]) => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const login = async () => {
    if (!email.trim() || !password || busy) return
    setBusy(true)
    setError('')
    try {
      onSignedIn(await signInAllProjects({ email: email.trim(), password }))
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '로그인하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-2.5">
      <div className="flex flex-col gap-1">
        <span className="text-xs text-neutral-500">이메일</span>
        <input
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void login()}
          className={inputCls + ' w-full'}
        />
      </div>
      <div className="flex flex-col gap-1">
        <span className="text-xs text-neutral-500">비밀번호</span>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void login()}
          className={inputCls + ' w-full'}
        />
      </div>
      {error && <p className="text-xs text-red-600">{error}</p>}
      <Button variant="primary" onClick={() => void login()} disabled={busy || !email.trim() || !password} className="w-full">
        {busy && <Loader2 className="w-4 h-4 animate-spin" />}
        로그인
      </Button>
      <p className="text-xs text-neutral-400">비밀번호를 모르면 마스터에게 문의하세요.</p>
    </div>
  )
}
