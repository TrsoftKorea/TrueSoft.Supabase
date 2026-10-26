import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { signInAllProjects, type SignInOutcome } from '../lib/api'
import { Button } from './ui/Button'

/**
 * 이메일 + 비밀번호 로그인. 구글 계정이 없는 운영자용 대안 로그인 경로다.
 *
 * 비밀번호는 마스터가 "운영자 관리" 화면에서 직접 정해서 알려준다 — 여기서는
 * 그렇게 받은 비밀번호로 로그인만 한다.
 */

const inputCls = 'h-9 w-full px-2 rounded-md border border-neutral-300 text-sm bg-white'

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
      // 비밀번호는 게임마다 따로 저장돼 있다 — 이 비밀번호가 맞는 게임만 목록에서 들어갈 수 있다.
      onSignedIn(await signInAllProjects('auth.passwordLogin', { email: email.trim(), password }))
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
          className={inputCls}
        />
      </div>
      <div className="flex flex-col gap-1">
        <span className="text-xs text-neutral-500">비밀번호</span>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void login()}
          className={inputCls}
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
