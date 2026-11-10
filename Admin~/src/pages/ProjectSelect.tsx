import { useState } from 'react'
import { ChevronRight, KeyRound, LogOut, Plus, Settings2 } from 'lucide-react'
import { PROJECTS, type ProjectTarget } from '../lib/projectTarget'
import { setProjectLook, toProjectLook, useProjectLook } from '../lib/projectLook'
import { addToken, getHubToken, hubIsMaster, hubViaPassword, signedInEmail, signedInTargets } from '../lib/googleAuth'
import ChangePasswordDialog from '../components/ChangePasswordDialog'
import { blockedReason, callAdminPublic, isForbiddenProject, type SignInOutcome } from '../lib/api'
import { ProjectIcon } from '../components/ui/ProjectIcon'
import { ThemeToggle } from '../components/ThemeToggle'
import { EditDialog } from '../components/ui/EditDialog'
import { Button } from '../components/ui/Button'
import { inputCls } from '../components/ui/Input'

/**
 * 대기 중인 프로젝트를 꺼낸다. 이름은 꼭 정한다 — 이름 없는 프로젝트가 목록에 섞이면 엉뚱한 게임에서 작업하게 된다.
 * 대기 프로젝트에는 운영자가 없어 그 프로젝트의 로그인 표가 없다 — 트루베이스 초대 표로 꺼내고, 누른 사람이 첫 운영자가 되어
 * 돌려받은 표로 바로 들어간다.
 */
function NewProjectDialog({
  target,
  onClose,
  onCreated,
}: {
  target: ProjectTarget
  onClose: () => void
  onCreated: (t: ProjectTarget) => void
}) {
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const trimmed = name.trim()

  const create = async () => {
    setBusy(true)
    setError('')
    try {
      const hubToken = getHubToken()
      if (!hubToken) throw new Error('로그인이 만료됐습니다. 다시 로그인해 주세요.')
      const data = await callAdminPublic<{
        look: { color: string | null; display_name: string | null; icon: string | null; active?: boolean }
        token: string
      }>(target, 'auth.activateProject', { hubToken, displayName: trimmed })
      addToken(target, data.token)
      setProjectLook(target, toProjectLook(data.look))
      onCreated(target)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : '프로젝트를 만들지 못했습니다.')
      setBusy(false)
    }
  }

  return (
    <EditDialog
      title="새 프로젝트 만들기"
      busy={busy}
      onClose={onClose}
      error={error || undefined}
      footer={
        <>
          <Button onClick={onClose} disabled={busy}>취소</Button>
          <Button variant="primary" onClick={() => void create()} disabled={busy || trimmed === '' || trimmed.length > 40}>
            만들기
          </Button>
        </>
      }
    >
      <label className="block space-y-1">
        <span className="text-xs font-medium text-neutral-500">프로젝트 이름</span>
        <input
          className={`${inputCls} w-full`}
          value={name}
          maxLength={40}
          autoFocus
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && trimmed !== '' && !busy) void create() }}
        />
        <span className="block text-xs text-neutral-500">콘솔에 보이는 이름을 40자까지 넣습니다.</span>
      </label>
    </EditDialog>
  )
}

/**
 * 로그인 뒤 게임 고르기. 로그인 때 모든 게임 서버에 한꺼번에 확인했으므로,
 * 표를 받은 게임만 들어갈 수 있고 나머지는 그 게임에서 받은 실패 사유를 보여 준다.
 * 대기 중인 프로젝트는 목록에 없고, 트루베이스에 초대된 사람(초대 표가 있는 사람)에게 "새 프로젝트 만들기"가 보인다.
 * 마스터에게는 초대·관리 열쇠를 다루는 "트루베이스 설정"이 보인다.
 */
