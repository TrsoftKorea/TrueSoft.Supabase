import { useEffect, useState, useCallback } from 'react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { TableFooter } from '../../components/ui/TableFooter'
import { TableCard, type Column } from '../../components/ui/DataTable'
import { StatusText, type Tone } from '../../components/ui/StatusText'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime, formatTotal, itemsText } from '../../components/ui/format'

type GameItem = { key: string; display_name: string }

type MailRow = {
  id: string
  category: string
  title: string
  items: { key: string; count: number }[] | null
  items_claimed_at: string | null
  deleted_at: string | null
  expires_at: string
  created_at: string
}
type MailData = { rows: MailRow[]; total: number; pageSize: number }

// 우편 내역 화면의 상태 구분에 "기한 만료"를 더한다 — 문의 대응에서는 "왜 못 받았나"가 가장 먼저 나온다.
function statusOf(r: MailRow): { label: string; tone: Tone } {
  if (r.deleted_at) return { label: '삭제', tone: 'muted' }
  if (r.items_claimed_at) return { label: '수령', tone: 'ok' }
  if (new Date(r.expires_at).getTime() <= Date.now()) return { label: '기한 만료', tone: 'warn' }
  return { label: '미수령', tone: 'plain' }
}

export default function MailsTab({
  target,
  onUnauthenticated,
  accountId,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
}) {
  const [page, setPage] = useState(1)
  const [data, setData] = useState<MailData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)
  const [catalog, setCatalog] = useState<GameItem[]>([])

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      // 검색어 칸이 계정 ID 도 찾는다. 전체 ID 를 넣으므로 이 사람 것만 걸린다.
      setData(await callAdmin<MailData>(target, 'mails.getRecords', { search: accountId, page }))
    } catch (e: unknown) {
      report(e, '받은 우편을 불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, accountId, page, report])

  useEffect(() => { void load() }, [load])

  useEffect(() => {
    callAdmin<GameItem[]>(target, 'gameItems.list')
      .then(setCatalog)
      .catch((e: unknown) => report(e, '아이템 목록을 불러오지 못했습니다.'))
  }, [target, report])

  const columns: Column<MailRow>[] = [
    { key: 'at', header: '받은 시각', kind: 'datetime', cell: (r) => formatDateTime(r.created_at) },
    { key: 'title', header: '제목', kind: 'text', cell: (r) => r.title },
    { key: 'items', header: '보상', kind: 'text', cell: (r) => itemsText(r.items, catalog) },
    { key: 'status', header: '상태', kind: 'status', cell: (r) => { const st = statusOf(r); return <StatusText tone={st.tone}>{st.label}</StatusText> } },
    { key: 'expires', header: '수령 기한', kind: 'datetime', cell: (r) => formatDateTime(r.expires_at) },
  ]

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / (data?.pageSize ?? 20)))

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <TableCard
        columns={columns}
        rows={rows}
        rowKey={(r) => r.id}
        loading={loading}
        failed={failed}
        emptyText="받은 우편이 없습니다."
        footer={<TableFooter summary={<>{formatTotal(total)} · 보관 기간이 지난 우편은 빠집니다</>} page={page} totalPages={totalPages} onPageChange={setPage} />}
      />
    </div>
  )
}
