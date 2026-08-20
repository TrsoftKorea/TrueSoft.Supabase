import { useState, type ReactNode, type ComponentType } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import {
  LayoutDashboard, Database, Trophy, Settings, Users, ReceiptText, Package, Ticket,
  MessageSquare, Mail, Send, Inbox, Tags, CalendarClock, ScrollText, History, UserCog,
  ChevronDown, LogOut, AlertTriangle, BookOpen,
} from 'lucide-react'
import { PROJECTS, setTarget, type ProjectTarget } from '../lib/projectTarget'
import { usePendingChanges } from '../lib/pendingChanges'

const NAV_BASE = 'flex items-center gap-2 h-9 px-3 rounded-md text-sm transition-colors'

type Icon = ComponentType<{ className?: string }>
type NavItem = { to: string; label: string; icon: Icon; end?: boolean }

function NavLinkRow({ to, label, icon: Icon, end = false, badge }: NavItem & { badge?: number | undefined }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `${NAV_BASE} ${isActive ? 'bg-[#e8f0fe] text-[#1677ff] font-medium' : 'text-neutral-600 hover:bg-neutral-100'}`
      }
    >
      <Icon className="w-4 h-4" />
      <span>{label}</span>
      {!!badge && (
        <span className="ml-auto inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full bg-amber-500 text-white text-[11px] font-medium leading-none">
          {badge}
        </span>
      )}
    </NavLink>
  )
}

function NavGroupBlock({ label, icon: Icon, items }: { label: string; icon: Icon; items: NavItem[] }) {
  const location = useLocation()
  const active = items.some((it) => location.pathname.startsWith(it.to))
  // 현재 화면이 이 그룹 소속이면 펼친 채로 시작한다.
  const [open, setOpen] = useState(active)

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className={`flex items-center gap-2 w-full h-9 px-3 rounded-md text-sm transition-colors ${active ? 'text-[#1677ff] font-medium' : 'text-neutral-600 hover:bg-neutral-100'}`}
      >
        <Icon className="w-4 h-4" />
        <span>{label}</span>
        <ChevronDown className={`w-3.5 h-3.5 ml-auto transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && (
        <div className="mt-0.5 space-y-0.5 pl-3">
          {items.map((it) => (
            <NavLinkRow key={it.to} {...it} />
          ))}
        </div>
      )}
    </div>
  )
}

function NavSection({ label, children }: { label?: string; children: ReactNode }) {
  return (
    <div className="pt-3 first:pt-0">
      {label && <div className="px-3 pb-1 text-[11px] font-medium text-neutral-400">{label}</div>}
      <div className="space-y-0.5">{children}</div>
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
  const location = useLocation()
  const switchTarget = (t: ProjectTarget) => {
    setTarget(t)
    onTargetChange(t)
  }

  // 미게시 변경(draft) 개수 — 어느 화면에서든 사이드바 배지와 상단 배너로 안내한다.
  // 실제 조회·폴링은 PendingChangesProvider 하나가 맡는다 — 각 관리 화면도 같은 값을 구독하므로,
  // 거기서 변경을 담거나 취소하면 이 배지도 그 자리에서 같이 바뀐다.
  const { drafts } = usePendingChanges()
  const pendingCount = drafts?.length ?? null

  const showPendingBanner = !!pendingCount && pendingCount > 0 && location.pathname !== '/schema-changes'

  return (
    <div className="h-screen flex overflow-hidden">
      {/* 사이드바는 화면 높이에 고정한다 — 본문이 길어져도 늘어나며 아래 항목들을
          화면 밖으로 밀어내지 않도록. 스크롤은 본문 쪽(아래 div)에서만 일어난다. */}
      <aside className="w-56 shrink-0 h-full bg-white border-r border-neutral-200 flex flex-col">
        <div className="h-14 flex items-center px-4 border-b border-neutral-200">
          <span className="font-semibold text-neutral-900">트루베이스</span>
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

        <nav className="flex-1 overflow-y-auto px-3 py-3">
          <NavSection>
            <NavLinkRow to="/dashboard" label="대시보드" icon={LayoutDashboard} />
          </NavSection>

          <NavSection label="빌드">
            <NavLinkRow to="/data-management" label="데이터 관리" icon={Database} />
            <NavLinkRow to="/leaderboard" label="리더보드" icon={Trophy} />
            <NavLinkRow to="/remote-config" label="원격 설정" icon={Settings} />
          </NavSection>

          <NavSection label="운영">
            <NavLinkRow to="/players" label="플레이어" icon={Users} />
            <NavLinkRow to="/purchases" label="구매 내역" icon={ReceiptText} />
            <NavLinkRow to="/" label="아이템 카탈로그" icon={Package} end />
            <NavLinkRow to="/coupons" label="쿠폰" icon={Ticket} />
            <NavLinkRow to="/chat" label="채팅 관리" icon={MessageSquare} />
            <NavGroupBlock
              label="우편함"
              icon={Mail}
              items={[
                { to: '/mails/send', label: '우편 발송', icon: Send },
                { to: '/mails/records', label: '우편 내역', icon: Inbox },
                { to: '/mails/schedules', label: '예약 목록', icon: CalendarClock },
                { to: '/mails/categories', label: '우편 분류', icon: Tags },
              ]}
            />
          </NavSection>

          <NavSection label="모니터링">
            <NavLinkRow to="/data-logs" label="데이터 로그" icon={ScrollText} />
          </NavSection>
        </nav>

        {/* 스크롤 영역 밖에 고정 — 메뉴가 길어져도 변경 관리는 항상 보인다. */}
        <div className="border-t border-neutral-200 p-3 space-y-0.5">
          <NavLinkRow to="/schema-changes" label="변경 관리" icon={History} badge={pendingCount ?? undefined} />
          {/* 운영자 관리는 마스터만. 서버도 같은 조건으로 다시 막는다. */}
          {isMaster && <NavLinkRow to="/operators" label="운영자 관리" icon={UserCog} />}
        </div>

        <div className="border-t border-neutral-200 p-3 space-y-0.5">
          <a
            href="https://trsoftkorea.github.io/TrueSoft.Supabase/"
            target="_blank"
            rel="noreferrer"
            className={`${NAV_BASE} text-neutral-600 hover:bg-neutral-100`}
          >
            <BookOpen className="w-4 h-4" />
            <span>SDK 가이드 문서</span>
          </a>
        </div>

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

      <div className="flex-1 min-w-0 overflow-y-auto">
        <main className="max-w-6xl mx-auto px-6 py-6">
          {showPendingBanner && (
            <NavLink
              to="/schema-changes"
              className="mb-4 flex items-center gap-2 rounded-md border border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-800 hover:bg-amber-100 transition-colors"
            >
              <AlertTriangle className="w-4 h-4 shrink-0" />
              <span>미게시 변경 {pendingCount}건이 있습니다. 변경 관리에서 검토 후 게시하세요.</span>
              <span className="ml-auto text-xs text-amber-600 underline">게시하러 가기</span>
            </NavLink>
          )}
          {children}
        </main>
      </div>
    </div>
  )
}