export default function ProjectSelect({
  outcomes,
  onEnter,
  onSignOut,
  onOpenHubSettings,
}: {
  outcomes: SignInOutcome[]
  onEnter: (t: ProjectTarget) => void
  onSignOut: () => void
  onOpenHubSettings: () => void
}) {
  const look = useProjectLook()
  const allowed = new Set(signedInTargets())
  const [creating, setCreating] = useState(false)
  const [changingPassword, setChangingPassword] = useState(false)
  const [passwordChanged, setPasswordChanged] = useState(false)
  const invited = getHubToken() !== null
  const standby = invited ? PROJECTS.find((p) => look.hidden(p.key))?.key ?? null : null
  // 대기 중이거나 권한이 없는 프로젝트는 보이지 않는다.
  const visible = PROJECTS.filter((p) => !look.hidden(p.key) && !isForbiddenProject(outcomes, p.key))

  return (
    <div className="min-h-screen flex items-center justify-center p-6 bg-[#161a2b]">
      <div className="w-full max-w-md space-y-4">
        <div className="flex items-center justify-between gap-3 text-[#c7cbe0]">
          <h1 className="text-xl font-semibold text-white whitespace-nowrap shrink-0">프로젝트 선택</h1>
          <div className="flex items-center gap-2">
            <ThemeToggle className="border-[#2c3250] text-[#c7cbe0] hover:bg-white/5 hover:text-white" />
            <button
              type="button"
              onClick={onSignOut}
              className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-[#2c3250] text-sm hover:bg-white/5 hover:text-white whitespace-nowrap"
            >
              <LogOut className="w-3.5 h-3.5" />
              로그아웃
            </button>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <p className="flex-1 min-w-0 text-sm text-[#c7cbe0] truncate">{signedInEmail()}</p>
          {hubViaPassword() && (
            <button
              type="button"
              onClick={() => setChangingPassword(true)}
              className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-[#2c3250] text-sm text-[#c7cbe0] hover:bg-white/5 hover:text-white whitespace-nowrap shrink-0"
            >
              <KeyRound className="w-3.5 h-3.5" />
              비밀번호 바꾸기
            </button>
          )}
          {hubIsMaster() && (
            <button
              type="button"
              onClick={onOpenHubSettings}
              className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-[#2c3250] text-sm text-[#c7cbe0] hover:bg-white/5 hover:text-white whitespace-nowrap shrink-0"
            >
              <Settings2 className="w-3.5 h-3.5" />
              트루베이스 설정
            </button>
          )}
        </div>
        {passwordChanged && <p className="text-xs text-emerald-400">비밀번호를 바꿨습니다.</p>}
        {changingPassword && (
          <ChangePasswordDialog
            email={signedInEmail()}
            onClose={() => setChangingPassword(false)}
            onDone={() => { setChangingPassword(false); setPasswordChanged(true) }}
          />
        )}

        <ul className="space-y-2">
          {visible.map((p) => {
            const ok = allowed.has(p.key)
            const color = look.color(p.key)
            const reason = ok ? null : blockedReason(outcomes.find((o) => o.target === p.key))
            return (
              <li key={p.key}>
                <button
                  type="button"
                  disabled={!ok}
                  onClick={() => onEnter(p.key)}
                  className="w-full flex items-center gap-4 rounded-xl bg-white px-4 py-4 text-left transition-shadow hover:shadow-lg disabled:bg-[#1f2438] disabled:cursor-not-allowed"
                  style={ok ? { boxShadow: `inset 6px 0 0 ${color}` } : undefined}
                >
                  <ProjectIcon target={p.key} size={40} dim={!ok} />
                  <span className="flex-1 min-w-0">
                    <span className={`block truncate text-base font-semibold ${ok ? 'text-neutral-900' : 'text-[#8d93b0]'}`}>
                      {look.name(p.key)}
                    </span>
                    {reason && reason.full !== reason.short && (
                      <span className="block mt-0.5 text-xs text-[#8d93b0] break-words">{reason.full}</span>
                    )}
                  </span>
                  {ok
                    ? <ChevronRight className="w-5 h-5 text-neutral-400 shrink-0" />
                    : <span className="text-xs text-[#8d93b0] whitespace-nowrap">{reason?.short}</span>}
                </button>
              </li>
            )
          })}
        </ul>

        {standby && (
          <button
            type="button"
            onClick={() => setCreating(true)}
            className="w-full flex items-center justify-center gap-2 h-12 rounded-xl border border-dashed border-[#2c3250] text-sm text-[#c7cbe0] hover:bg-white/5 hover:text-white"
          >
            <Plus className="w-4 h-4" />
            새 프로젝트 만들기
          </button>
        )}
      </div>
      {creating && standby && (
        <NewProjectDialog
          target={standby}
          onClose={() => setCreating(false)}
          onCreated={(t) => {
            setCreating(false)
            onEnter(t)
          }}
        />
      )}
    </div>
  )
}
