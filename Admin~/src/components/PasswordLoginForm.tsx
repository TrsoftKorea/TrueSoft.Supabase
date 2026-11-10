import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { PasswordChangeRequiredError, signInAllProjects, type SignInOutcome } from '../lib/api'
import ChangePasswordDialog from './ChangePasswordDialog'
import { Button } from './ui/Button'
import { inputCls } from './ui/Input'

/**
 * 이메일 + 비밀번호 로그인. 구글 계정이 없는 운영자용 대안 로그인 경로다.
 *
 * 마스터는 "트루베이스 설정"에서 임시 비밀번호만 준다. 임시 비밀번호로 들어오면 새 비밀번호를 정한 뒤에 들어간다.
 */

export default function PasswordLoginForm({ onSignedIn }: { onSignedIn: (outcomes: SignInOutcome[]) => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [pendingToken, setPendingToken] = useState<string | null>(null)

  const login = async () => {
    if (!email.trim() || !password || busy) return
    setBusy(true)
    setError('')
    try {
      onSignedIn(await signInAllProjects({ email: email.trim(), password }))
    } catch (e: unknown) {
      if (e instanceof PasswordChangeRequiredError) setPendingToken(e.pendingToken)
      else setError(e instanceof Error ? e.message : '로그인하지 못했습니다.')
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
      {pendingToken && (
        <ChangePasswordDialog
          email={email.trim()}
          forced={{ currentPassword: password, pendingToken }}
          onClose={() => { setPendingToken(null); setPassword('') }}
          onDone={(outcomes) => { setPendingToken(null); onSignedIn(outcomes) }}
        />
      )}
    </div>
  )
}
