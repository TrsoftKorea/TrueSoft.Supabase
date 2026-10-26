import { ChevronRight, LogOut } from 'lucide-react'
import { PROJECTS, type ProjectTarget } from '../lib/projectTarget'
import { useProjectLook } from '../lib/projectLook'
import { signedInEmail, signedInTargets } from '../lib/googleAuth'
import { ProjectIcon } from '../components/ui/ProjectIcon'
import { ThemeToggle } from '../components/ThemeToggle'

/**
 * 로그인 뒤 게임 고르기. 로그인 때 모든 게임 서버에 한꺼번에 확인했으므로,
 * 표를 받은 게임만 들어갈 수 있고 나머지는 "권한 없음"으로 보인다.
 */
export default function ProjectSelect({
  onEnter,
  onSignOut,
}: {
  onEnter: (t: ProjectTarget) => void
  onSignOut: () => void
}) {
  const look = useProjectLook()
  const allowed = new Set(signedInTargets())

  return (
    <div className="min-h-screen flex items-center justify-center p-6 bg-[#161a2b]">
      <div className="w-full max-w-md space-y-4">
        <div className="flex items-center justify-between gap-3 text-[#c7cbe0]">
          <h1 className="text-xl font-semibold text-white">게임 선택</h1>
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
        <p className="text-sm text-[#c7cbe0] truncate">{signedInEmail()}</p>

        <ul className="space-y-2">
          {PROJECTS.map((p) => {
            const ok = allowed.has(p.key)
            const color = look.color(p.key)
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
                  <span className={`flex-1 min-w-0 truncate text-base font-semibold ${ok ? 'text-neutral-900' : 'text-[#8d93b0]'}`}>
                    {look.name(p.key)}
                  </span>
                  {ok
                    ? <ChevronRight className="w-5 h-5 text-neutral-400 shrink-0" />
                    : <span className="text-xs text-[#8d93b0] whitespace-nowrap">권한 없음</span>}
                </button>
              </li>
            )
          })}
        </ul>
      </div>
    </div>
  )
}
