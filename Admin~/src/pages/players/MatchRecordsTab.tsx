import { useCallback, useEffect, useState } from 'react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { TableFooter } from '../../components/ui/TableFooter'
import { CardStack, TableCard, type Column } from '../../components/ui/DataTable'
import { StatusText, type Tone } from '../../components/ui/StatusText'
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
const STATUS_TONE: Record<Status, Tone> = {
  verified: 'ok', pending: 'warn', solo: 'plain', mismatch: 'bad',
}

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

      <CardStack>
        <TableCard
          title="요약"
          columns={SUMMARY_COLUMNS}
          rows={shownSummary}
          rowKey={(s) => `${s.game_code}/${s.status}`}
          loading={summaryLoading}
          failed={failed}
          emptyText="대전 기록이 없습니다."
        />

        <TableCard
          title="기록"
          columns={RECORD_COLUMNS}
          rows={rows}
          rowKey={(r) => r.session_id}
          loading={loading}
          failed={failed}
          emptyText="대전 기록이 없습니다."
          footer={<TableFooter summary={formatTotal(total)} page={page} totalPages={totalPages} onPageChange={setPage} />}
        />
      </CardStack>
    </div>
  )
}

const statusCell = (s: Status) => <StatusText tone={STATUS_TONE[s] ?? 'plain'}>{STATUS_LABEL[s] ?? s}</StatusText>
const score = (v: number | null) => (v == null ? null : v.toLocaleString())

const SUMMARY_COLUMNS: Column<SummaryRow>[] = [
  { key: 'code', header: '게임 코드', kind: 'mono', cell: (s) => s.game_code },
  { key: 'status', header: '상태', kind: 'status', cell: (s) => statusCell(s.status) },
  { key: 'wins', header: '승', kind: 'number', cell: (s) => s.wins.toLocaleString() },
  { key: 'losses', header: '패', kind: 'number', cell: (s) => s.losses.toLocaleString() },
  { key: 'max', header: '최고 점수', kind: 'number', cell: (s) => score(s.max_score) },
  { key: 'min', header: '최저 점수', kind: 'number', cell: (s) => score(s.min_score) },
]

const RECORD_COLUMNS: Column<RecordRow>[] = [
  // 세션 ID 는 시각에 마우스를 올리면 보인다.
  { key: 'at', header: '신고 시각', kind: 'datetime', cell: (r) => <span title={`세션 ${r.session_id}`}>{formatDateTime(r.reported_at)}</span> },
  { key: 'code', header: '게임 코드', kind: 'mono', cell: (r) => r.game_code },
  { key: 'win', header: '결과', kind: 'status', cell: (r) => <span className={r.is_win ? 'text-[#1677ff]' : 'text-neutral-500'}>{r.is_win ? '승' : '패'}</span> },
  { key: 'score', header: '점수', kind: 'number', cell: (r) => score(r.score) },
  { key: 'status', header: '상태', kind: 'status', cell: (r) => statusCell(r.status) },
  {
    key: 'opponent', header: '상대', kind: 'name', cell: (r) => (
      r.opponent_account_id && !r.opponent_exists
        ? <span className="text-neutral-400" title={r.opponent_account_id}>탈퇴한 계정</span>
        : r.opponent_account_id ? <PlayerLink accountId={r.opponent_account_id} name={r.opponent_name} /> : null
    ),
  },
  { key: 'reward', header: '보상 우편', kind: 'name', cell: (r) => (r.rewarded ? '보냄' : null) },
]
