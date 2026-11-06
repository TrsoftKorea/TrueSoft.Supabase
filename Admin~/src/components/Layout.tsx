import { startTransition, useEffect, useState, type ReactNode, type ComponentType, type CSSProperties } from 'react'
import { NavLink, useLocation, useSearchParams } from 'react-router-dom'
import {
  LayoutDashboard, Database, FilePen, Trophy, Settings, Users, ReceiptText, Package, Ticket,
  MessageSquare, Send, Inbox, Tags, CalendarClock, ScrollText, History, UserCog,
  Swords, Medal, SlidersHorizontal, Server, ClipboardList, ShieldBan,
  UserRound, MessagesSquare, Mail, Gift, Crown, Settings2, Gauge,
  ChevronDown, LogOut, AlertTriangle, BookOpen, LayoutGrid, Timer, BarChart3, Headset, Activity,
} from 'lucide-react'
import { PROJECTS, setTarget, type ProjectTarget } from '../lib/projectTarget'
import { useProjectLook } from '../lib/projectLook'
import { ProjectIcon } from './ui/ProjectIcon'
import { ThemeToggle } from './ThemeToggle'
import { signedInTargets } from '../lib/googleAuth'
import { blockedReason, type SignInOutcome } from '../lib/api'
import { usePendingChanges } from '../lib/pendingChanges'
import { PublishBar } from '../mock/PublishBar'
import { dropGameParams } from './ui/PlayerLink'

// 사이드바 색은 화면 모드를 따른다 — 값은 index.css 의 .sidebar 변수(--sb-*)에 있다.
const NAV_BASE = 'flex items-center gap-2 h-9 px-3 rounded-md text-sm transition-colors'
const NAV_IDLE = 'text-[color:var(--sb-text)] hover:bg-[var(--sb-hover)] hover:text-[color:var(--sb-text-strong)]'
const NAV_ACTIVE = 'bg-[var(--sb-active-bg)] text-[color:var(--sb-active-text)] font-medium'

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
      <Icon className="w-4 h-4 shrink-0" />
      <span className="whitespace-nowrap">{label}</span>
      {!!badge && (
        <span className="ml-auto inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full bg-amber-400 text-[#161a2b] text-[11px] font-semibold leading-none">
          {badge}
        </span>
      )}
    </NavLink>
  )
}

// 운영자가 하러 온 일로 묶는다. 대시보드·통계는 묶음 밖에 늘 보인다.
const NAV_SECTIONS: { label: string; icon: Icon; items: NavItem[] }[] = [
  {
    label: '플레이어',
    icon: UserRound,
    items: [
      { to: '/players', label: '플레이어 목록', icon: Users },
      { to: '/player-data', label: '플레이어 데이터', icon: FilePen },
      { to: '/purchases', label: '구매 내역', icon: ReceiptText },
      { to: '/data-logs', label: '데이터 로그', icon: ScrollText },
    ],
  },
  {
    label: '커뮤니티',
    icon: MessagesSquare,
    items: [
      { to: '/chat', label: '채팅 관리', icon: MessageSquare },
      { to: '/banned-words', label: '금칙어', icon: ShieldBan },
    ],
  },
  {
    label: '우편',
    icon: Mail,
    items: [
      { to: '/mails/send', label: '우편 발송', icon: Send },
      { to: '/mails/records', label: '우편 내역', icon: Inbox },
      { to: '/mails/schedules', label: '예약 목록', icon: CalendarClock },
      { to: '/mails/categories', label: '우편 분류', icon: Tags },
    ],
  },
  {
    label: '보상',
    icon: Gift,
    items: [
      { to: '/coupons', label: '쿠폰', icon: Ticket },
      { to: '/items', label: '아이템 카탈로그', icon: Package },
    ],
  },
  {
    label: '매치·랭킹',
    icon: Crown,
    items: [
      { to: '/leaderboard', label: '리더보드', icon: Trophy },
      { to: '/lobbies', label: '매치 로비', icon: Swords },
      { to: '/match-rewards', label: '매치 보상', icon: Medal },
    ],
  },
  {
    label: '게임 설정',
    icon: Settings2,
    items: [
      { to: '/remote-config', label: '원격 설정', icon: Settings },
      { to: '/player-data-fields', label: '플레이어 데이터 필드', icon: Database },
      { to: '/servers', label: '게임 서버', icon: Server },
    ],
  },
  {
    label: '모니터링',
    icon: Gauge,
    items: [
      { to: '/server', label: '서버 상태', icon: Activity },
      { to: '/jobs', label: '자동 작업', icon: Timer },
      { to: '/audit', label: '운영 기록', icon: ClipboardList },
    ],
  },
]

