import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { changeOwnPassword, type SignInOutcome } from '../lib/api'
import { EditDialog } from './ui/EditDialog'
import { Button } from './ui/Button'
import { inputCls } from './ui/Input'

// hub.ts 의 MIN_PASSWORD_LENGTH 와 같은 값이어야 한다 — 런타임이 달라(Vite 번들 vs Pages 서버 기능) 상수를 공유할 수 없다.
const MIN_PASSWORD_LENGTH = 8

/**
 * 비밀번호로 들어온 사람이 자기 비밀번호를 정한다. 마스터는 초기화(임시 비밀번호)만 하고 실제 비밀번호는 모른다.
 * forced 는 임시 비밀번호로 막 로그인한 경우 — 지금 비밀번호는 방금 넣은 것이라 다시 묻지 않는다.
 */
export default function ChangePasswordDialog({
  email,
  forced,
  onClose,
  onDone,
}: {
  email: string
  forced?: { currentPassword: string; pendingToken: string }
  onClose: () => void
  onDone: (outcomes: SignInOutcome[]) => void
}) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const tooShort = next.length > 0 && next.length < MIN_PASSWORD_LENGTH
  const mismatch = confirm.length > 0 && next !== confirm
  const canSubmit = (forced || current.length > 0) && next.length >= MIN_PASSWORD_LENGTH && next === confirm && !busy

  const submit = async () => {
    if (!canSubmit) return
    setBusy(true)
    setError('')
    try {
      onDone(await changeOwnPassword(forced?.currentPassword ?? current, next, forced?.pendingToken))
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '비밀번호를 바꾸지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <EditDialog
      title={forced ? '새 비밀번호 정하기' : '비밀번호 바꾸기'}
      description={email}
      busy={busy}
      onClose={onClose}
      error={error || undefined}
      footer={<>
        <Button variant="outline" onClick={onClose} disabled={busy}>취소</Button>
        <Button variant="primary" onClick={() => void submit()} disabled={!canSubmit}>
          {busy && <Loader2 className="w-4 h-4 animate-spin" />}
          저장
        </Button>
      </>}
    >
      <div className="space-y-3">
        {forced ? (
          <p className="text-xs text-neutral-500">임시 비밀번호로 로그인했습니다. 새 비밀번호를 정하면 들어갑니다.</p>
        ) : (
          <div>
            <label className="block text-xs text-neutral-500 mb-1">지금 비밀번호</label>
            <input
              type="password"
              autoComplete="current-password"
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
              className={inputCls + ' w-full'}
            />
          </div>
        )}
        <div>
          <label className="block text-xs text-neutral-500 mb-1">새 비밀번호</label>
          <input
            type="password"
            autoComplete="new-password"
            value={next}
            onChange={(e) => setNext(e.target.value)}
            className={inputCls + ' w-full'}
          />
          <p className={`mt-1 text-xs ${tooShort ? 'text-amber-600' : 'text-neutral-400'}`}>{MIN_PASSWORD_LENGTH}자 이상 넣습니다.</p>
        </div>
        <div>
          <label className="block text-xs text-neutral-500 mb-1">새 비밀번호 확인</label>
          <input
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && void submit()}
            className={inputCls + ' w-full'}
          />
          {mismatch && <p className="mt-1 text-xs text-amber-600">비밀번호가 서로 다릅니다.</p>}
        </div>
        {!forced && <p className="text-xs text-neutral-500">바꾸면 다른 브라우저에 로그인해 둔 곳은 몇 분 안에 로그아웃됩니다.</p>}
      </div>
    </EditDialog>
  )
}
