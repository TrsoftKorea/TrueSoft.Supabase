import { useEffect, useRef, useState, type ReactNode } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { ChevronDown, LogOut } from 'lucide-react'
import { PROJECTS, setTarget, type ProjectTarget } from '../lib/projectTarget'

const NAV_BASE =
  'inline-flex items-center h-8 px-3 rounded-md text-sm transition-colors'

function NavGroup({ label, items }: { label: string; items: { to: string; label: string }[] }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const location = useLocation()
  const active = items.some((it) => location.pathname.startsWith(it.to))

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [])

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className={`${NAV_BASE} gap-1 ${active ? 'bg-neutral-100 text-neutral-900 font-medium' : 'text-neutral-600 hover:bg-neutral-50'}`}
      >
        {label}
        <ChevronDown className="w-3.5 h-3.5" />
      </button>
      {open && (
        <div className="absolute left-0 mt-1 z-30 w-44 bg-white border border-neutral-200 rounded-md shadow-lg py-1">
          {items.map((it) => (
            <NavLink
              key={it.to}
              to={it.to}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                `block px-3 py-1.5 text-sm ${isActive ? 'bg-neutral-100 text-neutral-900 font-medium' : 'text-neutral-600 hover:bg-neutral-50'}`
              }
            >
              {it.label}
            </NavLink>
          ))}
        </div>
      )}
    </div>
  )
}

export function Layout({
  target,
  onTargetChange,
  email,
  isMaster,
  onSignOut,
  children,
}: {
  target: ProjectTarget
  onTargetChange: (t: ProjectTarget) => void
  email: string
  isMaster: boolean
  onSignOut: () => void
  children: ReactNode
}) {
  const switchTarget = (t: ProjectTarget) => {
    setTarget(t)
    onTargetChange(t)
  }

  const navClass = ({ isActive }: { isActive: boolean }) =>
    `${NAV_BASE} ${isActive ? 'bg-neutral-100 text-neutral-900 font-medium' : 'text-neutral-600 hover:bg-neutral-50'}`

  return (
    <div className="min-h-screen">
      <header className="bg-white border-b border-neutral-200">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center gap-4">
          <span className="font-semibold text-neutral-900">TrueBase</span>

          <select
            value={target}
            onChange={(e) => switchTarget(e.target.value as ProjectTarget)}
            className="h-8 px-2 rounded-md border border-neutral-300 text-sm bg-white"
          >
            {PROJECTS.map((p) => (
              <option key={p.key} value={p.key}>
                {p.label}
              </option>
            ))}
          </select>

          <nav className="flex items-center gap-1">
            <NavLink to="/" end className={navClass}>
              아이템 카탈로그
            </NavLink>
            <NavLink to="/players" className={navClass}>
              플레이어
            </NavLink>
            <NavGroup
              label="우편함"
              items={[
                { to: '/mails/send', label: '우편 발송' },
                { to: '/mails/records', label: '우편 내역' },
                { to: '/mails/categories', label: '우편 분류' },
                { to: '/mails/schedules', label: '예약 목록' },
              ]}
            />
            <NavLink to="/leaderboard" className={navClass}>
              리더보드
            </NavLink>
            <NavLink to="/purchases" className={navClass}>
              구매 내역
            </NavLink>
            <NavLink to="/remote-config" className={navClass}>
              원격 설정
            </NavLink>
            <NavLink to="/schema-changes" className={navClass}>
              변경 관리
            </NavLink>
            {/* 운영자 관리는 마스터만. 서버도 같은 조건으로 다시 막는다. */}
            {isMaster && (
              <NavLink to="/operators" className={navClass}>
                운영자 관리
              </NavLink>
            )}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            <span className="text-sm text-neutral-500">
              {email}
              {isMaster && <span className="ml-1.5 text-xs text-[#1677ff]">마스터</span>}
            </span>
            <button
              onClick={onSignOut}
              className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-neutral-300 text-sm text-neutral-600 hover:bg-neutral-50"
            >
              <LogOut className="w-3.5 h-3.5" />
              로그아웃
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-6xl mx-auto px-6 py-6">{children}</main>
    </div>
  )
}