// 운영자가 직접 펼친 묶음만 기억한다. 처음에는 전부 접혀 있다.
const OPEN_KEY = 'tb_sidebar_open'

function loadOpen(): string[] {
  try {
    const raw = window.localStorage.getItem(OPEN_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : []
  } catch {
    return []
  }
}

function saveOpen(labels: string[]) {
  try {
    window.localStorage.setItem(OPEN_KEY, JSON.stringify(labels))
  } catch {
    // 저장이 막힌 브라우저에서는 새로 열 때 다시 접힐 뿐이다.
  }
}

// '/server' 가 '/servers' 까지 잡지 않게 경로 단위로 본다.
const isUnder = (pathname: string, to: string) => pathname === to || pathname.startsWith(`${to}/`)

const sectionHasPath = (items: NavItem[], pathname: string) => items.some((it) => isUnder(pathname, it.to))
const sectionOf = (pathname: string) => NAV_SECTIONS.find((s) => sectionHasPath(s.items, pathname))?.label ?? null

function NavSection({ label, icon: Icon, open, active, onToggle, children }: {
  label: string; icon: Icon; open: boolean; active: boolean; onToggle: () => void; children: ReactNode
}) {
  return (
    <div>
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        className={`${NAV_BASE} w-full ${active && !open ? 'text-[color:var(--sb-text-strong)] font-medium' : NAV_IDLE}`}
      >
        <Icon className="w-4 h-4" />
        <span>{label}</span>
        <ChevronDown className={`w-3.5 h-3.5 ml-auto transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && <div className="mt-0.5 space-y-0.5 pl-3">{children}</div>}
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
  const [, setSearchParams] = useSearchParams()
  const switchTarget = (t: ProjectTarget) => {
    setTarget(t)
    // 한 번에 그린다 — 라우터의 주소 변경은 늦게 그려져서, 따로 하면 새 게임의 화면이 앞 게임 주소로 먼저 만들어진다.
    startTransition(() => {
      onTargetChange(t)
      setSearchParams(dropGameParams, { replace: true })
    })
  }

  // 미게시 변경(draft) 개수 — 어느 화면에서든 사이드바 배지와 상단 배너로 안내한다.
  // 실제 조회는 PendingChangesProvider 하나가 맡는다 — 각 관리 화면도 같은 값을 구독하므로,
  // 거기서 변경을 담거나 취소하면 이 배지도 그 자리에서 같이 바뀐다.
  const { drafts } = usePendingChanges()
  const pendingCount = drafts?.length ?? null

  const showPendingBanner = !!pendingCount && pendingCount > 0 && location.pathname !== '/schema-changes'
  const accent = colorOf(target)
  // 로그인 때 표를 받은 게임만 오갈 수 있다. 나머지는 그 게임에서 받은 실패 사유를 붙인다.
  const allowedTargets = new Set(signedInTargets())

  const [savedOpen, setSavedOpen] = useState<string[]>(loadOpen)
  // 지금 화면이 든 묶음은 위치가 보이게 잠시 편다. 기억하지 않아서 다른 화면으로 가면 다시 접힌다.
  const [currentOpen, setCurrentOpen] = useState<string | null>(() => sectionOf(location.pathname))
  useEffect(() => { setCurrentOpen(sectionOf(location.pathname)) }, [location.pathname])

  const isOpen = (label: string) => savedOpen.includes(label) || currentOpen === label
  const toggleSection = (label: string) => {
    const next = isOpen(label) ? savedOpen.filter((l) => l !== label) : [...savedOpen, label]
    if (currentOpen === label) setCurrentOpen(null)
    setSavedOpen(next)
    saveOpen(next)
  }

  return (
    <div className="h-screen flex overflow-hidden">
      {/* 사이드바는 화면 높이에 고정한다 — 본문이 길어져도 늘어나며 아래 항목들을
          화면 밖으로 밀어내지 않도록. 스크롤은 본문 쪽(아래 div)에서만 일어난다. */}
      <aside
        className="sidebar w-60 shrink-0 h-full flex flex-col border-r border-[color:var(--sb-border)] text-[color:var(--sb-text)]"
        style={{ backgroundColor: 'var(--sb-bg)', '--sb-accent': accent } as CSSProperties}
      >
        <div className="h-14 flex items-center gap-2.5 px-4">
          <span
            className="w-7 h-7 rounded-md flex items-center justify-center text-white text-sm font-bold"
            style={{ backgroundColor: accent }}
          >
            T
          </span>
          <span className="font-semibold text-[color:var(--sb-text-strong)]">트루베이스</span>
        </div>

        {/* 게임 고르기 — 색 점으로 게임을 구분한다. 지금 게임은 테두리도 그 색으로. */}
        <div className="px-3 pb-2 space-y-1">
          <div className="px-1 pb-0.5 text-[11px] font-medium text-[color:var(--sb-muted)]">프로젝트</div>
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
                className={`w-full flex items-center gap-2.5 h-9 px-3 rounded-md text-sm border transition-colors disabled:cursor-not-allowed disabled:text-[color:var(--sb-muted)] ${
                  on
                    ? 'bg-[var(--sb-active-bg)] text-[color:var(--sb-text-strong)] font-semibold'
                    : 'border-[color:var(--sb-border)] hover:enabled:bg-[var(--sb-hover)] hover:enabled:text-[color:var(--sb-text-strong)]'
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

        <nav className="flex-1 overflow-y-auto px-3 py-3 scroll-area scroll-area-dark">
          <div className="space-y-0.5">
            <NavLinkRow to="/" label="대시보드" icon={LayoutDashboard} end accent={accent} />
            <NavLinkRow to="/stats" label="통계" icon={BarChart3} accent={accent} />
          </div>

          <div className="mt-3 space-y-0.5">
            {NAV_SECTIONS.map((s) => (
              <NavSection
                key={s.label}
                label={s.label}
                icon={s.icon}
                open={isOpen(s.label)}
                active={sectionHasPath(s.items, location.pathname)}
                onToggle={() => toggleSection(s.label)}
              >
                {s.items.map((it) => <NavLinkRow key={it.to} {...it} accent={accent} />)}
              </NavSection>
            ))}
          </div>
        </nav>

        {/* 스크롤 영역 밖에 고정 — 자주 드나드는 고객센터와 변경 관리는 묶음을 접어도 항상 보인다. */}
        <div className="border-t border-[color:var(--sb-border)] p-3 space-y-0.5">
          <NavLinkRow to="/support" label="고객센터" icon={Headset} accent={accent} />
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

      <div className="flex-1 min-w-0 overflow-y-auto bg-neutral-100 scroll-area">
        {/* 상단 띠 — 지금 어느 게임에서 작업 중인지 화면 어디서든 보이게 그 게임 색으로 깐다. */}
        <header className="text-white" style={{ backgroundColor: accent }}>
          <div className="max-w-6xl mx-auto px-6 h-14 flex items-center gap-3">
            <button
              type="button"
              onClick={onBackToList}
              title="프로젝트 목록으로"
              className="shrink-0 inline-flex items-center gap-1.5 h-8 px-3 rounded-md border border-white/60 text-sm text-white hover:bg-white/10 whitespace-nowrap"
            >
              <LayoutGrid className="w-3.5 h-3.5" />
              프로젝트 목록
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

        {__MOCK__ && <PublishBar />}

        <main className="max-w-6xl mx-auto px-6 py-6">
          {showPendingBanner && (
            <NavLink
              to="/schema-changes"
              className="mb-4 flex items-center gap-2 rounded-md border border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-800 hover:bg-amber-100 transition-colors"
            >
              <AlertTriangle className="w-4 h-4 shrink-0" />
              <span>미게시 변경 {pendingCount}건</span>
              <span className="ml-auto text-xs text-amber-600 underline">게시하러 가기</span>
            </NavLink>
          )}
          {children}
        </main>
      </div>
    </div>
  )
}
