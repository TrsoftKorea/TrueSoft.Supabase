import { useCallback, useEffect, useState } from 'react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { CardHeader, WhiteCard } from '../../components/ui/Card'
import { TableFooter } from '../../components/ui/TableFooter'
import { TableStatusRow } from '../../components/ui/TableStatusRow'
import { PlayerLink } from '../../components/ui/PlayerLink'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { formatDateTime, formatTotal } from '../../components/ui/format'
import { inputCls } from '../../components/ui/Input'
import { RefreshButton } from '../../components/ui/FilterBar'

type Status = 'verified' | 'pending' | 'solo' | 'mismatch'

type SummaryRow = {
  game_code: string; status: Status
  wins: number; losses: number; max_score: number | null; min_score: number | null
}
type RecordRow = {
  session_id: string; game_code: string; is_win: boolean; score: number | null; status: Status
  rewarded: boolean; opponent_account_id: string | null; opponent_exists: boolean; opponent_name: string | null
  reported_at: string; resolved_at: string | null
}
type Data = { summary: SummaryRow[]; rows: RecordRow[]; total: number; pageSize: number }

const STATUS_LABEL: Record<Status, string> = {
  verified: '확인됨', pending: '상대 미신고', solo: '상대 없음', mismatch: '무효',
}
const STATUS_CLS: Record<Status, string> = {
  verified: 'text-emerald-600', pending: 'text-amber-600', solo: 'text-neutral-500', mismatch: 'text-red-600',
}

const fmtScore = (v: number | null) => (v == null ? '-' : v.toLocaleString())

export default function MatchRecordsTab({
  target,
  onUnauthenticated,
  accountId,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
}) {
  const [data, setData] = useState<Data | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)
  const [gameCode, setGameCode] = useState('')
  const [page, setPage] = useState(1)
  // 선택지는 마지막으로 성공한 요약에서 만든다 — 불러오기가 실패해도 고른 코드가 드롭다운에서 사라지지 않게.
  const [codes, setCodes] = useState<string[]>([])

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      const d = await callAdmin<Data>(target, 'matchRecords.player', { accountId, gameCode, page })
      setData(d)
      setCodes(Array.from(new Set(d.summary.map((s) => s.game_code))))
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '대전 기록을 불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, accountId, gameCode, page, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  const summary = data?.summary ?? []
  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 20
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const codeOptions = gameCode && !codes.includes(gameCode) ? [...codes, gameCode] : codes
  const shownSummary = gameCode ? summary.filter((s) => s.game_code === gameCode) : summary
  // 요약은 쪽 번호와 무관하다 — 쪽을 넘길 때는 이미 보이는 요약을 그대로 둔다.
  const summaryLoading = loading && summary.length === 0

  return (
    <div className="space-y-4">
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="flex items-center gap-2">
        <select
          className={inputCls}
          value={gameCode}
          onChange={(e) => { setGameCode(e.target.value); setPage(1) }}
        >
          <option value="">모든 게임 코드</option>
          {codeOptions.map((c) => (<option key={c} value={c}>{c}</option>))}
        </select>
        <RefreshButton onClick={() => void load()} loading={loading} />
      </div>

      <WhiteCard>
        <div className="p-5 pb-3">
          <CardHeader title="요약" description="게임 코드와 상태마다 승패와 점수 범위를 봅니다." />
        </div>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-44">게임 코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">상태</th>
                <th className="text-right px-4 py-2.5 font-medium w-20">승</th>
                <th className="text-right px-4 py-2.5 font-medium w-20">패</th>
                <th className="text-right px-4 py-2.5 font-medium w-28">최고 점수</th>
                <th className="text-right px-4 py-2.5 font-medium">최저 점수</th>
              </tr>
            </thead>
            <tbody>
              {summaryLoading || shownSummary.length === 0 ? (
                <TableStatusRow loading={summaryLoading} empty={shownSummary.length === 0} failed={failed} colSpan={6} emptyText="대전 기록이 없습니다." />
              ) : (
                shownSummary.map((s) => (
                  <tr key={`${s.game_code}/${s.status}`} className="border-t border-neutral-100">
                    <td className="px-4 py-3 font-mono text-neutral-700 whitespace-nowrap">{s.game_code}</td>
                    <td className={`px-4 py-3 whitespace-nowrap ${STATUS_CLS[s.status] ?? ''}`}>{STATUS_LABEL[s.status] ?? s.status}</td>
                    <td className="px-4 py-3 text-right text-neutral-700 whitespace-nowrap">{s.wins.toLocaleString()}</td>
                    <td className="px-4 py-3 text-right text-neutral-700 whitespace-nowrap">{s.losses.toLocaleString()}</td>
                    <td className="px-4 py-3 text-right text-neutral-600 whitespace-nowrap">{fmtScore(s.max_score)}</td>
                    <td className="px-4 py-3 text-right text-neutral-600 whitespace-nowrap">{fmtScore(s.min_score)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="p-5 pb-3">
          <CardHeader title="기록" description="이 플레이어가 신고한 대전을 최근 순으로 봅니다." />
        </div>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">신고 시각</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">게임 코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-16">결과</th>
                <th className="text-right px-4 py-2.5 font-medium w-24">점수</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">상태</th>
                <th className="text-left px-4 py-2.5 font-medium">상대</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">보상 우편</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={7} emptyText="대전 기록이 없습니다." />
              ) : (
                rows.map((r) => (
                  <tr key={r.session_id} className="border-t border-neutral-100" title={`세션 ${r.session_id}`}>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.reported_at)}</td>
                    <td className="px-4 py-3 font-mono text-neutral-700 whitespace-nowrap">{r.game_code}</td>
                    <td className={`px-4 py-3 whitespace-nowrap ${r.is_win ? 'text-[#1677ff]' : 'text-neutral-500'}`}>{r.is_win ? '승' : '패'}</td>
                    <td className="px-4 py-3 text-right text-neutral-600 whitespace-nowrap">{fmtScore(r.score)}</td>
                    <td className={`px-4 py-3 whitespace-nowrap ${STATUS_CLS[r.status] ?? ''}`}>{STATUS_LABEL[r.status] ?? r.status}</td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      {r.opponent_account_id && !r.opponent_exists ? (
                        <span className="text-neutral-400" title={r.opponent_account_id}>탈퇴한 계정</span>
                      ) : r.opponent_account_id ? (
                        <PlayerLink accountId={r.opponent_account_id} name={r.opponent_name} />
                      ) : (
                        <span className="text-neutral-400">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{r.rewarded ? '보냄' : ''}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />
      </WhiteCard>
    </div>
  )
}
