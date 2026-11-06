// 미게시 변경(draft) 목록 — 사이드바 배지·배너와 각 관리 화면(플레이어 데이터 필드·원격 설정·
// 리더보드·변경 관리)이 전부 같은 schema.getDraft 를 따로 불러오고 있었다. 여기 하나로
// 모으고, 화면들은 구독만 한다 — 한 화면에서 변경을 담거나 취소하면 사이드바 배지도 같이 갱신된다.
// 주기적으로 묻지 않는다: 화면을 띄워 두기만 해도 탭 하나가 시간당 720건을 부르던 곳이다(2026-11-05).
// 다른 운영자의 변경은 탭으로 돌아올 때·변경 관리에 들어올 때·게시와 전체 취소 창을 열 때 받는다.
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
  /** 새 목록을 돌려준다. 못 받았으면 null. */
  reload: () => Promise<DraftRow[] | null>
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
      return draft.rows
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return null }
      setError(e instanceof Error ? e.message : '불러오지 못했습니다.')
      return null
    }
  }, [target, onUnauthenticated])

  useEffect(() => {
    let alive = true
    let inFlight = false
    setDrafts(null) // 프로젝트를 바꾸면 새 값이 올 때까지 이전 프로젝트 목록을 보여주지 않는다.
    setError(null)
    const load = async () => {
      if (inFlight) return
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
    void load()
    const onVisible = () => { if (!document.hidden) void load() }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      alive = false
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
