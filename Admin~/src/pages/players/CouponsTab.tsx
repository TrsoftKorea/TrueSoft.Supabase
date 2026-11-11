import { useEffect, useState, useCallback } from 'react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { TableCard, type Column } from '../../components/ui/DataTable'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime } from '../../components/ui/format'

type RedemptionRow = { code: string; kind: string; title: string; mail_id: string | null; redeemed_at: string }

const COLUMNS: Column<RedemptionRow>[] = [
  { key: 'at', header: '사용 시각', kind: 'datetime', cell: (r) => formatDateTime(r.redeemed_at) },
  { key: 'title', header: '쿠폰', kind: 'name', cell: (r) => r.title },
  { key: 'code', header: '코드', kind: 'mono', cell: (r) => r.code },
  { key: 'mail', header: '보상 우편', kind: 'name', cell: (r) => (r.mail_id ? '보냄' : null) },
]

export default function CouponsTab({
  target,
  onUnauthenticated,
  accountId,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
}) {
  const [rows, setRows] = useState<RedemptionRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  useEffect(() => {
    let alive = true
    setLoading(true)
    setFailed(false)
    callAdmin<RedemptionRow[]>(target, 'players.coupons', { accountId })
      .then((d) => { if (alive) setRows(d) })
      .catch((e: unknown) => { if (alive) { setFailed(true); report(e, '쿠폰 사용 이력을 불러오지 못했습니다.') } })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [target, accountId, report])

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <TableCard
        columns={COLUMNS}
        rows={rows}
        rowKey={(r, i) => `${r.code}-${r.redeemed_at}-${i}`}
        loading={loading}
        failed={failed}
        emptyText="사용한 쿠폰이 없습니다."
        footer={<div className="border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">최근 100건 · 지운 쿠폰의 사용 기록은 빠집니다</div>}
      />
    </div>
  )
}
