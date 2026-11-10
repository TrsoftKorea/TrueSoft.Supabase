import { useEffect, useState, type ReactNode, type ComponentType, type CSSProperties } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import {
  LayoutDashboard, Database, FilePen, Trophy, Settings, Users, ReceiptText, Package, Ticket,
  MessageSquare, Send, Inbox, Tags, CalendarClock, ScrollText, History, UserCog,
  Swords, Medal, SlidersHorizontal, Server, ClipboardList, ShieldBan,
  UserRound, MessagesSquare, Mail, Gift, Crown, Settings2, Gauge,
  ChevronDown, LogOut, AlertTriangle, BookOpen, Timer, BarChart3, Headset, Activity, Palette, Menu, PanelLeftClose, PanelLeftOpen,
} from 'lucide-react'
import { type ProjectTarget } from '../lib/projectTarget'
import { useProjectLook } from '../lib/projectLook'
import { ProjectIcon } from './ui/ProjectIcon'
import { ThemeToggle } from './ThemeToggle'
import { callAdmin } from '../lib/api'
import { usePendingChanges } from '../lib/pendingChanges'
import { PublishBar } from '../mock/PublishBar'

// 사이드바 색은 화면 모드를 따른다 — 값은 index.css 의 .sidebar 변수(--sb-*)에 있다.
const NAV_BASE = 'flex items-center gap-2 h-9 px-3 rounded-md text-sm transition-colors'
const NAV_IDLE = 'text-[color:var(--sb-text)] hover:bg-[var(--sb-hover)] hover:text-[color:var(--sb-text-strong)]'
const NAV_ACTIVE = 'bg-[var(--sb-active-bg)] text-[color:var(--sb-active-text)] font-medium'

type Icon = ComponentType<{ className?: string }>
type NavItem = { to: string; label: string; icon: Icon; end?: boolean }

// alert: 처리를 기다리는 일(답변 대기 문의 등) — 빨간 점에 개수. 아니면 노란 개수(미게시 변경처럼 알림 정도).
function NavLinkRow({ to, label, icon: Icon, end = false, badge, alert = false, accent }: NavItem & { badge?: number | undefined; alert?: boolean; accent: string }) {
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
        <span className={`ml-auto inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full text-[11px] font-semibold leading-none ${
          alert ? 'bg-red-500 text-white' : 'bg-amber-400 text-[#161a2b]'
        }`}>
          {badge > 99 ? '99+' : badge}
        </span>
      )}
    </NavLink>
  )
}

/**
 * 답변 대기 문의 수 — 고객센터 메뉴의 빨간 표시. 1분마다, 그리고 화면을 옮길 때(답변하고 나오면 바로 줄게) 다시 센다.
 * 못 세면 표시를 숨긴다(null).
 */
