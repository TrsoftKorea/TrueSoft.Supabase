import type { ReactNode } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { LogOut } from 'lucide-react'
import { PROJECTS, setTarget, type ProjectTarget } from '../lib/projectTarget'

const NAV_BASE = 'flex items-center h-9 px-3 rounded-md text-sm transition-colors'

type NavItem = { to: string; label: string; end?: boolean }

function NavLinkRow({ to, label, end = false }: NavItem) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `${NAV_BASE} ${isActive ? 'bg-[#e8f0fe] text-[#1677ff] font-medium' : 'text-neutral-600 hover:bg-neutral-100'}`
      }
    >
      {label}
    </NavLink>
  )
}

function NavGroupBlock({ label, items }: { label: string; items: NavItem[] }) {
  const location = useLocation()
  const active = items.some((it) => location.pathname.startsWith(it.to))
  return (
    <div>
      <div className={`px-3 pt-3 pb-1 text-xs font-medium ${active ? 'text-[#1677ff]' : 'text-neutral-400'}`}>{label}</div>
      <div className="space-y-0.5">
        {items.map((it) => (
          <NavLinkRow key={it.to} {...it} />
        ))}
      </div>
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

  return (
    <div className="min-h-screen flex">
      <aside className="w-56 shrink-0 bg-white border-r border-neutral-200 flex flex-col">
        <div className="h-14 flex items-center px-4 border-b border-neutral-200">
          <span className="font-semibold text-neutral-900">TrueBase</span>
        </div>

        <div className="px-3 pt-3">
          <select
            value={target}
            onChange={(e) => switchTarget(e.target.value as ProjectTarget)}
            className="w-full h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white"
          >
            {PROJECTS.map((p) => (
              <option key={p.key} value={p.key}>
                {p.label}
              </option>
            ))}
          </select>
        </div>

        <nav className="flex-1 overflow-y-auto px-3 py-3 space-y-0.5">
          <NavLinkRow to="/dashboard" label="대시보드" />
          <NavLinkRow to="/" label="아이템 카탈로그" end />
          <NavLinkRow to="/players" label="플레이어" />
          <NavGroupBlock
            label="우편함"
            items={[
              { to: '/mails/send', label: '우편 발송' },
              { to: '/mails/records', label: '우편 내역' },
              { to: '/mails/categories', label: '우편 분류' },
              { to: '/mails/schedules', label: '예약 목록' },
            ]}
          />
          <NavLinkRow to="/leaderboard" label="리더보드" />
          <NavLinkRow to="/purchases" label="구매 내역" />
          <NavLinkRow to="/remote-config" label="원격 설정" />
          <NavLinkRow to="/coupons" label="쿠폰" />
          <NavLinkRow to="/chat" label="채팅 관리" />
          <NavLinkRow to="/data-logs" label="데이터 로그" />
          <NavLinkRow to="/schema-changes" label="변경 관리" />
          {/* 운영자 관리는 마스터만. 서버도 같은 조건으로 다시 막는다. */}
          {isMaster && <NavLinkRow to="/operators" label="운영자 관리" />}
        </nav>

        <div className="border-t border-neutral-200 p-3 space-y-2">
          <div className="text-xs text-neutral-500 truncate">
            {email}
            {isMaster && <span className="ml-1.5 text-[#1677ff]">마스터</span>}
          </div>
          <button
            onClick={onSignOut}
            className="w-full inline-flex items-center justify-center gap-1.5 h-8 px-3 rounded-md border border-neutral-300 text-sm text-neutral-600 hover:bg-neutral-50"
          >
            <LogOut className="w-3.5 h-3.5" />
            로그아웃
          </button>
        </div>
      </aside>

      <div className="flex-1 min-w-0">
        <main className="max-w-6xl mx-auto px-6 py-6">{children}</main>
      </div>
    </div>
  )
}
