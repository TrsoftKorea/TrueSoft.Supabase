// 미게시 변경(draft) 목록 — 사이드바 배지·배너와 각 관리 화면(필드 관리·원격 설정·
// 리더보드·변경 관리)이 전부 같은 schema.getDraft 를 따로 불러오고 있었다. 여기 하나로
// 모아 한 번만 폴링하고, 화면들은 구독만 한다 — 한 화면에서 변경을 담거나 취소하면
// 사이드바 배지도 그 자리에서 같이 갱신된다(따로 5초를 기다리지 않는다).
import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react'
import { callAdmin, NotAuthenticatedError } from './api'
import type { ProjectTarget } from './projectTarget'

export type DraftRow = {
  id: number
  created_at?: string
  operator?: string | null
  feature: string
  action: string
  object_name: string
  params: Record<string, unknown>
  sort_order?: number
}

type Ctx = {
  /** null = 아직 한 번도 못 불러옴(로딩 중). */
  drafts: DraftRow[] | null
  /** 마지막 조회가 인증 실패가 아닌 이유로 실패했으면 그 메시지, 성공했으면 null.
   *  화면들이 이 값을 자기 에러 배너에 실어야 실패가 조용히 묻히지 않는다. */
  error: string | null
  reload: () => Promise<void>
}

const PendingChangesContext = createContext<Ctx | null>(null)

export function PendingChangesProvider({
  target,
  onUnauthenticated,
  children,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  children: ReactNode
}) {
  const [drafts, setDrafts] = useState<DraftRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      const draft = await callAdmin<{ rows: DraftRow[] }>(target, 'schema.getDraft')
      setDrafts(draft.rows)
      setError(null)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '불러오지 못했습니다.')
    }
  }, [target, onUnauthenticated])

  useEffect(() => {
    let alive = true
    let inFlight = false
    setDrafts(null) // 프로젝트를 바꾸면 새 값이 올 때까지 이전 프로젝트 목록을 보여주지 않는다.
    setError(null)
    const poll = async (force = false) => {
      if (inFlight || (!force && document.hidden)) return
      inFlight = true
      try {
        const draft = await callAdmin<{ rows: DraftRow[] }>(target, 'schema.getDraft')
        if (alive) { setDrafts(draft.rows); setError(null) }
      } catch (e: unknown) {
        if (!alive) return
        if (e instanceof NotAuthenticatedError) onUnauthenticated()
        else setError(e instanceof Error ? e.message : '불러오지 못했습니다.')
      } finally {
        inFlight = false
      }
    }
    // 최초 1회는 탭이 백그라운드(document.hidden)여도 무조건 불러온다 — 안 그러면
    // 화면을 처음 열었을 때 하필 탭이 안 보이는 상태면 drafts 가 null 에서 영영 안 벗어난다.
    void poll(true)
    const timer = window.setInterval(() => poll(), 5000)
    // 탭이 백그라운드일 땐 위에서 건너뛰므로, 다시 돌아왔을 때 바로 한 번 갱신한다.
    const onVisible = () => { if (!document.hidden) void poll() }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      alive = false
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [target, onUnauthenticated])

  return <PendingChangesContext.Provider value={{ drafts, error, reload }}>{children}</PendingChangesContext.Provider>
}

export function usePendingChanges(): Ctx {
  const ctx = useContext(PendingChangesContext)
  if (!ctx) throw new Error('usePendingChanges 는 PendingChangesProvider 안에서만 쓸 수 있습니다.')
  return ctx
}