function useWaitingSupportCount(target: ProjectTarget, pathname: string): number | null {
  const [count, setCount] = useState<number | null>(null)
  useEffect(() => {
    let alive = true
    const read = () => {
      callAdmin<{ counts?: { open?: number } }>(target, 'support.list', { limit: 1, offset: 0 })
        .then((d) => { if (alive) setCount(typeof d.counts?.open === 'number' ? d.counts.open : null) })
        .catch(() => { if (alive) setCount(null) })
    }
    // 화면을 열거나 옮길 때는 늘 센다. 주기 재조회만 가려진 탭에서 건너뛴다.
    const tick = () => { if (!document.hidden) read() }
    read()
    const id = window.setInterval(tick, 60_000)
    document.addEventListener('visibilitychange', tick)
    return () => {
      alive = false
      window.clearInterval(id)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [target, pathname])
  return count
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

// 넓은 화면에서 메뉴를 접어 둔 것 — 이 브라우저에만 기억한다.
const COLLAPSED_KEY = 'tb_sidebar_collapsed'

function loadCollapsed(): boolean {
  try {
    return window.localStorage.getItem(COLLAPSED_KEY) === '1'
  } catch {
    return false
  }
}

function saveCollapsed(v: boolean) {
  try {
    window.localStorage.setItem(COLLAPSED_KEY, v ? '1' : '0')
  } catch {
    // 저장이 막힌 브라우저에서는 새로 열 때 다시 펴질 뿐이다.
  }
}

// 사이드바가 화면 옆에 붙어 있는 폭(lg) — 그 아래에서는 위에 덮어 여는 메뉴가 된다.
const isWide = () => typeof window !== 'undefined' && window.matchMedia('(min-width: 1024px)').matches

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
  email,
  isMaster,
  onSignOut,
  onBackToList,
  children,
}: {
  target: ProjectTarget
  email: string
  isMaster: boolean
  onSignOut: () => void
  onBackToList: () => void
  children: ReactNode
}) {
  const location = useLocation()
  const look = useProjectLook()

  // 미게시 변경(draft) 개수 — 어느 화면에서든 사이드바 배지와 상단 배너로 안내한다.
  // 실제 조회는 PendingChangesProvider 하나가 맡는다 — 각 관리 화면도 같은 값을 구독하므로,
  // 거기서 변경을 담거나 취소하면 이 배지도 그 자리에서 같이 바뀐다.
  const { drafts } = usePendingChanges()
  const pendingCount = drafts?.length ?? null

  const waitingSupport = useWaitingSupportCount(target, location.pathname)

  const showPendingBanner = !!pendingCount && pendingCount > 0 && location.pathname !== '/schema-changes'
  const accent = look.color(target)

  const [savedOpen, setSavedOpen] = useState<string[]>(loadOpen)
  // 지금 화면이 든 묶음은 위치가 보이게 잠시 편다. 기억하지 않아서 다른 화면으로 가면 다시 접힌다.
  const [currentOpen, setCurrentOpen] = useState<string | null>(() => sectionOf(location.pathname))
  useEffect(() => { setCurrentOpen(sectionOf(location.pathname)) }, [location.pathname])

  // 넓은 화면: 접기 버튼으로 사이드바를 감추고 위 띠의 메뉴 버튼으로 다시 편다.
  // 좁은 화면(lg 아래): 사이드바는 늘 접혀 있고 메뉴 버튼으로 위에 덮어 연다. 화면을 옮기면 닫는다.
  const [collapsed, setCollapsed] = useState(loadCollapsed)
  const setCollapsedSaved = (v: boolean) => { setCollapsed(v); saveCollapsed(v) }
  const openMenu = () => { if (isWide()) setCollapsedSaved(false); else setNavOpen(true) }
  const closeMenu = () => { if (isWide()) setCollapsedSaved(true); else setNavOpen(false) }
  const [navOpen, setNavOpen] = useState(false)
  useEffect(() => { setNavOpen(false) }, [location.pathname, target])
  useEffect(() => {
    if (!navOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setNavOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [navOpen])

  const isOpen = (label: string) => savedOpen.includes(label) || currentOpen === label
  const toggleSection = (label: string) => {
    const next = isOpen(label) ? savedOpen.filter((l) => l !== label) : [...savedOpen, label]
    if (currentOpen === label) setCurrentOpen(null)
    setSavedOpen(next)
    saveOpen(next)
  }

  return (
    <div className="h-screen flex overflow-hidden">
      {navOpen && <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setNavOpen(false)} aria-hidden />}
      {/* 사이드바는 화면 높이에 고정한다 — 본문이 길어져도 늘어나며 아래 항목들을
          화면 밖으로 밀어내지 않도록. 스크롤은 본문 쪽(아래 div)에서만 일어난다. */}
      <aside
        className={`sidebar w-60 shrink-0 h-full flex flex-col border-r border-[color:var(--sb-border)] text-[color:var(--sb-text)] fixed inset-y-0 left-0 z-40 transition-transform lg:static lg:translate-x-0 ${navOpen ? 'translate-x-0' : '-translate-x-full'} ${collapsed ? 'lg:hidden' : ''}`}
        style={{ backgroundColor: 'var(--sb-bg)', '--sb-accent': accent } as CSSProperties}
      >
        {/* 접기 버튼은 맨 왼쪽 — 접은 뒤 위 띠에 나오는 펴기 버튼과 같은 자리라 오가며 누르기 쉽다.
            "트루베이스"를 누르면 프로젝트 목록으로 간다 — 다른 프로젝트로 갈 때는 목록을 거친다. */}
        <div className="h-14 shrink-0 flex items-center gap-1.5 px-3">
          <button
            type="button"
            onClick={closeMenu}
            title="메뉴 접기"
            aria-label="메뉴 접기"
            className="shrink-0 inline-flex items-center justify-center w-8 h-8 rounded-md transition-colors hover:bg-[var(--sb-hover)] hover:text-[color:var(--sb-text-strong)]"
          >
            <PanelLeftClose className="w-4 h-4" />
          </button>
          <button
            type="button"
            onClick={onBackToList}
            title="프로젝트 목록"
            className="flex items-center gap-2.5 h-9 px-1.5 rounded-md transition-colors hover:bg-[var(--sb-hover)]"
          >
            <span
              className="w-7 h-7 shrink-0 rounded-md flex items-center justify-center text-white text-sm font-bold"
              style={{ backgroundColor: accent }}
            >
              T
            </span>
            <span className="font-semibold whitespace-nowrap text-[color:var(--sb-text-strong)]">트루베이스</span>
          </button>
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
          <NavLinkRow to="/support" label="고객센터" icon={Headset} badge={waitingSupport ?? undefined} alert accent={accent} />
          <NavLinkRow to="/settings" label="설정" icon={SlidersHorizontal} accent={accent} />
          <NavLinkRow to="/schema-changes" label="변경 관리" icon={History} badge={pendingCount ?? undefined} accent={accent} />
          {/* 운영자 관리는 마스터만. 서버도 같은 조건으로 다시 막는다. */}
          <NavLinkRow to="/operators" label="운영자 관리" icon={UserCog} accent={accent} />
          {__MOCK__ && <NavLinkRow to="/design-sample" label="디자인 견본" icon={Palette} accent={accent} />}
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
        <header className="text-white flex items-center" style={{ backgroundColor: accent }}>
          {/* 넓은 화면에서 메뉴를 접었을 때의 펴기 버튼 — 화면 왼쪽 끝, 접기 버튼이 있던 자리. 가운데 내용이 본문과
              줄이 맞게 오른쪽에 같은 폭의 빈칸을 둔다. */}
          {collapsed && (
            <button
              type="button"
              onClick={openMenu}
              title="메뉴 펴기"
              aria-label="메뉴 펴기"
              className="hidden lg:inline-flex shrink-0 ml-3 items-center justify-center w-8 h-8 rounded-md border border-white/60 text-white hover:bg-white/10"
            >
              <PanelLeftOpen className="w-4 h-4" />
            </button>
          )}
          {/* 좁은 화면에서는 글자를 접고 아이콘만 남긴다 — 이메일은 넓은 화면에서만. */}
          <div className="flex-1 min-w-0 max-w-6xl mx-auto px-4 sm:px-6 h-14 flex items-center gap-2 sm:gap-3">
            <button
              type="button"
              onClick={openMenu}
              aria-label="메뉴"
              className="lg:hidden shrink-0 inline-flex items-center justify-center w-8 h-8 rounded-md border border-white/60 text-white hover:bg-white/10"
            >
              <Menu className="w-4 h-4" />
            </button>
            <ProjectIcon target={target} size={32} className="ring-2 ring-white/70 shrink-0" />
            <span className="text-lg font-bold tracking-tight truncate">{look.name(target)}</span>
            <div className="ml-auto flex items-center gap-2 sm:gap-3 text-sm min-w-0">
              <span className="hidden md:inline truncate">
                {email}
                {isMaster && <span className="ml-1.5 font-semibold">· 마스터</span>}
              </span>
              <ThemeToggle compact className="shrink-0 border-white/60 text-white hover:bg-white/10" />
              <button
                onClick={onSignOut}
                title="로그아웃"
                className="shrink-0 inline-flex items-center gap-1.5 h-8 px-2 sm:px-3 rounded-md border border-white/60 text-sm text-white hover:bg-white/10 whitespace-nowrap"
              >
                <LogOut className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">로그아웃</span>
              </button>
            </div>
          </div>
          {collapsed && <div className="hidden lg:block shrink-0 w-8 mr-3" aria-hidden />}
        </header>

        {__MOCK__ && <PublishBar />}

        <main className="max-w-6xl mx-auto px-4 py-4 sm:px-6 sm:py-6">
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
