import { useState, type ReactNode, type ComponentType } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import {
  LayoutDashboard, Database, Trophy, Settings, Users, ReceiptText, Package, Ticket,
  MessageSquare, Mail, Send, Inbox, Tags, CalendarClock, ScrollText, History, UserCog,
  Swords, Medal, SlidersHorizontal, Server, ClipboardList, ShieldBan,
  ChevronDown, LogOut, AlertTriangle, BookOpen, LayoutGrid, Timer, BarChart3,
} from 'lucide-react'
import { PROJECTS, setTarget, type ProjectTarget } from '../lib/projectTarget'
import { useProjectLook } from '../lib/projectLook'
import { ProjectIcon } from './ui/ProjectIcon'
import { ThemeToggle } from './ThemeToggle'
import { signedInTargets } from '../lib/googleAuth'
import { blockedReason, type SignInOutcome } from '../lib/api'
import { usePendingChanges } from '../lib/pendingChanges'

// 사이드바는 어두운 남색 바탕이다. 글자색은 바탕 위에서 4.5:1 이상이 되게 골랐다.
const SIDEBAR_BG = '#161a2b'
const NAV_BASE = 'flex items-center gap-2 h-9 px-3 rounded-md text-sm transition-colors'
const NAV_IDLE = 'text-[#c7cbe0] hover:bg-white/5 hover:text-white'
const NAV_ACTIVE = 'bg-[#262c45] text-white font-medium'

type Icon = ComponentType<{ className?: string }>
type NavItem = { to: string; label: string; icon: Icon; end?: boolean }

function NavLinkRow({ to, label, icon: Icon, end = false, badge, accent }: NavItem & { badge?: number | undefined; accent: string }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) => `${NAV_BASE} ${isActive ? NAV_ACTIVE : NAV_IDLE}`}
      style={({ isActive }) => (isActive ? { boxShadow: `inset 3px 0 0 ${accent}` } : undefined)}
    >
      <Icon className="w-4 h-4" />
      <span>{label}</span>
      {!!badge && (
        <span className="ml-auto inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full bg-amber-400 text-[#161a2b] text-[11px] font-semibold leading-none">
          {badge}
        </span>
      )}
    </NavLink>
  )
}

