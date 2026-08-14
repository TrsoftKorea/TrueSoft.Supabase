import { useState } from 'react'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import LeaderboardsTab from './leaderboard/LeaderboardsTab'
import ColumnsTab from './leaderboard/ColumnsTab'
import ScoresTab from './leaderboard/ScoresTab'

type Tab = 'boards' | 'columns' | 'scores'

export default function Leaderboard({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [tab, setTab] = useState<Tab>('boards')
  const [selectedCode, setSelectedCode] = useState('')

  return (
    <div className="space-y-4">
      <PageHeader
        title="리더보드"
        description="게임에서 사용할 랭킹을 만들고 기록을 관리합니다."
      />

      <div className="border-b border-neutral-200 flex gap-1">
        {(
          [
            ['boards', '리더보드'],
            ['columns', '필드'],
            ['scores', '기록'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={[
              'h-10 px-4 text-sm border-b-2 -mb-px transition-colors',
              tab === key
                ? 'border-[#1677ff] text-[#1677ff] font-medium'
                : 'border-transparent text-neutral-600 hover:text-neutral-900',
            ].join(' ')}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'boards' && (
        <LeaderboardsTab target={target} onUnauthenticated={onUnauthenticated} selectedCode={selectedCode} onSelect={setSelectedCode} />
      )}
      {tab === 'columns' && (
        <ColumnsTab target={target} onUnauthenticated={onUnauthenticated} selectedCode={selectedCode} onSelect={setSelectedCode} />
      )}
      {tab === 'scores' && (
        <ScoresTab target={target} onUnauthenticated={onUnauthenticated} selectedCode={selectedCode} onSelect={setSelectedCode} />
      )}
    </div>
  )
}
