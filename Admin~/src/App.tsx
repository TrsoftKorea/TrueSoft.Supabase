import { useCallback, useEffect, useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { getTarget, setTarget, type ProjectTarget } from './lib/projectTarget'
import { clearToken, getToken, signedInEmail, signedInTargets } from './lib/googleAuth'
import { callAdmin, NotAuthenticatedError, type SignInOutcome } from './lib/api'
import { PendingChangesProvider } from './lib/pendingChanges'
import { refreshProjectLooks } from './lib/projectLook'
import { Layout } from './components/Layout'
import Login from './pages/Login'
import ProjectSelect from './pages/ProjectSelect'
import GameItems from './pages/GameItems'
import Operators from './pages/Operators'
import Players from './pages/Players'
import Mails from './pages/Mails'
import MailRecords from './pages/MailRecords'
import MailCategories from './pages/MailCategories'
import MailSchedules from './pages/MailSchedules'
import Leaderboard from './pages/Leaderboard'
import SchemaChanges from './pages/SchemaChanges'
import Purchases from './pages/Purchases'
import RemoteConfig from './pages/RemoteConfig'
import Coupons from './pages/Coupons'
import ChatModeration from './pages/ChatModeration'
import Settings from './pages/Settings'
import Lobbies from './pages/Lobbies'
import MatchRewards from './pages/MatchRewards'
import Dashboard from './pages/Dashboard'
import DataLogs from './pages/DataLogs'
import CronJobs from './pages/CronJobs'
import DataManagement from './pages/DataManagement'
import GameServers from './pages/GameServers'
import AuditLogs from './pages/AuditLogs'
import BannedWords from './pages/BannedWords'

type Me = { email: string; isMaster: boolean }

const ENTERED_KEY = 'tb_admin_entered'
// 게임별 로그인 결과(실패 사유 포함). 로그인 표와 같이 탭 단위로 기억해 새로고침 뒤에도 사유가 남게 한다.
const OUTCOMES_KEY = 'tb_admin_signin_outcomes'

function readOutcomes(): SignInOutcome[] {
  try {
    const raw = window.sessionStorage.getItem(OUTCOMES_KEY)
    return raw ? (JSON.parse(raw) as SignInOutcome[]) : []
  } catch {
    return []
  }
}

export default function App() {
  const [target, setTargetState] = useState<ProjectTarget>(() => getTarget())
  const [signedIn, setSignedIn] = useState(() => signedInTargets().length > 0)
  // 게임 목록에서 하나를 골라 들어갔는지. 새로고침해도 같은 게임 안에 머물도록 탭 단위로 기억한다.
  const [entered, setEntered] = useState(() => window.sessionStorage.getItem(ENTERED_KEY) === '1')
  const [me, setMe] = useState<Me | null>(null)
  const [meError, setMeError] = useState('')
  const [outcomes, setOutcomes] = useState<SignInOutcome[]>(readOutcomes)

  const inside = signedIn && entered && getToken(target) !== null

  const signOut = useCallback(() => {
    clearToken()
    window.sessionStorage.removeItem(ENTERED_KEY)
    window.sessionStorage.removeItem(OUTCOMES_KEY)
    setOutcomes([])
    setSignedIn(false)
    setEntered(false)
    setMe(null)
  }, [])

  const enter = useCallback((t: ProjectTarget) => {
    setTarget(t)
    setTargetState(t)
    window.sessionStorage.setItem(ENTERED_KEY, '1')
    setEntered(true)
  }, [])

  // 로그인은 그대로 두고 게임 목록으로만 돌아간다.
  const backToList = useCallback(() => {
    window.sessionStorage.removeItem(ENTERED_KEY)
    setEntered(false)
    setMe(null)
  }, [])

  const onSignedIn = useCallback((results: SignInOutcome[]) => {
    window.sessionStorage.setItem(OUTCOMES_KEY, JSON.stringify(results))
    setOutcomes(results)
    window.sessionStorage.removeItem(ENTERED_KEY)
    setEntered(false)
    setSignedIn(true)
  }, [])

  // 게임 색·이름·아이콘은 로그인 화면에서도 쓰므로 로그인 여부와 관계없이 처음 한 번 받는다.
  useEffect(() => { void refreshProjectLooks() }, [])

  // 로그인 세션 토큰(24시간)이 모든 게임에서 만료되면 로그인 화면으로 돌려보낸다.
  useEffect(() => {
    if (!signedIn) return
    const id = window.setInterval(() => {
      if (signedInTargets().length === 0) signOut()
    }, 30_000)
    return () => window.clearInterval(id)
  }, [signedIn, signOut])

  // 인가 결과(마스터 여부)는 서버가 정한다. 여기서 토큰을 뜯어 판단하지 않는다.
  useEffect(() => {
    if (!inside) return
    let alive = true
    setMe(null)
    setMeError('')
    callAdmin<Me>(target, 'session.me')
      .then((data) => {
        if (alive) setMe(data)
      })
      .catch((e: unknown) => {
        if (!alive) return
        if (e instanceof NotAuthenticatedError) {
          signOut()
          return
        }
        setMeError(e instanceof Error ? e.message : '권한을 확인하지 못했습니다.')
      })
    return () => {
      alive = false
    }
  }, [inside, target, signOut])

  if (!signedIn) return <Login onSignedIn={onSignedIn} />
  if (!inside) return <ProjectSelect outcomes={outcomes} onEnter={enter} onSignOut={signOut} />

  // 권한 확인에 실패하면(대개 403) 화면 대신 사유를 보여준다.
  if (meError) {
    return (
      <div className="min-h-screen flex items-center justify-center p-6">
        <div className="w-full max-w-sm space-y-3 text-center">
          <p className="text-sm text-red-600">{meError}</p>
          <p className="text-xs text-neutral-500">{signedInEmail()}</p>
          <button
            onClick={signOut}
            className="h-9 px-4 rounded-md border border-neutral-300 text-sm text-neutral-700"
          >
            다른 계정으로 로그인
          </button>
        </div>
      </div>
    )
  }

  if (!me) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <Loader2 className="w-6 h-6 animate-spin text-neutral-300" />
      </div>
    )
  }

  return (
    <PendingChangesProvider target={target} onUnauthenticated={signOut}>
    <Layout
      target={target}
      onTargetChange={setTargetState}
      outcomes={outcomes}
      email={me.email}
      isMaster={me.isMaster}
      onSignOut={signOut}
      onBackToList={backToList}
    >
      <Routes>
        <Route path="/" element={<Dashboard target={target} onUnauthenticated={signOut} />} />
        <Route path="/items" element={<GameItems target={target} onUnauthenticated={signOut} />} />
        <Route path="/servers" element={<GameServers target={target} onUnauthenticated={signOut} />} />
        <Route path="/audit" element={<AuditLogs target={target} onUnauthenticated={signOut} />} />
        <Route path="/banned-words" element={<BannedWords target={target} onUnauthenticated={signOut} />} />
        <Route path="/players" element={<Players target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/send" element={<Mails target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/records" element={<MailRecords target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/categories" element={<MailCategories target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/schedules" element={<MailSchedules target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/settings" element={<Navigate to="/settings" replace />} />
        <Route path="/leaderboard" element={<Leaderboard target={target} onUnauthenticated={signOut} />} />
        <Route path="/purchases" element={<Purchases target={target} onUnauthenticated={signOut} />} />
        <Route path="/remote-config" element={<RemoteConfig target={target} onUnauthenticated={signOut} />} />
        <Route path="/coupons" element={<Coupons target={target} onUnauthenticated={signOut} />} />
        <Route path="/chat" element={<ChatModeration target={target} onUnauthenticated={signOut} />} />
        <Route path="/settings" element={<Settings target={target} onUnauthenticated={signOut} />} />
        <Route path="/friends" element={<Navigate to="/settings" replace />} />
        <Route path="/lobbies" element={<Lobbies target={target} onUnauthenticated={signOut} />} />
        <Route path="/match-rewards" element={<MatchRewards target={target} onUnauthenticated={signOut} />} />
        {/* 예전 주소를 북마크해 둔 운영자가 빈 화면을 보지 않도록 남겨 둔다. */}
        <Route path="/social" element={<Navigate to="/settings" replace />} />
        <Route path="/dashboard" element={<Navigate to="/" replace />} />
        <Route path="/data-logs" element={<DataLogs target={target} onUnauthenticated={signOut} />} />
        <Route path="/jobs" element={<CronJobs target={target} onUnauthenticated={signOut} />} />
        <Route path="/data-management" element={<DataManagement target={target} onUnauthenticated={signOut} />} />
        <Route path="/schema-changes" element={<SchemaChanges target={target} onUnauthenticated={signOut} />} />
        <Route
          path="/operators"
          element={
            me.isMaster ? (
              <Operators target={target} masterEmail={me.email} onUnauthenticated={signOut} />
            ) : (
              <Navigate to="/" replace />
            )
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Layout>
    </PendingChangesProvider>
  )
}
