import { useCallback, useEffect, useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { getTarget, type ProjectTarget } from './lib/projectTarget'
import { clearToken, emailOf, getToken } from './lib/googleAuth'
import { callAdmin, NotAuthenticatedError } from './lib/api'
import { Layout } from './components/Layout'
import Login from './pages/Login'
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

type Me = { email: string; isMaster: boolean }

export default function App() {
  const [target, setTargetState] = useState<ProjectTarget>(() => getTarget())
  const [token, setToken] = useState<string | null>(() => getToken())
  const [me, setMe] = useState<Me | null>(null)
  const [meError, setMeError] = useState('')

  const signOut = useCallback(() => {
    clearToken()
    setToken(null)
    setMe(null)
  }, [])

  // 구글 ID 토큰은 1시간이면 만료된다. 만료되면 로그인 화면으로 돌려보낸다.
  useEffect(() => {
    if (!token) return
    const id = window.setInterval(() => {
      if (!getToken()) signOut()
    }, 30_000)
    return () => window.clearInterval(id)
  }, [token, signOut])

  // 인가 결과(마스터 여부)는 서버가 정한다. 여기서 토큰을 뜯어 판단하지 않는다.
  useEffect(() => {
    if (!token) return
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
  }, [token, target, signOut])

  if (!token) return <Login target={target} onToken={setToken} />

  // 권한 확인에 실패하면(대개 403) 화면 대신 사유를 보여준다.
  if (meError) {
    return (
      <div className="min-h-screen flex items-center justify-center p-6">
        <div className="w-full max-w-sm space-y-3 text-center">
          <p className="text-sm text-red-600">{meError}</p>
          <p className="text-xs text-neutral-500">{emailOf(token)}</p>
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
    <Layout
      target={target}
      onTargetChange={setTargetState}
      email={me.email}
      isMaster={me.isMaster}
      onSignOut={signOut}
    >
      <Routes>
        <Route path="/" element={<GameItems target={target} onUnauthenticated={signOut} />} />
        <Route path="/players" element={<Players target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/send" element={<Mails target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/records" element={<MailRecords target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/categories" element={<MailCategories target={target} onUnauthenticated={signOut} />} />
        <Route path="/mails/schedules" element={<MailSchedules target={target} onUnauthenticated={signOut} />} />
        <Route path="/leaderboard" element={<Leaderboard target={target} onUnauthenticated={signOut} />} />
        <Route path="/purchases" element={<Purchases target={target} onUnauthenticated={signOut} />} />
        <Route path="/remote-config" element={<RemoteConfig target={target} onUnauthenticated={signOut} />} />
        <Route path="/coupons" element={<Coupons target={target} onUnauthenticated={signOut} />} />
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
  )
}