function NavGroupBlock({ label, icon: Icon, items, accent }: { label: string; icon: Icon; items: NavItem[]; accent: string }) {
  const location = useLocation()
  const active = items.some((it) => location.pathname.startsWith(it.to))
  // 현재 화면이 이 그룹 소속이면 펼친 채로 시작한다.
  const [open, setOpen] = useState(active)

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className={`flex items-center gap-2 w-full h-9 px-3 rounded-md text-sm transition-colors ${active ? 'text-white font-medium' : NAV_IDLE}`}
      >
        <Icon className="w-4 h-4" />
        <span>{label}</span>
        <ChevronDown className={`w-3.5 h-3.5 ml-auto transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && (
        <div className="mt-0.5 space-y-0.5 pl-3">
          {items.map((it) => (
            <NavLinkRow key={it.to} {...it} accent={accent} />
          ))}
        </div>
      )}
    </div>
  )
}

function NavSection({ label, children }: { label?: string; children: ReactNode }) {
  return (
    <div className="pt-4 first:pt-0">
      {label && <div className="px-3 pb-1 text-[11px] font-medium text-[#8d93b0]">{label}</div>}
      <div className="space-y-0.5">{children}</div>
    </div>
  )
}

export function Layout({
  target,
  onTargetChange,
  outcomes,
  email,
  isMaster,
  onSignOut,
  onBackToList,
  children,
}: {
  target: ProjectTarget
  onTargetChange: (t: ProjectTarget) => void
  outcomes: SignInOutcome[]
  email: string
  isMaster: boolean
  onSignOut: () => void
  onBackToList: () => void
  children: ReactNode
}) {
  const location = useLocation()
  const look = useProjectLook()
  const colorOf = look.color
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
  const accent = colorOf(target)
  // 로그인 때 표를 받은 게임만 오갈 수 있다. 나머지는 그 게임에서 받은 실패 사유를 붙인다.
  const allowedTargets = new Set(signedInTargets())

  return (
    <div className="h-screen flex overflow-hidden">
      {/* 사이드바는 화면 높이에 고정한다 — 본문이 길어져도 늘어나며 아래 항목들을
          화면 밖으로 밀어내지 않도록. 스크롤은 본문 쪽(아래 div)에서만 일어난다. */}
      <aside className="w-56 shrink-0 h-full flex flex-col text-[#c7cbe0]" style={{ backgroundColor: SIDEBAR_BG }}>
        <div className="h-14 flex items-center gap-2.5 px-4">
          <span
            className="w-7 h-7 rounded-md flex items-center justify-center text-white text-sm font-bold"
            style={{ backgroundColor: accent }}
          >
            T
          </span>
          <span className="font-semibold text-white">트루베이스</span>
        </div>

        {/* 게임 고르기 — 색 점으로 게임을 구분한다. 지금 게임은 테두리도 그 색으로. */}
        <div className="px-3 pb-2 space-y-1">
          <div className="px-1 pb-0.5 text-[11px] font-medium text-[#8d93b0]">게임</div>
          {PROJECTS.map((p) => {
            const on = p.key === target
            const allowed = allowedTargets.has(p.key)
            const reason = allowed ? null : blockedReason(outcomes.find((o) => o.target === p.key))
            const c = colorOf(p.key)
            return (
              <button
                key={p.key}
                type="button"
                disabled={!allowed}
                title={reason?.full}
                onClick={() => { if (!on) switchTarget(p.key) }}
                aria-pressed={on}
                className={`w-full flex items-center gap-2.5 h-9 px-3 rounded-md text-sm border transition-colors disabled:cursor-not-allowed disabled:text-[#8d93b0] ${
                  on ? 'bg-[#262c45] text-white font-semibold' : 'border-[#2c3250] hover:enabled:bg-white/5 hover:enabled:text-white'
                }`}
                style={on ? { borderColor: c } : undefined}
              >
                <ProjectIcon target={p.key} size={20} dim={!allowed} />
                <span className="truncate">{look.name(p.key)}</span>
                {reason && <span className="ml-auto text-[11px] whitespace-nowrap">{reason.short}</span>}
              </button>
            )
          })}
        </div>

        <nav className="flex-1 overflow-y-auto px-3 py-3">
          <NavSection>
            <NavLinkRow to="/" label="대시보드" icon={LayoutDashboard} end accent={accent} />
            <NavLinkRow to="/stats" label="통계" icon={BarChart3} accent={accent} />
          </NavSection>

          <NavSection label="빌드">
            <NavLinkRow to="/data-management" label="데이터 관리" icon={Database} accent={accent} />
            <NavLinkRow to="/leaderboard" label="리더보드" icon={Trophy} accent={accent} />
            <NavLinkRow to="/remote-config" label="원격 설정" icon={Settings} accent={accent} />
          </NavSection>

          <NavSection label="운영">
            <NavLinkRow to="/players" label="플레이어" icon={Users} accent={accent} />
            <NavLinkRow to="/purchases" label="구매 내역" icon={ReceiptText} accent={accent} />
            <NavLinkRow to="/items" label="아이템 카탈로그" icon={Package} accent={accent} />
            <NavLinkRow to="/coupons" label="쿠폰" icon={Ticket} accent={accent} />
            <NavLinkRow to="/servers" label="게임 서버" icon={Server} accent={accent} />
            <NavGroupBlock
              label="우편함"
              icon={Mail}
              accent={accent}
              items={[
                { to: '/mails/send', label: '우편 발송', icon: Send },
                { to: '/mails/records', label: '우편 내역', icon: Inbox },
                { to: '/mails/schedules', label: '예약 목록', icon: CalendarClock },
                { to: '/mails/categories', label: '우편 분류', icon: Tags },
              ]}
            />
          </NavSection>

          <NavSection label="소셜">
            <NavLinkRow to="/chat" label="채팅 관리" icon={MessageSquare} accent={accent} />
            <NavLinkRow to="/banned-words" label="금칙어" icon={ShieldBan} accent={accent} />
          </NavSection>

          <NavSection label="매치">
            <NavLinkRow to="/lobbies" label="매치 로비" icon={Swords} accent={accent} />
            <NavLinkRow to="/match-rewards" label="매치 보상" icon={Medal} accent={accent} />
          </NavSection>

          <NavSection label="모니터링">
            <NavLinkRow to="/data-logs" label="데이터 로그" icon={ScrollText} accent={accent} />
            <NavLinkRow to="/audit" label="운영 기록" icon={ClipboardList} accent={accent} />
            <NavLinkRow to="/jobs" label="자동 작업" icon={Timer} accent={accent} />
          </NavSection>
        </nav>

        {/* 스크롤 영역 밖에 고정 — 메뉴가 길어져도 변경 관리는 항상 보인다. */}
        <div className="border-t border-[#2c3250] p-3 space-y-0.5">
          <NavLinkRow to="/settings" label="설정" icon={SlidersHorizontal} accent={accent} />
          <NavLinkRow to="/schema-changes" label="변경 관리" icon={History} badge={pendingCount ?? undefined} accent={accent} />
          {/* 운영자 관리는 마스터만. 서버도 같은 조건으로 다시 막는다. */}
          {isMaster && <NavLinkRow to="/operators" label="운영자 관리" icon={UserCog} accent={accent} />}
          <a
            href="https://trsoftkorea.github.io/TrueSoft.Supabase/"
            target="_blank"
            rel="noreferrer"
            className={`${NAV_BASE} ${NAV_IDLE}`}
          >
            <BookOpen className="w-4 h-4" />
            <span>SDK 가이드 문서</span>
          </a>
        </div>
      </aside>

      <div className="flex-1 min-w-0 overflow-y-auto bg-neutral-100">
        {/* 상단 띠 — 지금 어느 게임에서 작업 중인지 화면 어디서든 보이게 그 게임 색으로 깐다. */}
        <header className="text-white" style={{ backgroundColor: accent }}>
          <div className="max-w-6xl mx-auto px-6 h-14 flex items-center gap-3">
            <button
              type="button"
              onClick={onBackToList}
              title="게임 목록으로"
              className="shrink-0 inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-white/60 text-sm text-white hover:bg-white/10 whitespace-nowrap"
            >
              <LayoutGrid className="w-3.5 h-3.5" />
              게임 목록
            </button>
            <ProjectIcon target={target} size={32} className="ring-2 ring-white/70" />
            <span className="text-lg font-bold tracking-tight truncate">{look.name(target)}</span>
            <div className="ml-auto flex items-center gap-3 text-sm min-w-0">
              <span className="truncate">
                {email}
                {isMaster && <span className="ml-1.5 font-semibold">· 마스터</span>}
              </span>
              <ThemeToggle compact className="shrink-0 border-white/60 text-white hover:bg-white/10" />
              <button
                onClick={onSignOut}
                className="shrink-0 inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-white/60 text-sm text-white hover:bg-white/10 whitespace-nowrap"
              >
                <LogOut className="w-3.5 h-3.5" />
                로그아웃
              </button>
            </div>
          </div>
        </header>

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
