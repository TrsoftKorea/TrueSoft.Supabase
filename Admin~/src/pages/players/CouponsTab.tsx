import { useEffect, useState, useCallback } from 'react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime } from '../../components/ui/format'

type RedemptionRow = { code: string; kind: string; title: string; mail_id: string | null; redeemed_at: string }

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
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  useEffect(() => {
    let alive = true
    setLoading(true)
    callAdmin<RedemptionRow[]>(target, 'players.coupons', { accountId })
      .then((d) => { if (alive) setRows(d) })
      .catch((e: unknown) => { if (alive) report(e, '쿠폰 사용 이력을 불러오지 못했습니다.') })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [target, accountId, report])

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">사용 시각</th>
                <th className="text-left px-4 py-2.5 font-medium">쿠폰</th>
                <th className="text-left px-4 py-2.5 font-medium">코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">보상 우편</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} colSpan={4} emptyText="사용한 쿠폰이 없습니다." />
              ) : (
                rows.map((r, i) => (
                  <tr key={`${r.code}-${r.redeemed_at}-${i}`} className="border-t border-neutral-100">
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.redeemed_at)}</td>
                    <td className="px-4 py-3 text-neutral-800">{r.title || '-'}</td>
                    <td className="px-4 py-3 font-mono text-xs text-neutral-700">{r.code}</td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{r.mail_id ? '보냄' : '-'}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <div className="border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          최근 100건 · 삭제한 쿠폰의 사용 이력은 함께 지워져 나오지 않습니다
        </div>
      </WhiteCard>
    </div>
  )
}
