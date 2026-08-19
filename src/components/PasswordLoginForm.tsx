import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { callAdminPublic } from '../lib/api'
import { storeToken } from '../lib/googleAuth'
import type { ProjectTarget } from '../lib/projectTarget'

/**
 * 이메일 + 비밀번호 로그인. 구글 계정이 없는 운영자용 대안 로그인 경로다.
 *
 * 최초 비밀번호 설정과 재설정은 같은 흐름(이메일로 받는 6자리 코드)을 쓴다 —
 * 평소 로그인은 이 코드를 안 거친다.
 */

type Mode = 'login' | 'request' | 'confirm'

const inputCls = 'h-9 w-full px-2 rounded-md border border-neutral-300 text-sm bg-white'
const primaryBtnCls =
  'w-full h-9 inline-flex items-center justify-center gap-1.5 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-50'
const linkBtnCls = 'text-xs text-[#1677ff] hover:underline'

export default function PasswordLoginForm({
  target,
  onToken,
  initialMode = 'login',
  initialEmail = '',
  initialCode = '',
}: {
  target: ProjectTarget
  onToken: (token: string) => void
  /** 초대·재설정 메일의 링크로 들어왔을 때 코드 확인 단계부터 바로 열기 위한 값들. */
  initialMode?: Mode
  initialEmail?: string
  initialCode?: string
}) {
  const [mode, setMode] = useState<Mode>(initialMode)
  const [email, setEmail] = useState(initialEmail)
  const [password, setPassword] = useState('')
  const [code, setCode] = useState(initialCode)
  const [newPassword, setNewPassword] = useState('')
  const [newPasswordConfirm, setNewPasswordConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState(initialMode === 'confirm' ? '새 비밀번호를 설정하세요.' : '')

  const login = async () => {
    if (!email.trim() || !password || busy) return
    setBusy(true)
    setError('')
    try {
      const { token } = await callAdminPublic<{ token: string }>(target, 'auth.passwordLogin', {
        email: email.trim(),
        password,
      })
      storeToken(token)
      onToken(token)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '로그인하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const requestCode = async () => {
    if (!email.trim() || busy) return
    setBusy(true)
    setError('')
    try {
      await callAdminPublic(target, 'auth.requestReset', { email: email.trim() })
      setNotice('등록된 이메일이면 코드가 도착합니다. 받은 코드를 아래에 입력하세요.')
      setMode('confirm')
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '코드를 요청하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const confirmReset = async () => {
    if (!code.trim() || !newPassword || busy) return
    if (newPassword !== newPasswordConfirm) {
      setError('새 비밀번호가 서로 다릅니다.')
      return
    }
    setBusy(true)
    setError('')
    try {
      await callAdminPublic(target, 'auth.resetPassword', {
        email: email.trim(),
        code: code.trim(),
        newPassword,
      })
      setPassword('')
      setCode('')
      setNewPassword('')
      setNewPasswordConfirm('')
      setNotice('비밀번호가 설정됐습니다. 로그인하세요.')
      setMode('login')
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '설정하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  if (mode === 'login') {
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
        {notice && <p className="text-xs text-emerald-600">{notice}</p>}
        {error && <p className="text-xs text-red-600">{error}</p>}
        <button onClick={() => void login()} disabled={busy || !email.trim() || !password} className={primaryBtnCls}>
          {busy && <Loader2 className="w-4 h-4 animate-spin" />}
          로그인
        </button>
        <button
          type="button"
          onClick={() => {
            setError('')
            setNotice('')
            setMode('request')
          }}
          className={linkBtnCls}
        >
          비밀번호를 처음 설정하거나 잊으셨나요?
        </button>
      </div>
    )
  }

  if (mode === 'request') {
    return (
      <div className="space-y-2.5">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-neutral-500">이메일</span>
          <input
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && void requestCode()}
            className={inputCls}
          />
        </div>
        {error && <p className="text-xs text-red-600">{error}</p>}
        <button onClick={() => void requestCode()} disabled={busy || !email.trim()} className={primaryBtnCls}>
          {busy && <Loader2 className="w-4 h-4 animate-spin" />}
          코드 받기
        </button>
        <button
          type="button"
          onClick={() => {
            setError('')
            setNotice('')
            setMode('login')
          }}
          className={linkBtnCls}
        >
          로그인으로 돌아가기
        </button>
      </div>
    )
  }

  return (
    <div className="space-y-2.5">
      <p className="text-xs text-neutral-500">{email}</p>
      <div className="flex flex-col gap-1">
        <span className="text-xs text-neutral-500">이메일로 받은 6자리 코드</span>
        <input value={code} onChange={(e) => setCode(e.target.value)} className={inputCls} />
      </div>
      <div className="flex flex-col gap-1">
        <span className="text-xs text-neutral-500">새 비밀번호 (8자 이상)</span>
        <input
          type="password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          className={inputCls}
        />
      </div>
      <div className="flex flex-col gap-1">
        <span className="text-xs text-neutral-500">새 비밀번호 확인</span>
        <input
          type="password"
          value={newPasswordConfirm}
          onChange={(e) => setNewPasswordConfirm(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void confirmReset()}
          className={inputCls}
        />
      </div>
      {notice && <p className="text-xs text-emerald-600">{notice}</p>}
      {error && <p className="text-xs text-red-600">{error}</p>}
      <button
        onClick={() => void confirmReset()}
        disabled={busy || !code.trim() || !newPassword}
        className={primaryBtnCls}
      >
        {busy && <Loader2 className="w-4 h-4 animate-spin" />}
        비밀번호 설정
      </button>
      <button
        type="button"
        onClick={() => {
          setError('')
          setNotice('')
          setMode('request')
        }}
        className={linkBtnCls}
      >
        코드 다시 받기
      </button>
    </div>
  )
}
